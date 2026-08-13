namespace LnisAfsValidator.Infrastructure;

/// <summary>비트 스트림에서 68심볼 AFS 동기 패턴을 찾고 해당 위치의 6000심볼 프레임을 추출한다.</summary>
public static class AfsFrameSynchronizer
{
    public const int SyncSymbolCount = 68;
    public const int FrameSymbolCount = 6000;
    private static readonly byte[] SyncBytes = [0xCC, 0x63, 0xF7, 0x45, 0x36, 0xF4, 0x9E, 0x04, 0xA0];

    public static IReadOnlyList<long> FindSyncOffsets(ReadOnlySpan<byte> packedStream, long symbolCount)
    {
        if (symbolCount < 0 || symbolCount > packedStream.Length * 8L) throw new ArgumentOutOfRangeException(nameof(symbolCount));
        var offsets = new List<long>();

        // 수신 시작 위치를 모른다고 가정하고 한 심볼씩 이동하면서 68심볼 SP를 찾는다.
        // 따라서 프레임 경계가 바이트 중간에 놓여도 동일하게 재동기할 수 있다.
        for (long offset = 0; offset <= symbolCount - SyncSymbolCount; offset++)
        {
            var matches = true;
            for (var i = 0; i < SyncSymbolCount; i++)
            {
                if (ReadBit(packedStream, offset + i) == ReadBit(SyncBytes, i)) continue;
                matches = false; break;
            }
            if (matches) offsets.Add(offset);
        }
        return offsets;
    }

    public static byte[] ExtractFrame(ReadOnlySpan<byte> packedStream, long symbolOffset)
    {
        if (symbolOffset < 0 || symbolOffset + FrameSymbolCount > packedStream.Length * 8L) throw new ArgumentOutOfRangeException(nameof(symbolOffset));
        var frame = new byte[750];
        for (var i = 0; i < FrameSymbolCount; i++)
            if (ReadBit(packedStream, symbolOffset + i) != 0) frame[i >> 3] |= (byte)(1 << (7 - (i & 7)));
        return frame;
    }

    private static int ReadBit(ReadOnlySpan<byte> source, long index) => (source[(int)(index >> 3)] >> (7 - ((int)index & 7))) & 1;
}
