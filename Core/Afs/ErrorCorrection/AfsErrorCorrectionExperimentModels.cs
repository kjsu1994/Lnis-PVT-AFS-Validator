namespace LnisAfsValidator.Core;

// Test B/C 오류정정 반복시험의 입력, 개별 시행, 조건별 요약과 전체 결과 모델이다.
public sealed record AfsErrorCorrectionExperimentSettings(
    AfsErrorInjectionMode Mode,
    IReadOnlyList<int> ErrorCounts,
    int TrialsPerCondition,
    int Seed);

/// <summary>Test B/C 한 회차의 블록별 LDPC·CRC 및 원본 복원 결과다.</summary>
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

/// <summary>동일 오류 심볼 수 조건을 반복한 뒤 계산한 블록별 성공률 요약이다.</summary>
public sealed record AfsErrorCorrectionSummary(
    AfsErrorInjectionMode Mode,
    int ErrorCount,
    int Trials,
    double SyncAcceptanceRate,
    double LdpcSuccessRate,
    double CrcSuccessRate,
    double FrameRestoreRate,
    double AverageChangedBits,
    double Sb2LdpcSuccessRate,
    double Sb3LdpcSuccessRate,
    double Sb4LdpcSuccessRate,
    double Sb2CrcSuccessRate,
    double Sb3CrcSuccessRate,
    double Sb4CrcSuccessRate);

/// <summary>Test B/C 설정, 조건별 요약, 모든 시행과 결과 폴더를 묶는다.</summary>
public sealed record AfsErrorCorrectionExperimentResult(
    DateTimeOffset CompletedAt,
    AfsErrorCorrectionExperimentSettings Settings,
    IReadOnlyList<AfsErrorCorrectionSummary> Summaries,
    IReadOnlyList<AfsErrorCorrectionTrialResult> Trials,
    string ResultDirectory);
