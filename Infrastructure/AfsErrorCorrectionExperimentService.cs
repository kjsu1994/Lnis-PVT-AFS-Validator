using System.Globalization;
using System.Text;
using System.Text.Json;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

public sealed class AfsErrorCorrectionExperimentService(Func<IAfsFrameCodec>? codecFactory = null)
{
    private readonly Func<IAfsFrameCodec> codecFactory = codecFactory ?? (() => new AfsNativeCodec());
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task<AfsErrorCorrectionExperimentResult> RunAsync(
        AfsErrorCorrectionExperimentSettings settings,
        string resultRoot,
        IProgress<AfsSessionProgress>? progress,
        CancellationToken token)
    {
        Validate(settings);
        Directory.CreateDirectory(resultRoot);

        // 실제 AFS 프레임 형식 자체의 오류정정 능력을 분리해서 보기 위해
        // 네트워크를 거치지 않고 Encode → 오류 주입 → Decode 순서로 반복한다.
        var sb2 = CreateTestBits(1176, unchecked((uint)settings.Seed));
        var sb3 = CreateTestBits(846, unchecked((uint)settings.Seed + 1));
        var sb4 = CreateTestBits(846, unchecked((uint)settings.Seed + 2));
        var trials = new List<AfsErrorCorrectionTrialResult>();
        var total = settings.ErrorCounts.Count * settings.TrialsPerCondition;
        var completed = 0;
        var directory = Path.Combine(resultRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}-afs-fec");
        var frameDirectory = Path.Combine(directory, "frames");
        Directory.CreateDirectory(frameDirectory);

        // CRC 입력 데이터도 실제 파일로 보관한다. 한 바이트에 0 또는 1 한 비트를 저장하므로
        // 문서의 1176/846비트 배열과 인덱스를 그대로 대조할 수 있다.
        await File.WriteAllBytesAsync(Path.Combine(directory, "reference-sb2.bits"), sb2, token);
        await File.WriteAllBytesAsync(Path.Combine(directory, "reference-sb3.bits"), sb3, token);
        await File.WriteAllBytesAsync(Path.Combine(directory, "reference-sb4.bits"), sb4, token);
        await File.WriteAllTextAsync(Path.Combine(directory, "실험데이터_파일설명.txt"),
            "AFS 오류정정 반복시험 파일 설명\r\n\r\n" +
            "reference-sb*.bits : CRC 입력용 unpacked 0/1 비트 배열\r\n" +
            "*-reference.afs   : 오류 주입 전 6000심볼 MSB-first 패킹 프레임(750바이트)\r\n" +
            "*-injected.afs    : 오류 주입 후 실제 복호기에 입력한 프레임(750바이트)\r\n" +
            "*-flipped-symbols.txt : 반전한 심볼의 0 기준 인덱스\r\n" +
            "*-decoded-sb*.bits : 복호기가 출력한 unpacked 비트 배열\r\n" +
            "fec-summary.csv   : 오류 개수 조건별 성공률 요약\r\n" +
            "fec-trials.csv    : 모든 반복시험의 상세 결과\r\n" +
            "fec-result.json   : 설정, 요약, 상세 결과 전체\r\n",
            Encoding.UTF8, token);

        await using var codec = codecFactory();
        foreach (var errorCount in settings.ErrorCounts)
        {
            for (var trial = 0; trial < settings.TrialsPerCondition; trial++)
            {
                token.ThrowIfCancellationRequested();
                var toi = trial % 100;
                var encoded = await codec.EncodeAsync(toi, sb2, sb3, sb4, token);
                var injection = AfsErrorInjector.Inject(encoded,
                    new(settings.Mode, errorCount, settings.Seed), trial);
                await SaveFrameFilesAsync(frameDirectory, settings.Mode, errorCount, trial,
                    encoded, injection, token);
                trials.Add(await DecodeTrialAsync(codec, settings, errorCount, trial, toi,
                    injection, sb2, sb3, sb4, frameDirectory, token));

                completed++;
                progress?.Report(new("오류정정 반복시험", 100.0 * completed / total,
                    $"{errorCount}심볼 조건 {trial + 1}/{settings.TrialsPerCondition}회"));
            }
        }

        var summaries = trials.GroupBy(x => x.ErrorCount).Select(CreateSummary).ToArray();
        var result = new AfsErrorCorrectionExperimentResult(DateTimeOffset.UtcNow, settings, summaries, trials, directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "fec-result.json"), JsonSerializer.Serialize(result, Json), Encoding.UTF8, token);
        await WriteCsvAsync(directory, summaries, trials, token);
        return result;
    }

    private static async Task SaveFrameFilesAsync(string root, AfsErrorInjectionMode mode,
        int errorCount, int trial, byte[] referenceFrame, AfsErrorInjectionResult injection, CancellationToken token)
    {
        var conditionDirectory = Path.Combine(root, $"{mode}-{errorCount:D4}");
        Directory.CreateDirectory(conditionDirectory);
        var prefix = $"trial-{trial + 1:D4}";

        // .afs 파일은 헤더 없는 6000심볼 MSB-first 패킹 데이터이며 항상 750바이트다.
        // reference와 injected를 함께 저장해 어떤 비트가 바뀌었는지 직접 비교할 수 있다.
        await File.WriteAllBytesAsync(Path.Combine(conditionDirectory, $"{prefix}-reference.afs"), referenceFrame, token);
        await File.WriteAllBytesAsync(Path.Combine(conditionDirectory, $"{prefix}-injected.afs"), injection.Frame, token);
        await File.WriteAllTextAsync(Path.Combine(conditionDirectory, $"{prefix}-flipped-symbols.txt"),
            string.Join(Environment.NewLine, injection.FlippedSymbolIndices), Encoding.UTF8, token);
    }

    private static async Task<AfsErrorCorrectionTrialResult> DecodeTrialAsync(
        IAfsFrameCodec codec, AfsErrorCorrectionExperimentSettings settings, int errorCount,
        int trial, int toi, AfsErrorInjectionResult injection,
        byte[] expectedSb2, byte[] expectedSb3, byte[] expectedSb4,
        string frameDirectory, CancellationToken token)
    {
        try
        {
            var decoded = await codec.DecodeAsync(toi, injection.Frame, token);
            var conditionDirectory = Path.Combine(frameDirectory, $"{settings.Mode}-{errorCount:D4}");
            var prefix = $"trial-{trial + 1:D4}";
            // 복호 배열도 unpacked bit 파일로 남겨 CRC 성공 여부와 원본 데이터를 직접 대조한다.
            await File.WriteAllBytesAsync(Path.Combine(conditionDirectory, $"{prefix}-decoded-sb2.bits"), decoded.Sb2Bits, token);
            await File.WriteAllBytesAsync(Path.Combine(conditionDirectory, $"{prefix}-decoded-sb3.bits"), decoded.Sb3Bits, token);
            await File.WriteAllBytesAsync(Path.Combine(conditionDirectory, $"{prefix}-decoded-sb4.bits"), decoded.Sb4Bits, token);
            var restored = decoded.Sb2Valid && decoded.Sb3Valid && decoded.Sb4Valid &&
                decoded.Sb2Bits.SequenceEqual(expectedSb2) && decoded.Sb3Bits.SequenceEqual(expectedSb3) && decoded.Sb4Bits.SequenceEqual(expectedSb4);
            return new(settings.Mode, errorCount, trial + 1, settings.Seed, true,
                decoded.Sb2Corrections >= 0, decoded.Sb3Corrections >= 0, decoded.Sb4Corrections >= 0,
                decoded.Sb2Valid, decoded.Sb3Valid, decoded.Sb4Valid,
                decoded.Sb2Corrections, decoded.Sb3Corrections, decoded.Sb4Corrections,
                restored, string.Join(' ', injection.FlippedSymbolIndices), null);
        }
        catch (InvalidOperationException ex) when (settings.Mode == AfsErrorInjectionMode.SyncLoss)
        {
            // SP가 훼손되면 현재 프레임 단위 DLL은 동기를 거부한다.
            // 이는 예상된 Sync Loss 결과이며 프로그램 전체 오류로 처리하지 않는다.
            return new(settings.Mode, errorCount, trial + 1, settings.Seed, false,
                false, false, false, false, false, false, -1, -1, -1, false,
                string.Join(' ', injection.FlippedSymbolIndices), ex.Message);
        }
    }

    private static AfsErrorCorrectionSummary CreateSummary(IGrouping<int, AfsErrorCorrectionTrialResult> group)
    {
        var rows = group.ToArray();
        var blockCount = rows.Length * 3.0;
        var changed = rows.SelectMany(x => new[] { x.Sb2ChangedBits, x.Sb3ChangedBits, x.Sb4ChangedBits }).Where(x => x >= 0).ToArray();
        return new(rows[0].Mode, group.Key, rows.Length,
            Percent(rows.Count(x => x.SyncAccepted), rows.Length),
            Percent(rows.Sum(x => Bool(x.Sb2LdpcSuccess) + Bool(x.Sb3LdpcSuccess) + Bool(x.Sb4LdpcSuccess)), blockCount),
            Percent(rows.Sum(x => Bool(x.Sb2CrcSuccess) + Bool(x.Sb3CrcSuccess) + Bool(x.Sb4CrcSuccess)), blockCount),
            Percent(rows.Count(x => x.DataRestored), rows.Length),
            changed.Length == 0 ? 0 : changed.Average());
    }

    private static byte[] CreateTestBits(int length, uint state)
    {
        // 외부 Random 구현 변화에 영향을 받지 않는 xorshift32로 매 실행의 입력을 재현한다.
        if (state == 0) state = 0x6D2B79F5;
        var bits = new byte[length];
        for (var i = 0; i < bits.Length; i++)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            bits[i] = (byte)(state & 1);
        }
        return bits;
    }

    private static async Task WriteCsvAsync(string directory, IReadOnlyList<AfsErrorCorrectionSummary> summaries,
        IReadOnlyList<AfsErrorCorrectionTrialResult> trials, CancellationToken token)
    {
        static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        var summaryLines = new[] { "Mode,ErrorCount,Trials,SyncAcceptanceRate,LdpcSuccessRate,CrcSuccessRate,FrameRestoreRate,AverageChangedBits" }
            .Concat(summaries.Select(x => $"{x.Mode},{x.ErrorCount},{x.Trials},{N(x.SyncAcceptanceRate)},{N(x.LdpcSuccessRate)},{N(x.CrcSuccessRate)},{N(x.FrameRestoreRate)},{N(x.AverageChangedBits)}"));
        await File.WriteAllLinesAsync(Path.Combine(directory, "fec-summary.csv"), summaryLines, Encoding.UTF8, token);
        var trialLines = new[] { "Mode,ErrorCount,Trial,Seed,SyncAccepted,Sb2Ldpc,Sb3Ldpc,Sb4Ldpc,Sb2Crc,Sb3Crc,Sb4Crc,Sb2ChangedBits,Sb3ChangedBits,Sb4ChangedBits,DataRestored,FlippedSymbols,Detail" }
            .Concat(trials.Select(x => $"{x.Mode},{x.ErrorCount},{x.TrialNumber},{x.Seed},{x.SyncAccepted},{x.Sb2LdpcSuccess},{x.Sb3LdpcSuccess},{x.Sb4LdpcSuccess},{x.Sb2CrcSuccess},{x.Sb3CrcSuccess},{x.Sb4CrcSuccess},{x.Sb2ChangedBits},{x.Sb3ChangedBits},{x.Sb4ChangedBits},{x.DataRestored},\"{x.FlippedSymbols}\",\"{Escape(x.Detail)}\""));
        await File.WriteAllLinesAsync(Path.Combine(directory, "fec-trials.csv"), trialLines, Encoding.UTF8, token);
        static string Escape(string? value) => (value ?? string.Empty).Replace("\"", "\"\"");
    }

    private static void Validate(AfsErrorCorrectionExperimentSettings settings)
    {
        if (settings.Mode == AfsErrorInjectionMode.None) throw new ArgumentException("오류 유형을 선택해야 합니다.");
        if (settings.ErrorCounts.Count == 0) throw new ArgumentException("오류 개수를 하나 이상 입력해야 합니다.");
        if (settings.ErrorCounts.Any(x => x <= 0)) throw new ArgumentException("오류 개수는 1 이상이어야 합니다.");
        if (settings.TrialsPerCondition is < 1 or > 10000) throw new ArgumentException("반복 횟수는 1~10000 범위여야 합니다.");
        var maximum = settings.Mode == AfsErrorInjectionMode.SyncLoss ? 68 : 5880;
        if (settings.ErrorCounts.Any(x => x > maximum)) throw new ArgumentException($"{settings.Mode} 오류 개수는 {maximum}을 초과할 수 없습니다.");
    }

    private static int Bool(bool value) => value ? 1 : 0;
    private static double Percent(double numerator, double denominator) => denominator <= 0 ? 0 : 100.0 * numerator / denominator;
}
