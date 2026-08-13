namespace LnisAfsValidator.Core;

// Test D 동기 손실·재탐색 시험의 설정과 시행별/조건별 결과 모델이다.
public sealed record AfsSyncRecoverySettings(IReadOnlyList<int> SyncErrorCounts, int TrialsPerCondition, int Seed);

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

public sealed record AfsSyncRecoverySummary(
    int SyncErrorCount,
    int Trials,
    double DamagedFrameRejectionRate,
    double SyncRecoveryRate,
    double DecodeRecoveryRate,
    double? AverageRecoverySeconds);

public sealed record AfsSyncRecoveryResult(
    DateTimeOffset CompletedAt,
    AfsSyncRecoverySettings Settings,
    IReadOnlyList<AfsSyncRecoverySummary> Summaries,
    IReadOnlyList<AfsSyncRecoveryTrial> Trials,
    string ResultDirectory);
