using System.Buffers.Binary;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>송신할 한 AFS 프레임의 시각 식별자와 6000심볼 payload를 보관한다.</summary>
public sealed record AfsTransmitFrame(
    ushort Week,
    ushort IntervalOfWeek,
    byte TimeOfInterval,
    byte[] Payload);

/// <summary>입력 RAW에서 생성한 AFS 프레임과 SessionStart에 필요한 원본 정보를 묶는다.</summary>
public sealed record AfsPreparedFrames(
    long SourceLength,
    string SourceSha256,
    int RecordCount,
    IReadOnlyList<AfsTransmitFrame> Frames,
    int InjectedFrameCount);

/// <summary>RAW fragment 구성, AFS 프레임 부호화·복호와 Test B/C/D 오류 주입을 담당한다.</summary>
public sealed class AfsFrameService(Func<IAfsFrameCodec> codecFactory)
{
    public async Task<AfsPreparedFrames> PrepareAsync(
        AfsSenderSettings settings,
        IProgress<AfsSessionProgress>? progress,
        CancellationToken token)
    {
        var records = await ReadRecordsAsync(settings.CapturePath, token);
        var sourceHash = await Hashing.Sha256Async(settings.CapturePath, token);
        var sourceLength = new FileInfo(settings.CapturePath).Length;
        var (week, intervalOfWeek, timeOfInterval) = TimeFrom(records);
        var blocks = records
            .SelectMany((record, index) => AfsRawFragmentCodec.Fragment(checked((uint)index), record))
            .ToArray();
        var totalFrames = (blocks.Length + 1) / 2;
        var frames = new List<AfsTransmitFrame>(totalFrames);
        var injectedFrameCount = 0;

        await using var codec = codecFactory();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex += 2)
        {
            token.ThrowIfCancellationRequested();
            // 홀수 fragment 수에서는 마지막 fragment를 두 SB에 동일하게 넣어 기존 wire 동작을 유지한다.
            var second = blockIndex + 1 < blocks.Length ? blocks[blockIndex + 1] : blocks[blockIndex];
            var sb2 = AfsSb2Builder.BuildValidationPattern(week, intervalOfWeek);
            var sb3 = AfsRawFragmentCodec.ToSbBits(blocks[blockIndex]);
            var sb4 = AfsRawFragmentCodec.ToSbBits(second);
            var encoded = await codec.EncodeAsync(timeOfInterval, sb2, sb3, sb4, token);
            var injected = ApplyFrameErrors(encoded, settings, frames.Count, totalFrames);
            if (injected.FlippedSymbolIndices.Count > 0) injectedFrameCount++;

            frames.Add(new(week, intervalOfWeek, timeOfInterval, injected.Frame));
            Advance(ref week, ref intervalOfWeek, ref timeOfInterval);
            progress?.Report(new(
                "Encoding",
                35.0 * (blockIndex + 2) / Math.Max(1, blocks.Length),
                $"Encoded {frames.Count} AFS frames"));
        }

        if (settings.TestType == AfsEndToEndTestType.TestD_SyncRecovery && frames.Count < 2)
            throw new InvalidDataException("Test D requires at least two AFS frames so the next synchronization pattern can be recovered.");

        return new(sourceLength, sourceHash, records.Count, frames, injectedFrameCount);
    }

    public AfsFrameReceiver CreateReceiver(int customMessageType) =>
        new(codecFactory(), customMessageType);

    private static AfsErrorInjectionResult ApplyFrameErrors(
        byte[] frame,
        AfsSenderSettings settings,
        int frameIndex,
        int totalFrames)
    {
        var mode = settings.TestType switch
        {
            AfsEndToEndTestType.TestB_RandomErrors => AfsErrorInjectionMode.Random,
            AfsEndToEndTestType.TestC_BurstErrors => AfsErrorInjectionMode.Burst,
            AfsEndToEndTestType.TestD_SyncRecovery
                when frameIndex < totalFrames - 1 && frameIndex % settings.SyncDamageInterval == 0
                => AfsErrorInjectionMode.SyncLoss,
            _ => AfsErrorInjectionMode.None
        };
        var errorCount = mode == AfsErrorInjectionMode.None ? 0 : settings.ErrorCount;
        return AfsErrorInjector.Inject(
            frame,
            new(mode, errorCount, settings.ErrorSeed),
            frameIndex);
    }

    private static async Task<List<byte[]>> ReadRecordsAsync(string path, CancellationToken token)
    {
        var records = new List<byte[]>();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        var size = new byte[4];
        while (stream.Position < stream.Length)
        {
            await stream.ReadExactlyAsync(size, token);
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(size));
            if (length is <= 0 or > 1_048_704)
                throw new InvalidDataException("Invalid capture.graw record length.");
            var record = new byte[length];
            await stream.ReadExactlyAsync(record, token);
            records.Add(record);
        }

        if (records.Count == 0) throw new InvalidDataException("capture.graw is empty.");
        return records;
    }

    private static (ushort Week, ushort IntervalOfWeek, byte TimeOfInterval) TimeFrom(
        IReadOnlyList<byte[]> records)
    {
        var codec = new GnssRawBinaryCodec();
        foreach (var record in records)
        {
            var envelope = codec.Decode(record);
            if (envelope.Message is ObservationEpochMessage observation)
                return NextTime(observation.Week, observation.ReceiverTowSeconds);
        }

        var first = codec.Decode(records[0]);
        var gps = first.CapturedAtUtc.AddSeconds(18) -
                  new DateTimeOffset(1980, 1, 6, 0, 0, 0, TimeSpan.Zero);
        return NextTime(checked((ushort)(gps.TotalDays / 7)), gps.TotalSeconds % 604800);
    }

    private static (ushort Week, ushort IntervalOfWeek, byte TimeOfInterval) NextTime(
        ushort week,
        double secondsOfWeek)
    {
        var intervalOfWeek = (ushort)(secondsOfWeek / 1200);
        var timeOfInterval = (int)(secondsOfWeek % 1200) / 12 + 1;
        if (timeOfInterval < 100) return (week, intervalOfWeek, (byte)timeOfInterval);
        timeOfInterval = 0;
        if (++intervalOfWeek < 504) return (week, intervalOfWeek, 0);
        return (checked((ushort)(week + 1)), 0, 0);
    }

    private static void Advance(
        ref ushort week,
        ref ushort intervalOfWeek,
        ref byte timeOfInterval)
    {
        if (++timeOfInterval < 100) return;
        timeOfInterval = 0;
        if (++intervalOfWeek < 504) return;
        intervalOfWeek = 0;
        week++;
    }
}

/// <summary>한 수신 세션의 코덱, RAW 재조립 상태와 블록별 복호 계수를 관리한다.</summary>
public sealed class AfsFrameReceiver(
    IAfsFrameCodec codec,
    int customMessageType) : IAsyncDisposable
{
    private readonly AfsRawReassembler reassembler = new();

    public long DecodedFrames { get; private set; }
    public long Sb2ValidFrames { get; private set; }
    public long Sb3ValidFrames { get; private set; }
    public long Sb4ValidFrames { get; private set; }
    public long CorrectedSymbols { get; private set; }
    public long CorruptFrames { get; private set; }
    public long RecoveredSyncFrames { get; private set; }

    public async Task DecodeAsync(int timeOfInterval, byte[] frame, CancellationToken token)
    {
        try
        {
            var decoded = await codec.DecodeAsync(timeOfInterval, frame, token);
            DecodedFrames++;
            if (decoded.Sb2Valid) Sb2ValidFrames++;
            if (decoded.Sb3Valid) Sb3ValidFrames++;
            if (decoded.Sb4Valid) Sb4ValidFrames++;
            CorrectedSymbols +=
                Math.Max(0, decoded.Sb2Corrections) +
                Math.Max(0, decoded.Sb3Corrections) +
                Math.Max(0, decoded.Sb4Corrections);

            if (!decoded.Sb3Valid || !decoded.Sb4Valid)
            {
                CorruptFrames++;
                return;
            }

            reassembler.Add(AfsRawFragmentCodec.DecodeBlock(
                AfsRawFragmentCodec.FromSbBits(decoded.Sb3Bits, customMessageType)));
            reassembler.Add(AfsRawFragmentCodec.DecodeBlock(
                AfsRawFragmentCodec.FromSbBits(decoded.Sb4Bits, customMessageType)));
        }
        catch (InvalidOperationException) { CorruptFrames++; }
        catch (InvalidDataException) { CorruptFrames++; }
    }

    public async Task RecoverSynchronizedAsync(
        IReadOnlyList<AfsPacket> orderedFrames,
        CancellationToken token)
    {
        if (orderedFrames.Count == 0) return;

        var packedStream = orderedFrames.SelectMany(packet => packet.Payload).ToArray();
        var offsets = AfsFrameSynchronizer.FindSyncOffsets(
            packedStream,
            (long)packedStream.Length * 8);
        RecoveredSyncFrames = offsets.Count;
        foreach (var symbolOffset in offsets)
        {
            var sourceIndex = checked((int)(symbolOffset / AfsErrorInjector.FrameSymbolCount));
            if (sourceIndex >= orderedFrames.Count) continue;
            await DecodeAsync(
                orderedFrames[sourceIndex].TimeOfInterval,
                AfsFrameSynchronizer.ExtractFrame(packedStream, symbolOffset),
                token);
        }
    }

    public IReadOnlyList<(uint Sequence, byte[] Record)> CompleteRecords() =>
        reassembler.CompleteRecords();

    public int IncompleteRecordCount => reassembler.IncompleteRecords().Count;

    public ValueTask DisposeAsync() => codec.DisposeAsync();
}
