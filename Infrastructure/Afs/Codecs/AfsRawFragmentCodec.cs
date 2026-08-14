using System.Buffers.Binary;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>
/// 가변 길이 GNSS RAW 레코드를 SB3/SB4에 넣을 수 있는 105바이트 블록으로 분할하고 복원한다.
/// 각 블록은 19바이트 헤더와 최대 86바이트의 RAW payload로 구성된다.
/// </summary>
public static class AfsRawFragmentCodec
{
    public const int BlockBytes = 105;
    public const int HeaderBytes = 19;
    public const int PayloadBytes = BlockBytes - HeaderBytes;
    public const int CustomMessageType = 63;
    private const byte CurrentVersion = 1;
    private const byte StartFlag = 1;
    private const byte EndFlag = 2;

    public static IReadOnlyList<byte[]> Fragment(uint recordSequence, ReadOnlySpan<byte> record)
    {
        if (record.IsEmpty) throw new InvalidDataException("GNSS RAW record is empty.");
        var count = checked((int)Math.Ceiling(record.Length / (double)PayloadBytes));
        if (count > ushort.MaxValue) throw new InvalidDataException("GNSS RAW record requires too many AFS fragments.");
        // 모든 조각에 원본 레코드의 길이와 CRC32를 기록하여 재조립 완료 후 무결성을 확인한다.
        var crc = Hashing.Crc32(record); var result = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            var offset = i * PayloadBytes; var length = Math.Min(PayloadBytes, record.Length - offset);
            var block = new byte[BlockBytes]; block[0] = CurrentVersion;
            block[1] = (byte)((i == 0 ? StartFlag : 0) | (i == count - 1 ? EndFlag : 0));
            BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(2), recordSequence);
            BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(6), checked((ushort)i));
            BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(8), checked((ushort)count));
            BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(10), checked((uint)record.Length));
            block[14] = checked((byte)length); BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(15), crc);
            record.Slice(offset, length).CopyTo(block.AsSpan(HeaderBytes)); result.Add(block);
        }
        return result;
    }

    public static AfsRawFragment DecodeBlock(ReadOnlySpan<byte> block)
    {
        if (block.Length != BlockBytes) throw new InvalidDataException("AFS custom block must be 105 bytes.");
        if (block[0] != CurrentVersion) throw new InvalidDataException($"Unsupported AFS custom block version {block[0]}.");
        var length = block[14]; if (length > PayloadBytes) throw new InvalidDataException("Invalid AFS fragment payload length.");
        var count = BinaryPrimitives.ReadUInt16BigEndian(block[8..]); var index = BinaryPrimitives.ReadUInt16BigEndian(block[6..]);
        if (count == 0 || index >= count) throw new InvalidDataException("Invalid AFS fragment index.");
        return new(block[0], block[1], BinaryPrimitives.ReadUInt32BigEndian(block[2..]), index, count,
            BinaryPrimitives.ReadUInt32BigEndian(block[10..]), length, BinaryPrimitives.ReadUInt32BigEndian(block[15..]),
            block.Slice(HeaderBytes, length).ToArray());
    }

    public static byte[] ToSbBits(ReadOnlySpan<byte> block, int messageType = CustomMessageType)
    {
        // SB 데이터는 바이트 패킹이 아닌 값이 0 또는 1인 unpacked bit 배열을 사용한다.
        if (block.Length != BlockBytes || messageType is < 0 or > 63) throw new ArgumentOutOfRangeException(nameof(messageType));
        var bits = new byte[846]; for (var i = 0; i < 6; i++) bits[i] = (byte)((messageType >> (5 - i)) & 1);
        for (var i = 0; i < block.Length * 8; i++) bits[6 + i] = (byte)((block[i >> 3] >> (7 - (i & 7))) & 1);
        return bits;
    }

    public static byte[] FromSbBits(ReadOnlySpan<byte> bits, int expectedType = CustomMessageType)
    {
        if (bits.Length != 846) throw new InvalidDataException("SB3/SB4 data must contain 846 unpacked bits.");
        var type = 0; for (var i = 0; i < 6; i++) { if (bits[i] > 1) throw new InvalidDataException("SB bits must be 0 or 1."); type = (type << 1) | bits[i]; }
        if (type != expectedType) throw new InvalidDataException($"Unexpected AFS custom message type {type}.");
        var block = new byte[BlockBytes];
        for (var i = 0; i < block.Length * 8; i++) { if (bits[6 + i] > 1) throw new InvalidDataException("SB bits must be 0 or 1."); block[i >> 3] |= (byte)(bits[6 + i] << (7 - (i & 7))); }
        return block;
    }
}

/// <summary>SB3/SB4에서 복호한 RAW fragment를 원래 레코드 순서로 재조립한다.</summary>
public sealed class AfsRawReassembler
{
    // 레코드 순서별로 조각을 보관하여 UDP 도착 순서와 무관하게 원래 순서로 복원한다.
    private readonly SortedDictionary<uint, RecordState> records = [];

    public void Add(AfsRawFragment fragment)
    {
        if (!records.TryGetValue(fragment.RecordSequence, out var state))
            records.Add(fragment.RecordSequence, state = new(fragment.FragmentCount, fragment.RecordLength, fragment.RecordCrc32));
        state.Add(fragment);
    }

    public IReadOnlyList<(uint Sequence, byte[] Record)> CompleteRecords()
    {
        var result = new List<(uint, byte[])>();
        foreach (var pair in records)
            if (pair.Value.TryBuild(out var record)) result.Add((pair.Key, record));
        return result;
    }

    public IReadOnlyList<uint> IncompleteRecords() => records.Where(x => !x.Value.IsComplete).Select(x => x.Key).ToArray();

    private sealed class RecordState(ushort count, uint length, uint crc)
    {
        private readonly byte[][] fragments = new byte[count][];
        public bool IsComplete => fragments.All(x => x is not null);
        public void Add(AfsRawFragment f)
        {
            if (f.FragmentCount != count || f.RecordLength != length || f.RecordCrc32 != crc) throw new InvalidDataException("Conflicting AFS fragment metadata.");
            var existing = fragments[f.FragmentIndex];
            if (existing is not null && !existing.AsSpan().SequenceEqual(f.Payload)) throw new InvalidDataException("Conflicting duplicate AFS fragment.");
            fragments[f.FragmentIndex] = f.Payload;
        }
        public bool TryBuild(out byte[] record)
        {
            record = []; if (!IsComplete) return false;
            var joined = fragments.SelectMany(x => x).ToArray(); if (joined.Length != length) throw new InvalidDataException("Reassembled GNSS RAW length mismatch.");
            if (Hashing.Crc32(joined) != crc) throw new InvalidDataException("Reassembled GNSS RAW CRC32 mismatch.");
            record = joined; return true;
        }
    }
}

