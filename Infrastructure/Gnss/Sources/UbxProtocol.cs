using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>검증된 UBX class, id와 payload를 보관한다.</summary>
public sealed record UbxFrame(byte MessageClass, byte MessageId, byte[] Payload);

/// <summary>바이트 스트림에서 UBX sync, 길이와 checksum을 검사해 완전한 프레임을 추출한다.</summary>
public sealed class UbxFrameParser
{
    private readonly List<byte> buffer = [];
    public long ChecksumErrors { get; private set; }

    public IReadOnlyList<UbxFrame> Feed(ReadOnlySpan<byte> data)
    {
        foreach (var value in data) buffer.Add(value);
        var frames = new List<UbxFrame>(); var offset = 0;
        while (buffer.Count - offset >= 2)
        {
            while (offset + 1 < buffer.Count && (buffer[offset] != 0xB5 || buffer[offset + 1] != 0x62)) offset++;
            if (buffer.Count - offset < 6) break;
            var length = buffer[offset + 4] | buffer[offset + 5] << 8; var frameLength = 8 + length;
            if (buffer.Count - offset < frameLength) break;
            byte a = 0, b = 0;
            for (var i = offset + 2; i < offset + 6 + length; i++) { a += buffer[i]; b += a; }
            if (a != buffer[offset + 6 + length] || b != buffer[offset + 7 + length]) { ChecksumErrors++; offset++; continue; }
            frames.Add(new(buffer[offset + 2], buffer[offset + 3], buffer.GetRange(offset + 6, length).ToArray())); offset += frameLength;
        }
        if (offset > 0) buffer.RemoveRange(0, offset);
        return frames;
    }
}

/// <summary>지원하는 UBX 메시지를 프로그램의 정규화 GNSS RAW 모델로 변환한다.</summary>
public sealed class UbxGnssMapper(Guid testId, string receiverModel, string firmwareVersion, string portName, int baudRate, string sessionName)
{
    private ulong sequence;
    public long UnsupportedFrames { get; private set; }
    public long UnsupportedConstellations { get; private set; }
    public GnssRawEnvelope Metadata(DateTimeOffset capturedAt) => Wrap(capturedAt, new ReceiverMetadataMessage(receiverModel, firmwareVersion, portName, baudRate, sessionName));

    public GnssRawEnvelope? Map(UbxFrame frame, DateTimeOffset capturedAt)
    {
        if (frame.MessageClass != 0x02) { UnsupportedFrames++; return null; }
        return frame.MessageId switch { 0x15 => MapRawx(frame.Payload, capturedAt), 0x13 => MapSfrbx(frame.Payload, capturedAt), _ => Unsupported() };
    }

    private GnssRawEnvelope? MapRawx(ReadOnlySpan<byte> p, DateTimeOffset capturedAt)
    {
        if (p.Length < 16) throw new InvalidDataException("Truncated UBX-RXM-RAWX header.");
        var count = p[11]; if (p.Length != 16 + count * 32) throw new InvalidDataException("Invalid UBX-RXM-RAWX payload length.");
        var observations = new List<GnssObservation>(count);
        for (var i = 0; i < count; i++)
        {
            var x = p.Slice(16 + i * 32, 32); var constellation = Constellation(x[20]);
            if (constellation is null) { UnsupportedConstellations++; continue; }
            observations.Add(new(constellation.Value, x[21], x[22], x[23], F64(x), F64(x[8..]), F32(x[16..]), BinaryPrimitives.ReadUInt16LittleEndian(x[24..]), x[26], x[27], x[28], x[29], x[30]));
        }
        if (observations.Count == 0) return null;
        return Wrap(capturedAt, new ObservationEpochMessage(F64(p), BinaryPrimitives.ReadUInt16LittleEndian(p[8..]), unchecked((sbyte)p[10]), p[12], p[13], observations));
    }

    private GnssRawEnvelope? MapSfrbx(ReadOnlySpan<byte> p, DateTimeOffset capturedAt)
    {
        if (p.Length < 8) throw new InvalidDataException("Truncated UBX-RXM-SFRBX header.");
        var constellation = Constellation(p[0]); if (constellation is null) { UnsupportedConstellations++; return null; }
        var count = p[4]; if (p.Length != 8 + count * 4) throw new InvalidDataException("Invalid UBX-RXM-SFRBX payload length.");
        var words = new uint[count]; for (var i = 0; i < count; i++) words[i] = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(8 + i * 4, 4));
        return Wrap(capturedAt, new NavigationUpdateMessage(constellation.Value, p[1], p[2], p[3], p[6], words));
    }

    private GnssRawEnvelope Wrap(DateTimeOffset capturedAt, GnssRawMessage message) => new(1, testId, Guid.NewGuid(), sequence++, capturedAt.ToUniversalTime(), message);
    private GnssRawEnvelope? Unsupported() { UnsupportedFrames++; return null; }
    private static GnssConstellation? Constellation(byte id) => id switch { 0 => GnssConstellation.Gps, 2 => GnssConstellation.Galileo, _ => null };
    private static double F64(ReadOnlySpan<byte> p) => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(p));
    private static float F32(ReadOnlySpan<byte> p) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(p));
}

/// <summary>Stream에서 UBX 데이터를 읽고 parser가 완성한 프레임을 비동기로 반환한다.</summary>
public static class UbxStreamReader
{
    public static async IAsyncEnumerable<UbxFrame> ReadAsync(Stream stream, UbxFrameParser parser, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? rawSink = null, Action<int>? bytesRead = null, [EnumeratorCancellation] CancellationToken token = default)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, token); if (read == 0) yield break;
            bytesRead?.Invoke(read); if (rawSink is not null) await rawSink(buffer.AsMemory(0, read), token);
            foreach (var frame in parser.Feed(buffer.AsSpan(0, read))) yield return frame;
        }
    }
}
