namespace LnisAfsValidator.Infrastructure;

public static class AfsPacketDropSimulator
{
    public static bool ShouldDrop(uint frameSequence, int copyIndex, double dropRatePercent, int seed)
    {
        if (dropRatePercent <= 0) return false;
        if (dropRatePercent >= 100) return true;

        // 프레임 번호와 복제본 번호를 고정된 정수 해시로 섞는다.
        // 같은 Seed에서는 실행할 때마다 정확히 같은 UDP 데이터그램이 제거된다.
        var value = unchecked((uint)seed ^ (frameSequence + 1) * 0x9E3779B9u ^ (uint)(copyIndex + 1) * 0x85EBCA6Bu);
        value ^= value >> 16; value *= 0x7FEB352Du;
        value ^= value >> 15; value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value / ((double)uint.MaxValue + 1) * 100.0 < dropRatePercent;
    }
}
