namespace LnisAfsValidator.Core;

/// <summary>송신자가 SessionStart payload로 전달하는 원본 파일 및 세션 정보다.</summary>
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
    long ClockOffsetTicks = 0,
    double SimulatedDropRatePercent = 0,
    int SimulatedDropSeed = 1,
    long SimulatedDroppedDatagrams = 0);

/// <summary>AFS 송수신 시험의 판정, 무결성 결과, 성능 지표와 저장 위치를 묶는다.</summary>
public sealed record AfsSessionResult(
    Guid TestId,
    Verdict Verdict,
    DateTimeOffset CompletedAt,
    RawIntegrityResult Integrity,
    IReadOnlyList<PerformanceMetric> Metrics,
    AfsNetworkCounters Counters,
    string ResultDirectory,
    string? Error = null);

/// <summary>송수신 또는 실험 서비스가 UI에 전달하는 단계별 진행률이다.</summary>
public sealed record AfsSessionProgress(string Stage, double Percent, string Message);
