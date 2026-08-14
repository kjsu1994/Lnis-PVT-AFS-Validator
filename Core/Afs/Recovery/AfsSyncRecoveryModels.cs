namespace LnisAfsValidator.Core;

// Test D 동기 손실·재탐색 시험의 설정과 시행별/조건별 결과 모델이다.
public sealed record AfsSyncRecoverySettings(IReadOnlyList<int> SyncErrorCounts, int TrialsPerCondition, int Seed);

/// <summary>한 번의 SP 훼손 시험에서 손상 프레임 거부와 다음 정상 프레임 복구 결과다.</summary>
public sealed record AfsSyncRecoveryTrial(
    int SyncErrorCount,
    int TrialNumber,
    bool DamagedFrameRejected,
    bool NextSyncFound,
    bool NextFrameDecoded,
    int? RecoveryFrameCount,
    double? RecoveryTimeSeconds,
    long? RecoveredBitOffset,
    string FlippedSyncSymbols,
    string? Detail);

/// <summary>동일 SP 오류 심볼 수 조건의 거부·재탐색·복호 복구율 요약이다.</summary>
public sealed record AfsSyncRecoverySummary(
    int SyncErrorCount,
    int Trials,
    double DamagedFrameRejectionRate,
    double SyncRecoveryRate,
    double DecodeRecoveryRate,
    double? AverageRecoverySeconds);

/// <summary>Test D 설정, 조건별 요약, 시행 결과와 저장 위치를 묶는다.</summary>
public sealed record AfsSyncRecoveryResult(
    DateTimeOffset CompletedAt,
    AfsSyncRecoverySettings Settings,
    IReadOnlyList<AfsSyncRecoverySummary> Summaries,
    IReadOnlyList<AfsSyncRecoveryTrial> Trials,
    string ResultDirectory);
