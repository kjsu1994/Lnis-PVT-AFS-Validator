namespace LnisAfsValidator.Core;

public enum PerformanceCategory { Network, Routing, Pvt, System, DataIntegrity }
public enum MetricStatus { Pass, Fail, Measured, NotApplicable }

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
