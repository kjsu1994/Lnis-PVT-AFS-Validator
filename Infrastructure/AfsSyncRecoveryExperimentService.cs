using System.Globalization;
using System.Text;
using System.Text.Json;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>Test D의 정상·손상·정상 3프레임 스트림을 만들고 다음 정상 SP와 프레임 복구를 측정한다.</summary>
public sealed class AfsSyncRecoveryExperimentService(Func<IAfsFrameCodec>? codecFactory = null)
{
    private readonly Func<IAfsFrameCodec> codecFactory = codecFactory ?? (() => new AfsNativeCodec());
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task<AfsSyncRecoveryResult> RunAsync(AfsSyncRecoverySettings settings, string resultRoot,
        IProgress<AfsSessionProgress>? progress, CancellationToken token)
    {
        Validate(settings); Directory.CreateDirectory(resultRoot);
        var directory = Path.Combine(resultRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}-afs-sync");
        Directory.CreateDirectory(directory);
        var trials = new List<AfsSyncRecoveryTrial>();
        var sb2 = TestBits(1176, (uint)settings.Seed); var sb3 = TestBits(846, (uint)settings.Seed + 1); var sb4 = TestBits(846, (uint)settings.Seed + 2);
        var total = settings.SyncErrorCounts.Count * settings.TrialsPerCondition; var completed = 0;

        await using var codec = codecFactory();
        foreach (var errorCount in settings.SyncErrorCounts)
        {
            var conditionDirectory = Path.Combine(directory, $"SyncLoss-{errorCount:D2}");
            Directory.CreateDirectory(conditionDirectory);
            for (var trial = 0; trial < settings.TrialsPerCondition; trial++)
            {
                token.ThrowIfCancellationRequested();
                var firstToi = trial % 98;
                var first = await codec.EncodeAsync(firstToi, sb2, sb3, sb4, token);
                var damagedReference = await codec.EncodeAsync((firstToi + 1) % 100, sb2, sb3, sb4, token);
                var next = await codec.EncodeAsync((firstToi + 2) % 100, sb2, sb3, sb4, token);
                var damaged = AfsErrorInjector.Inject(damagedReference, new(AfsErrorInjectionMode.SyncLoss, errorCount, settings.Seed), trial);
                var stream = first.Concat(damaged.Frame).Concat(next).ToArray();
                var prefix = $"trial-{trial + 1:D4}";
                await File.WriteAllBytesAsync(Path.Combine(conditionDirectory, $"{prefix}-3frames.afsstream"), stream, token);
                await File.WriteAllBytesAsync(Path.Combine(conditionDirectory, $"{prefix}-damaged-reference.afs"), damagedReference, token);
                await File.WriteAllBytesAsync(Path.Combine(conditionDirectory, $"{prefix}-damaged.afs"), damaged.Frame, token);
                await File.WriteAllTextAsync(Path.Combine(conditionDirectory, $"{prefix}-flipped-sync-symbols.txt"), string.Join(Environment.NewLine, damaged.FlippedSymbolIndices), Encoding.UTF8, token);

                trials.Add(await EvaluateAsync(codec, settings, errorCount, trial, firstToi, stream, damaged, sb2, sb3, sb4, conditionDirectory, prefix, token));
                completed++; progress?.Report(new("Sync 재동기 시험", 100.0 * completed / total, $"SP {errorCount}심볼 훼손 {trial + 1}/{settings.TrialsPerCondition}회"));
            }
        }

        var summaries = trials.GroupBy(x => x.SyncErrorCount).Select(Summarize).ToArray();
        var result = new AfsSyncRecoveryResult(DateTimeOffset.UtcNow, settings, summaries, trials, directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "sync-result.json"), JsonSerializer.Serialize(result, Json), Encoding.UTF8, token);
        await WriteCsvAsync(directory, summaries, trials, token);
        await File.WriteAllTextAsync(Path.Combine(directory, "실험데이터_파일설명.txt"),
            "*.afsstream : 정상 → SP 훼손 → 정상 순서의 3개 AFS 프레임(2250바이트)\r\n" +
            "*-damaged-reference.afs : SP 훼손 전 두 번째 프레임(750바이트)\r\n" +
            "*-damaged.afs : SP가 훼손된 두 번째 프레임(750바이트)\r\n" +
            "*-recovered.afs : SP 재탐색 후 추출한 정상 프레임(750바이트)\r\n" +
            "Sync Recovery Time은 손상 프레임 시작부터 다음 정상 SP까지의 심볼 수 × 2ms입니다.\r\n", Encoding.UTF8, token);
        return result;
    }

    private static async Task<AfsSyncRecoveryTrial> EvaluateAsync(IAfsFrameCodec codec, AfsSyncRecoverySettings settings,
        int errorCount, int trial, int firstToi, byte[] stream, AfsErrorInjectionResult damaged,
        byte[] sb2, byte[] sb3, byte[] sb4, string directory, string prefix, CancellationToken token)
    {
        var rejected = false;
        try { await codec.DecodeAsync((firstToi + 1) % 100, damaged.Frame, token); }
        catch (InvalidOperationException) { rejected = true; }

        var offsets = AfsFrameSynchronizer.FindSyncOffsets(stream, stream.Length * 8L).Where(x => x > 6000).ToArray();
        foreach (var offset in offsets)
        {
            if (offset + AfsFrameSynchronizer.FrameSymbolCount > stream.Length * 8L) continue;
            var candidate = AfsFrameSynchronizer.ExtractFrame(stream, offset);
            var frameIndex = (int)(offset / AfsFrameSynchronizer.FrameSymbolCount);
            try
            {
                var decoded = await codec.DecodeAsync((firstToi + frameIndex) % 100, candidate, token);
                var restored = decoded.Sb2Valid && decoded.Sb3Valid && decoded.Sb4Valid && decoded.Sb2Bits.SequenceEqual(sb2) && decoded.Sb3Bits.SequenceEqual(sb3) && decoded.Sb4Bits.SequenceEqual(sb4);
                if (!restored) continue;
                await File.WriteAllBytesAsync(Path.Combine(directory, $"{prefix}-recovered.afs"), candidate, token);
                var seconds = (offset - 6000) * 0.002;
                return new(errorCount, trial + 1, rejected, true, true, Math.Max(0, frameIndex - 1), seconds, offset,
                    string.Join(' ', damaged.FlippedSymbolIndices), null);
            }
            catch (InvalidOperationException) { }
        }
        return new(errorCount, trial + 1, rejected, offsets.Length > 0, false, null, null, null,
            string.Join(' ', damaged.FlippedSymbolIndices), "다음 정상 프레임을 복호하지 못했습니다.");
    }

    private static AfsSyncRecoverySummary Summarize(IGrouping<int, AfsSyncRecoveryTrial> group)
    {
        var rows = group.ToArray(); var recovered = rows.Where(x => x.NextFrameDecoded).ToArray();
        return new(group.Key, rows.Length, Percent(rows.Count(x => x.DamagedFrameRejected), rows.Length),
            Percent(rows.Count(x => x.NextSyncFound), rows.Length), Percent(recovered.Length, rows.Length),
            recovered.Length == 0 ? null : recovered.Average(x => x.RecoveryTimeSeconds!.Value));
    }

    private static async Task WriteCsvAsync(string directory, IReadOnlyList<AfsSyncRecoverySummary> summaries, IReadOnlyList<AfsSyncRecoveryTrial> trials, CancellationToken token)
    {
        static string N(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
        await File.WriteAllLinesAsync(Path.Combine(directory, "sync-summary.csv"), new[] { "SyncErrorCount,Trials,DamagedFrameRejectionRate,SyncRecoveryRate,DecodeRecoveryRate,AverageRecoverySeconds" }
            .Concat(summaries.Select(x => $"{x.SyncErrorCount},{x.Trials},{N(x.DamagedFrameRejectionRate)},{N(x.SyncRecoveryRate)},{N(x.DecodeRecoveryRate)},{N(x.AverageRecoverySeconds)}")), Encoding.UTF8, token);
        await File.WriteAllLinesAsync(Path.Combine(directory, "sync-trials.csv"), new[] { "SyncErrorCount,Trial,DamagedFrameRejected,NextSyncFound,NextFrameDecoded,RecoveryFrameCount,RecoveryTimeSeconds,RecoveredBitOffset,FlippedSyncSymbols,Detail" }
            .Concat(trials.Select(x => $"{x.SyncErrorCount},{x.TrialNumber},{x.DamagedFrameRejected},{x.NextSyncFound},{x.NextFrameDecoded},{x.RecoveryFrameCount},{N(x.RecoveryTimeSeconds)},{x.RecoveredBitOffset},\"{x.FlippedSyncSymbols}\",\"{(x.Detail ?? "").Replace("\"", "\"\"")}\"")), Encoding.UTF8, token);
    }

    private static byte[] TestBits(int length, uint state) { if (state == 0) state = 0x6D2B79F5; var bits = new byte[length]; for (var i = 0; i < length; i++) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; bits[i] = (byte)(state & 1); } return bits; }
    private static double Percent(double numerator, double denominator) => denominator == 0 ? 0 : numerator / denominator * 100;
    private static void Validate(AfsSyncRecoverySettings settings) { if (settings.SyncErrorCounts.Count == 0 || settings.SyncErrorCounts.Any(x => x is < 1 or > 68)) throw new ArgumentException("Sync 오류 개수는 1~68 범위로 입력해야 합니다."); if (settings.TrialsPerCondition is < 1 or > 10000) throw new ArgumentException("반복 횟수는 1~10000 범위여야 합니다."); }
}
