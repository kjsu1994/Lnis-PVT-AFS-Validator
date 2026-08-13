using System.Buffers.Binary;
using System.IO.Ports;
using System.Text.Json;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>GNSS source를 읽어 정규화 레코드 파일, 원본 UBX와 manifest를 생성한다.</summary>
public sealed class GnssCaptureService(IGnssRawCodec? rawCodec = null) : IGnssCaptureService
{
    private readonly IGnssRawCodec codec = rawCodec ?? new GnssRawBinaryCodec();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task<GnssCaptureResult> CaptureSerialAsync(string portName, int baudRate, string sessionName, string resultRoot, IProgress<GnssCaptureProgress>? progress, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(portName)) throw new ArgumentException("Serial port is required.", nameof(portName));
        if (baudRate <= 0) throw new ArgumentOutOfRangeException(nameof(baudRate));
        var serial = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One) { Handshake = Handshake.None, ReadTimeout = 1000 };
        serial.Open();
        using var registration = token.Register(() => { try { serial.Close(); } catch { } });
        return await CaptureAsync(serial.BaseStream, portName, baudRate, sessionName, resultRoot, serial, progress, token);
    }

    public Task<GnssCaptureResult> ReplayFileAsync(string sourcePath, string sessionName, string resultRoot, IProgress<GnssCaptureProgress>? progress, CancellationToken token)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("UBX replay file was not found.", sourcePath);
        var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        return CaptureAsync(stream, Path.GetFileName(sourcePath), 0, sessionName, resultRoot, stream, progress, token);
    }

    private async Task<GnssCaptureResult> CaptureAsync(Stream input, string sourceName, int baudRate, string sessionName, string resultRoot, IDisposable owner, IProgress<GnssCaptureProgress>? progress, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(sessionName)) sessionName = "GNSS capture";
        var testId = Guid.NewGuid(); var directory = Path.Combine(resultRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{testId:N}-gnss"); Directory.CreateDirectory(directory);
        var rawPartial = Path.Combine(directory, "capture.ubx.partial"); var canonicalPartial = Path.Combine(directory, "capture.graw.partial"); var manifestPath = Path.Combine(directory, "capture-manifest.json");
        var started = DateTimeOffset.UtcNow; long bytes = 0, valid = 0, rawx = 0, sfrbx = 0, envelopes = 0, malformed = 0; string? error = null; var completed = false;
        var parser = new UbxFrameParser(); var mapper = new UbxGnssMapper(testId, "u-blox ZED-F9P", "unknown", sourceName, baudRate, sessionName);
        try
        {
            await using var raw = new FileStream(rawPartial, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, true);
            await using var canonical = new FileStream(canonicalPartial, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, true);
            async ValueTask WriteEnvelope(GnssRawEnvelope e, CancellationToken ct)
            {
                var data = codec.Encode(e); var size = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(size, (uint)data.Length);
                await canonical.WriteAsync(size, ct); await canonical.WriteAsync(data, ct); envelopes++;
            }
            await WriteEnvelope(mapper.Metadata(started), token);
            await foreach (var frame in UbxStreamReader.ReadAsync(input, parser, async (data, ct) => await raw.WriteAsync(data, ct), n => bytes += n, token))
            {
                valid++; if (frame.MessageClass == 0x02 && frame.MessageId == 0x15) rawx++; else if (frame.MessageClass == 0x02 && frame.MessageId == 0x13) sfrbx++;
                try { var envelope = mapper.Map(frame, DateTimeOffset.UtcNow); if (envelope is not null) await WriteEnvelope(envelope, token); }
                catch (InvalidDataException) { malformed++; }
                Report("Capturing GNSS RAW");
            }
            await raw.FlushAsync(token); await canonical.FlushAsync(token); completed = true;
        }
        catch (OperationCanceledException) { error = "Cancelled"; }
        catch (Exception ex) { error = token.IsCancellationRequested ? "Cancelled" : ex.Message; }
        finally { owner.Dispose(); }

        var rawFinal = Path.Combine(directory, "capture.ubx"); var canonicalFinal = Path.Combine(directory, "capture.graw");
        if (completed) { File.Move(rawPartial, rawFinal); File.Move(canonicalPartial, canonicalFinal); } else { rawFinal = rawPartial; canonicalFinal = canonicalPartial; }
        var statistics = Stats();
        var manifest = new
        {
            SchemaVersion = 1, TestId = testId, SessionName = sessionName, Source = sourceName, BaudRate = baudRate,
            StartedAtUtc = started, CompletedAtUtc = DateTimeOffset.UtcNow, Completed = completed, Error = error, Statistics = statistics,
            MalformedPayloads = malformed, RawFile = Path.GetFileName(rawFinal), CanonicalFile = Path.GetFileName(canonicalFinal),
            RawSha256 = File.Exists(rawFinal) ? await Hashing.Sha256Async(rawFinal, CancellationToken.None) : null,
            CanonicalSha256 = File.Exists(canonicalFinal) ? await Hashing.Sha256Async(canonicalFinal, CancellationToken.None) : null
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, Json), CancellationToken.None); Report(completed ? "Capture completed" : error ?? "Capture failed");
        return new(directory, rawFinal, canonicalFinal, manifestPath, statistics, completed, error);

        GnssCaptureStatistics Stats() => new(bytes, valid, rawx, sfrbx, parser.ChecksumErrors, mapper.UnsupportedFrames + malformed, mapper.UnsupportedConstellations, envelopes);
        void Report(string message) => progress?.Report(new(Stats(), message));
    }
}

/// <summary>길이 prefix가 붙은 정규화 GNSS 레코드 파일을 순차적으로 읽는다.</summary>
public static class GnssCanonicalFile
{
    public static async IAsyncEnumerable<GnssRawEnvelope> ReadAsync(string path, IGnssRawCodec? codec = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
    {
        codec ??= new GnssRawBinaryCodec(); await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true); var size = new byte[4];
        while (stream.Position < stream.Length)
        {
            await stream.ReadExactlyAsync(size, token); var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(size));
            if (length <= 0 || length > 1024 * 1024 + 128) throw new InvalidDataException("Invalid canonical GNSS record length.");
            var data = new byte[length]; await stream.ReadExactlyAsync(data, token); yield return codec.Decode(data);
        }
    }
}
