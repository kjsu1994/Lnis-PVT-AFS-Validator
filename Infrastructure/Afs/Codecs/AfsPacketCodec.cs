using System.Buffers.Binary;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>
/// AFS UDP 패킷을 고정 48바이트 헤더와 payload, CRC32 형식으로 직렬화하고 역직렬화한다.
/// 다중 바이트 정수는 네트워크 전송 순서인 Big Endian으로 저장한다.
/// </summary>
public static class AfsPacketCodec
{
    private static ReadOnlySpan<byte> Magic => "LAFS"u8;
    private const byte Version = 1;
    private const ushort HeaderLength = 48;
    public const int MaximumPayloadLength = 1200;

    public static byte[] Encode(AfsPacket packet)
    {
        if (packet.Payload.Length > MaximumPayloadLength) throw new InvalidDataException("AFS UDP payload exceeds the protocol limit.");
        var output = new byte[HeaderLength + packet.Payload.Length + 4]; var span = output.AsSpan();
        Magic.CopyTo(span); span[4] = Version; span[5] = (byte)packet.Kind; BinaryPrimitives.WriteUInt16BigEndian(span[6..], HeaderLength);
        if (!packet.TestId.TryWriteBytes(span[8..24], true, out var written) || written != 16) throw new InvalidOperationException("Unable to encode TestId.");
        BinaryPrimitives.WriteUInt32BigEndian(span[24..], packet.Sequence); span[28] = packet.CopyIndex; span[29] = packet.Prn;
        BinaryPrimitives.WriteUInt16BigEndian(span[30..], packet.Week); BinaryPrimitives.WriteUInt16BigEndian(span[32..], packet.IntervalOfWeek);
        span[34] = packet.TimeOfInterval; span[35] = 0; BinaryPrimitives.WriteInt64BigEndian(span[36..], packet.SentUtcTicks);
        BinaryPrimitives.WriteUInt16BigEndian(span[44..], checked((ushort)packet.Payload.Length)); span[46] = span[47] = 0;
        // 마지막 4바이트의 CRC32는 헤더와 payload 전체를 대상으로 계산한다.
        packet.Payload.CopyTo(span[HeaderLength..]); BinaryPrimitives.WriteUInt32BigEndian(span[^4..], Hashing.Crc32(span[..^4])); return output;
    }

    public static AfsPacket Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength + 4 || !data[..4].SequenceEqual(Magic)) throw new InvalidDataException("Invalid AFS UDP magic.");
        if (data[4] != Version || BinaryPrimitives.ReadUInt16BigEndian(data[6..]) != HeaderLength || data[35] != 0 || data[46] != 0 || data[47] != 0)
            throw new InvalidDataException("Unsupported AFS UDP header.");
        var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(data[44..]);
        if (payloadLength > MaximumPayloadLength || data.Length != HeaderLength + payloadLength + 4) throw new InvalidDataException("Invalid AFS UDP payload length.");
        if (Hashing.Crc32(data[..^4]) != BinaryPrimitives.ReadUInt32BigEndian(data[^4..])) throw new InvalidDataException("AFS UDP CRC32 mismatch.");
        if (!Enum.IsDefined((AfsPacketKind)data[5])) throw new InvalidDataException("Unknown AFS UDP packet type.");
        return new((AfsPacketKind)data[5], new Guid(data[8..24], true), BinaryPrimitives.ReadUInt32BigEndian(data[24..]), data[28], data[29],
            BinaryPrimitives.ReadUInt16BigEndian(data[30..]), BinaryPrimitives.ReadUInt16BigEndian(data[32..]), data[34],
            BinaryPrimitives.ReadInt64BigEndian(data[36..]), data.Slice(HeaderLength, payloadLength).ToArray());
    }
}

public sealed class AfsPacketDeduplicator
{
    // CopyIndex는 키에 포함하지 않아 중복 송신된 같은 논리 패킷을 한 번만 수용한다.
    private readonly HashSet<(Guid TestId, AfsPacketKind Kind, uint Sequence)> received = [];
    public bool TryAccept(AfsPacket packet) => received.Add((packet.TestId, packet.Kind, packet.Sequence));
    public int Count => received.Count;
}

