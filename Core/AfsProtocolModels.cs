namespace LnisAfsValidator.Core;

// AFS 시험 설정, 성능 지표와 최종 RAW 무결성 결과를 전달하는 모델이다.
public enum PerformanceCategory { Network, Routing, Pvt, System, DataIntegrity }
public enum MetricStatus { Pass, Fail, Measured, NotApplicable }

/// <summary>측정값에 적용할 최소 또는 최대 합격 기준을 나타낸다.</summary>
public sealed record MetricThreshold(bool Enabled, double Value, bool IsMinimum);

public sealed record PerformanceMetric(
    PerformanceCategory Category,
    string Name,
    string Description,
    string Unit,
    double? Value,
    MetricStatus Status,
    MetricThreshold? Threshold = null,
    string? Detail = null);

/// <summary>AFS UDP 종단 간 시험에 사용하는 주소, 포트, 반복 송신과 의도적 드롭 설정이다.</summary>
public sealed record AfsTransportSettings(
    string BroadcastAddress = "255.255.255.255",
    int DataPort = 45821,
    int ResultPort = 45822,
    int RepeatCount = 3,
    int ResultTimeoutSeconds = 30,
    int EndGraceMilliseconds = 1000,
    int ProbeIntervalMilliseconds = 1000,
    double SimulatedDropRatePercent = 0,
    int SimulatedDropSeed = 1);

/// <summary>입력 RAW·almanac 경로와 AFS 규격 고정값 및 판정 기준을 묶는다.</summary>
public sealed record AfsTestSettings(
    string CapturePath,
    string AlmanacPath,
    string ResultRoot,
    int Prn = 8,
    int CustomMessageType = 63,
    IReadOnlyDictionary<string, MetricThreshold>? Thresholds = null);

public sealed record RawIntegrityResult(
    bool Success,
    long SourceLength,
    long ReconstructedLength,
    string SourceSha256,
    string ReconstructedSha256,
    int ExpectedRecords,
    int ReconstructedRecords,
    string Detail);
