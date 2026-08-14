namespace LnisAfsValidator.Infrastructure;

/// <summary>외부 궤도정보 없이 CRC·LDPC 검증에 사용할 재현 가능한 SB2 1176비트 입력을 만든다.</summary>
public static class AfsSb2Builder
{
    /// <summary>주차와 20분 단위 ITOW를 포함하는 재현 가능한 1176비트 SB2 입력을 생성한다.</summary>
    public static byte[] BuildValidationPattern(ushort week, ushort intervalOfWeek)
    {
        if (intervalOfWeek >= 504) throw new ArgumentOutOfRangeException(nameof(intervalOfWeek));
        var bits = new byte[1176];
        var state = unchecked((uint)(0x6D2B79F5u ^ ((uint)week << 9) ^ intervalOfWeek));
        for (var i = 0; i < bits.Length; i++)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            bits[i] = (byte)(state & 1);
        }
        Write(bits, 0, 13, week);
        Write(bits, 13, 9, intervalOfWeek);
        return bits;
    }

    private static void Write(Span<byte> bits, int offset, int length, ulong value)
    {
        for (var i = 0; i < length; i++) bits[offset + i] = (byte)((value >> (length - i - 1)) & 1);
    }
}
