namespace LnisAfsValidator.Core;

// AFS 시험 설정, 성능 지표와 최종 RAW 무결성 결과를 전달하는 모델이다.
/// <summary>시험의 최종 판정 상태다.</summary>
public enum Verdict { Pass, Fail, Inconclusive }
/// <summary>성능 지표가 속하는 측정 영역이다.</summary>
public enum PerformanceCategory { Network, Routing, Pvt, System, DataIntegrity }
/// <summary>개별 성능 지표의 판정 또는 측정 상태다.</summary>
public enum MetricStatus { Pass, Fail, Measured, NotApplicable }

/// <summary>측정값에 적용할 최소 또는 최대 합격 기준을 나타낸다.</summary>
public sealed record MetricThreshold(bool Enabled, double Value, bool IsMinimum);

/// <summary>시험에서 측정한 단일 성능 지표와 선택 판정 기준이다.</summary>
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

/// <summary>송신부가 사용하는 원본 RAW 경로, 논리 AFS 식별자와 판정 기준이다.</summary>
public sealed record AfsSenderSettings(
    string CapturePath,
    string ResultRoot,
    int Prn = 8,
    int CustomMessageType = 63,
    IReadOnlyDictionary<string, MetricThreshold>? Thresholds = null);

/// <summary>수신부가 사용하는 저장 위치와 판정 기준이다. 송신 전용 입력 파일은 포함하지 않는다.</summary>
public sealed record AfsReceiverSettings(
    string ResultRoot,
    int Prn = 8,
    int CustomMessageType = 63,
    IReadOnlyDictionary<string, MetricThreshold>? Thresholds = null);

/// <summary>원본과 재구성 RAW 파일의 길이·레코드 수·SHA-256 비교 결과다.</summary>
public sealed record RawIntegrityResult(
    bool Success,
    long SourceLength,
    long ReconstructedLength,
    string SourceSha256,
    string ReconstructedSha256,
    int ExpectedRecords,
    int ReconstructedRecords,
    string Detail);
