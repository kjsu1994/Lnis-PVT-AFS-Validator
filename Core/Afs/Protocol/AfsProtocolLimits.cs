namespace LnisAfsValidator.Core;

/// <summary>Core와 UI 검증에서 함께 사용하는 고정 AFS 프레임 한계값이다.</summary>
public static class AfsProtocolLimits
{
    public const int SubframePayloadSymbolCount = 5880;
    public const int SyncPatternSymbolCount = 68;
}
