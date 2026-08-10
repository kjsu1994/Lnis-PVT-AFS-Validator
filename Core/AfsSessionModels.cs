namespace LnisAfsValidator.Core;

public sealed record AfsSessionManifest(
    Guid TestId,
    int ProtocolVersion,
    int Prn,
    int CustomMessageType,
    long SourceLength,
    string SourceSha256,
    int RecordCount,
    int FrameCount,
    ushort StartWeek,
    ushort StartIntervalOfWeek,
    byte StartTimeOfInterval,
    long ClockOffsetTicks = 0);

public sealed record AfsSessionResult(
    Guid TestId,
    Verdict Verdict,
    DateTimeOffset CompletedAt,
    RawIntegrityResult Integrity,
    IReadOnlyList<PerformanceMetric> Metrics,
    AfsNetworkCounters Counters,
    string ResultDirectory,
    string? Error = null);

public sealed record AfsSessionProgress(string Stage, double Percent, string Message);
