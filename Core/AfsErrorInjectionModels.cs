namespace LnisAfsValidator.Core;

public enum AfsErrorInjectionMode { None, Random, Burst, SyncLoss }

public sealed record AfsErrorInjectionSettings(
    AfsErrorInjectionMode Mode = AfsErrorInjectionMode.None,
    int ErrorCount = 0,
    int Seed = 1,
    bool IncludeSyncAndSb1 = false);

public sealed record AfsErrorInjectionResult(byte[] Frame, IReadOnlyList<int> FlippedSymbolIndices);
