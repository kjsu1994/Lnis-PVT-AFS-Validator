using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>수신 결과 패킷에 직렬화되는 최소 판정·성능 정보를 보관한다.</summary>
public sealed record AfsWireResult(
    Verdict Verdict,
    RawIntegrityResult Integrity,
    AfsEndToEndTestType TestType,
    long ExpectedFrames,
    long ReceivedFrames,
    long ReceivedDatagrams,
    long Duplicates,
    long Corrupt,
    long Probes,
    long ProbeResponses,
    double? AverageLatency,
    double? MaximumLatency,
    long DecodedFrames,
    long Sb2ValidFrames,
    long Sb3ValidFrames,
    long Sb4ValidFrames,
    long CorrectedSymbols,
    long RecoveredSyncFrames,
    double CpuAverage,
    double CpuMaximum,
    double MemoryAverage,
    double MemoryMaximum,
    string ResultDirectory,
    string? Error);

/// <summary>계산된 성능지표와 최종 시험 판정을 함께 반환한다.</summary>
public sealed record AfsEvaluation(
    IReadOnlyList<PerformanceMetric> Metrics,
    Verdict Verdict);

/// <summary>Test A~E 합격 조건, 표시 조건과 공통 성능지표를 순수 계산한다.</summary>
public sealed class AfsTestEvaluator
{
    public string TestConditions(AfsSessionManifest manifest) => manifest.TestType switch
    {
        AfsEndToEndTestType.TestB_RandomErrors =>
            $"Random {manifest.ErrorCount} symbols, seed {manifest.ErrorSeed}",
        AfsEndToEndTestType.TestC_BurstErrors =>
            $"Burst {manifest.ErrorCount} symbols, seed {manifest.ErrorSeed}",
        AfsEndToEndTestType.TestD_SyncRecovery =>
            $"SP {manifest.ErrorCount} symbols every {manifest.SyncDamageInterval} frames, seed {manifest.ErrorSeed}",
        AfsEndToEndTestType.TestE_UdpDrop =>
            $"UDP drop {manifest.SimulatedDropRatePercent:0.###}%, seed {manifest.SimulatedDropSeed}",
        _ => "Normal AFS transmission"
    };

    public AfsWireResult CreateReceiverWireResult(
        AfsSessionManifest manifest,
        AfsNetworkCounters counters,
        RawIntegrityResult integrity,
        AfsFrameReceiver frames,
        IReadOnlyList<ResourceSample> samples,
        string resultDirectory)
    {
        var expectedSyncFrames = manifest.FrameCount - manifest.InjectedFrameCount;
        var testPassed = manifest.TestType == AfsEndToEndTestType.TestD_SyncRecovery
            ? counters.ReceivedLogicalFrames == manifest.FrameCount &&
              frames.RecoveredSyncFrames == expectedSyncFrames &&
              frames.DecodedFrames == expectedSyncFrames
            : integrity.Success;

        return new(
            testPassed ? Verdict.Pass : Verdict.Fail,
            integrity,
            manifest.TestType,
            manifest.FrameCount,
            counters.ReceivedLogicalFrames,
            counters.ReceivedDatagrams,
            counters.DuplicateDatagrams,
            counters.CorruptDatagrams,
            counters.ProbeAttempts,
            counters.ProbeResponses,
            counters.OneWayLatencyMilliseconds.Count == 0
                ? null
                : counters.OneWayLatencyMilliseconds.Average(),
            counters.OneWayLatencyMilliseconds.Count == 0
                ? null
                : counters.OneWayLatencyMilliseconds.Max(),
            frames.DecodedFrames,
            frames.Sb2ValidFrames,
            frames.Sb3ValidFrames,
            frames.Sb4ValidFrames,
            frames.CorrectedSymbols,
            frames.RecoveredSyncFrames,
            samples.Count == 0 ? 0 : samples.Average(sample => sample.CpuPercent),
            samples.Count == 0 ? 0 : samples.Max(sample => sample.CpuPercent),
            samples.Count == 0 ? 0 : samples.Average(sample => sample.WorkingSetBytes),
            samples.Count == 0 ? 0 : samples.Max(sample => sample.WorkingSetBytes),
            resultDirectory,
            testPassed ? null : integrity.Detail);
    }

    public AfsEvaluation Evaluate(
        AfsNetworkCounters counters,
        IReadOnlyDictionary<string, MetricThreshold>? thresholds,
        AfsWireResult wireResult)
    {
        var metrics = AfsPerformanceCalculator
            .Calculate(counters, thresholds)
            .Concat(ResultMetrics(wireResult))
            .ToArray();
        var verdict = wireResult.Verdict == Verdict.Fail ||
                      metrics.Any(metric => metric.Status == MetricStatus.Fail)
            ? Verdict.Fail
            : wireResult.Verdict;
        return new(metrics, verdict);
    }

    private static IEnumerable<PerformanceMetric> ResultMetrics(AfsWireResult result)
    {
        yield return new(
            PerformanceCategory.DataIntegrity,
            "DecodedFrames",
            "AFS 복호를 수행한 프레임 수",
            "frame",
            result.DecodedFrames,
            MetricStatus.Measured);
        yield return new(
            PerformanceCategory.DataIntegrity,
            "Sb2CrcValidFrames",
            "SB2 CRC-24Q 통과 프레임 수",
            "frame",
            result.Sb2ValidFrames,
            MetricStatus.Measured);
        yield return new(
            PerformanceCategory.DataIntegrity,
            "Sb3CrcValidFrames",
            "SB3 CRC-24Q 통과 프레임 수",
            "frame",
            result.Sb3ValidFrames,
            MetricStatus.Measured);
        yield return new(
            PerformanceCategory.DataIntegrity,
            "Sb4CrcValidFrames",
            "SB4 CRC-24Q 통과 프레임 수",
            "frame",
            result.Sb4ValidFrames,
            MetricStatus.Measured);
        yield return new(
            PerformanceCategory.DataIntegrity,
            "CorrectedSymbols",
            "LDPC 복호기가 정정한 심볼 합계",
            "symbol",
            result.CorrectedSymbols,
            MetricStatus.Measured);
        if (result.TestType == AfsEndToEndTestType.TestD_SyncRecovery)
        {
            yield return new(
                PerformanceCategory.DataIntegrity,
                "RecoveredSyncFrames",
                "연속 수신 심볼에서 SP를 다시 찾아 복구한 정상 프레임 수",
                "frame",
                result.RecoveredSyncFrames,
                MetricStatus.Measured);
        }
        yield return new(
            PerformanceCategory.System,
            "CpuAverage",
            "AFS 수신 처리 중 프로세서 평균 사용량",
            "%",
            result.CpuAverage,
            MetricStatus.Measured);
        yield return new(
            PerformanceCategory.System,
            "CpuMaximum",
            "AFS 수신 처리 중 프로세서 최대 사용량",
            "%",
            result.CpuMaximum,
            MetricStatus.Measured);
        yield return new(
            PerformanceCategory.System,
            "MemoryAverage",
            "AFS 수신 처리 중 평균 메모리 사용량",
            "byte",
            result.MemoryAverage,
            MetricStatus.Measured);
        yield return new(
            PerformanceCategory.System,
            "MemoryMaximum",
            "AFS 수신 처리 중 최대 메모리 사용량",
            "byte",
            result.MemoryMaximum,
            MetricStatus.Measured);
        yield return new(
            PerformanceCategory.System,
            "LogStorageRate",
            "시험데이터 기록 성공률",
            "%",
            100,
            MetricStatus.Measured);
    }
}
