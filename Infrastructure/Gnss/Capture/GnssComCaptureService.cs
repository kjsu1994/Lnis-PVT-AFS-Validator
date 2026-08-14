using System.Buffers.Binary;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>COM 원본을 보존하면서 선택된 프로토콜의 메시지를 정규화 capture.graw로 기록한다.</summary>
public sealed class GnssComCaptureService(
    IGnssByteSourceFactory? sourceFactory = null,
    IGnssProtocolAdapterCatalog? protocolCatalog = null) : IGnssCaptureService
{
    private readonly IGnssByteSourceFactory sourceFactory = sourceFactory ?? new SerialPortGnssByteSourceFactory();
    private readonly IGnssProtocolAdapterCatalog protocolCatalog = protocolCatalog ?? new GnssProtocolAdapterCatalog();
    private readonly GnssRawBinaryCodec codec = new();

    public async Task<GnssCaptureResult> CaptureAsync(
        GnssSerialCaptureSettings settings,
        IProgress<GnssCaptureProgress>? progress,
        CancellationToken cancellationToken)
    {
        Validate(settings);
        var adapter = protocolCatalog.Create(settings.ProtocolId);
        adapter.Reset();
        var directory = CreateResultDirectory(settings.OutputRoot);
        var rawPath = Path.Combine(directory, "serial-input.bin");
        var canonicalPath = adapter.Descriptor.ProducesCanonicalRaw ? Path.Combine(directory, "capture.graw") : string.Empty;
        var statistics = new GnssCaptureStatistics(0, 0, 0, 0, 0, 0, 0, 0);
        var completed = false;
        string? error = null;

        try
        {
            await using var source = await sourceFactory.OpenAsync(settings, cancellationToken);
            await using var raw = new FileStream(rawPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, true);
            await using var canonical = adapter.Descriptor.ProducesCanonicalRaw
                ? new FileStream(canonicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, true)
                : null;
            ulong sequence = 0;
            var testId = Guid.NewGuid();

            if (canonical is not null)
            {
                var metadata = new ReceiverMetadataMessage(settings.ReceiverModel, settings.FirmwareVersion, settings.PortName, settings.BaudRate, settings.SessionName);
                await WriteEnvelopeAsync(canonical, new GnssRawEnvelope(1, testId, Guid.NewGuid(), sequence++, DateTimeOffset.UtcNow, metadata), cancellationToken);
                statistics = statistics with { ValidFrames = 1, EnvelopesWritten = 1 };
            }

            progress?.Report(new(statistics, $"수집 시작: {source.Description} / {adapter.Descriptor.DisplayName}"));
            var buffer = new byte[8192];
            while (true)
            {
                var count = await source.ReadAsync(buffer, cancellationToken);
                if (count == 0) break;
                await raw.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                statistics = statistics with { BytesRead = statistics.BytesRead + count };

                foreach (var message in adapter.Push(buffer.AsSpan(0, count)))
                {
                    if (canonical is null) throw new InvalidOperationException("A raw-only adapter cannot emit canonical messages.");
                    await WriteEnvelopeAsync(canonical, new GnssRawEnvelope(1, testId, Guid.NewGuid(), sequence++, DateTimeOffset.UtcNow, message), cancellationToken);
                    statistics = Count(statistics, message);
                }
                progress?.Report(new(statistics, $"{statistics.BytesRead:N0}바이트 수신, {statistics.EnvelopesWritten:N0}개 RAW 레코드"));
            }
            completed = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completed = true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        var summaryMessage = error is not null
            ? $"수집 실패: {error}"
            : adapter.Descriptor.ProducesCanonicalRaw
                ? $"수집 종료: capture.graw {statistics.EnvelopesWritten:N0}개 레코드"
                : "수집 종료: 프로토콜 미정이므로 원본 serial-input.bin만 저장했습니다.";
        progress?.Report(new(statistics, summaryMessage));
        return new(directory, rawPath, canonicalPath, string.Empty, statistics, completed, error);
    }

    private async Task WriteEnvelopeAsync(Stream output, GnssRawEnvelope envelope, CancellationToken token)
    {
        var record = codec.Encode(envelope);
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)record.Length));
        await output.WriteAsync(length, token);
        await output.WriteAsync(record, token);
    }

    private static GnssCaptureStatistics Count(GnssCaptureStatistics value, GnssRawMessage message) => message switch
    {
        ObservationEpochMessage => value with { ValidFrames = value.ValidFrames + 1, ObservationEpochs = value.ObservationEpochs + 1, EnvelopesWritten = value.EnvelopesWritten + 1 },
        NavigationUpdateMessage => value with { ValidFrames = value.ValidFrames + 1, NavigationUpdates = value.NavigationUpdates + 1, EnvelopesWritten = value.EnvelopesWritten + 1 },
        _ => value with { ValidFrames = value.ValidFrames + 1, EnvelopesWritten = value.EnvelopesWritten + 1 }
    };

    private static string CreateResultDirectory(string root)
    {
        Directory.CreateDirectory(root);
        var directory = Path.Combine(root, $"Capture-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Validate(GnssSerialCaptureSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.PortName)) throw new ArgumentException("COM 포트를 선택하세요.");
        if (settings.BaudRate is < 1200 or > 4_000_000) throw new ArgumentOutOfRangeException(nameof(settings.BaudRate));
        if (string.IsNullOrWhiteSpace(settings.ProtocolId)) throw new ArgumentException("프로토콜 어댑터를 선택하세요.");
        if (string.IsNullOrWhiteSpace(settings.OutputRoot)) throw new ArgumentException("저장 폴더를 선택하세요.");
    }
}
