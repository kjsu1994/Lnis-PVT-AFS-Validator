namespace LnisAfsValidator.Core;

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
