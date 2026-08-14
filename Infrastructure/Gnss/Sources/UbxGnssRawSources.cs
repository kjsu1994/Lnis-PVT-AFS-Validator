using System.IO.Ports;
using System.Runtime.CompilerServices;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>저장된 UBX 파일을 GNSS RAW envelope 스트림으로 제공한다.</summary>
public sealed class FileUbxGnssRawSource(string path, Guid testId, string sessionName) : IGnssRawSource
{
    public async IAsyncEnumerable<GnssRawEnvelope> ReadAsync([EnumeratorCancellation] CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true); var mapper = new UbxGnssMapper(testId, "u-blox ZED-F9P", "unknown", Path.GetFileName(path), 0, sessionName); yield return mapper.Metadata(DateTimeOffset.UtcNow); var parser = new UbxFrameParser();
        await foreach (var frame in UbxStreamReader.ReadAsync(stream, parser, token: token)) { GnssRawEnvelope? envelope; try { envelope = mapper.Map(frame, DateTimeOffset.UtcNow); } catch (InvalidDataException) { continue; } if (envelope is not null) yield return envelope; }
    }
}

/// <summary>지정한 시리얼 포트에서 UBX 메시지를 실시간으로 읽는다.</summary>
public sealed class SerialUbxGnssRawSource(string portName, int baudRate, Guid testId, string sessionName) : IGnssRawSource
{
    public async IAsyncEnumerable<GnssRawEnvelope> ReadAsync([EnumeratorCancellation] CancellationToken token)
    {
        using var serial = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One) { Handshake = Handshake.None }; serial.Open(); using var registration = token.Register(() => { try { serial.Close(); } catch { } });
        var mapper = new UbxGnssMapper(testId, "u-blox ZED-F9P", "unknown", portName, baudRate, sessionName); yield return mapper.Metadata(DateTimeOffset.UtcNow); var parser = new UbxFrameParser();
        await foreach (var frame in UbxStreamReader.ReadAsync(serial.BaseStream, parser, token: token)) { GnssRawEnvelope? envelope; try { envelope = mapper.Map(frame, DateTimeOffset.UtcNow); } catch (InvalidDataException) { continue; } if (envelope is not null) yield return envelope; }
    }
}
