namespace LnisAfsValidator.Core;

/// <summary>AFS 프레임에 적용할 오류 주입 방식이다.</summary>
public enum AfsErrorInjectionMode { None, Random, Burst, SyncLoss }

/// <summary>오류 유형, 반전할 심볼 수, 재현용 seed를 지정한다.</summary>
public sealed record AfsErrorInjectionSettings(
    AfsErrorInjectionMode Mode = AfsErrorInjectionMode.None,
    int ErrorCount = 0,
    int Seed = 1,
    bool IncludeSyncAndSb1 = false);

/// <summary>반전 적용된 750바이트 프레임과 실제 심볼 인덱스 목록이다.</summary>
public sealed record AfsErrorInjectionResult(byte[] Frame, IReadOnlyList<int> FlippedSymbolIndices);
