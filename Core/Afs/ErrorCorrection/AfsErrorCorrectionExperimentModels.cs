namespace LnisAfsValidator.Core;

// Test B/C 오류정정 반복시험의 입력, 개별 시행, 조건별 요약과 전체 결과 모델이다.
public sealed record AfsErrorCorrectionExperimentSettings(
    AfsErrorInjectionMode Mode,
    IReadOnlyList<int> ErrorCounts,
    int TrialsPerCondition,
    int Seed);

public sealed record AfsErrorCorrectionTrialResult(
    AfsErrorInjectionMode Mode,
    int ErrorCount,
    int TrialNumber,
    int Seed,
    bool SyncAccepted,
    bool Sb2LdpcSuccess,
    bool Sb3LdpcSuccess,
    bool Sb4LdpcSuccess,
    bool Sb2CrcSuccess,
    bool Sb3CrcSuccess,
    bool Sb4CrcSuccess,
    int Sb2ChangedBits,
    int Sb3ChangedBits,
    int Sb4ChangedBits,
    bool DataRestored,
    string FlippedSymbols,
    string? Detail);

public sealed record AfsErrorCorrectionSummary(
    AfsErrorInjectionMode Mode,
    int ErrorCount,
    int Trials,
    double SyncAcceptanceRate,
    double LdpcSuccessRate,
    double CrcSuccessRate,
    double FrameRestoreRate,
    double AverageChangedBits);

public sealed record AfsErrorCorrectionExperimentResult(
    DateTimeOffset CompletedAt,
    AfsErrorCorrectionExperimentSettings Settings,
    IReadOnlyList<AfsErrorCorrectionSummary> Summaries,
    IReadOnlyList<AfsErrorCorrectionTrialResult> Trials,
    string ResultDirectory);
