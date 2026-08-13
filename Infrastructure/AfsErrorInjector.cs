using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>정상 AFS 프레임을 복사한 뒤 지정된 방식과 seed로 심볼을 반전한다.</summary>
public static class AfsErrorInjector
{
    public const int FrameSymbolCount = 6000;
    public const int FrameByteCount = 750;
    public const int PayloadStartSymbol = 120;
    public const int SyncSymbolCount = 68;

    public static AfsErrorInjectionResult Inject(ReadOnlySpan<byte> encodedFrame, AfsErrorInjectionSettings settings, int trialIndex = 0)
    {
        if (encodedFrame.Length != FrameByteCount) throw new ArgumentException($"AFS frame must be exactly {FrameByteCount} bytes.", nameof(encodedFrame));
        if (settings.ErrorCount < 0) throw new ArgumentOutOfRangeException(nameof(settings), "Error count cannot be negative.");
        var frame = encodedFrame.ToArray();
        if (settings.Mode == AfsErrorInjectionMode.None || settings.ErrorCount == 0) return new(frame, []);
        var indices = settings.Mode switch
        {
            AfsErrorInjectionMode.Random => RandomIndices(settings, trialIndex),
            AfsErrorInjectionMode.Burst => BurstIndices(settings, trialIndex),
            AfsErrorInjectionMode.SyncLoss => SyncIndices(settings, trialIndex),
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Mode, "Unsupported AFS error mode.")
        };
        foreach (var index in indices) frame[index >> 3] ^= (byte)(1 << (7 - (index & 7)));
        return new(frame, indices);
    }

    private static int[] RandomIndices(AfsErrorInjectionSettings settings, int trialIndex)
    {
        var start = settings.IncludeSyncAndSb1 ? 0 : PayloadStartSymbol;
        var available = FrameSymbolCount - start;
        if (settings.ErrorCount > available) throw new ArgumentOutOfRangeException(nameof(settings), $"Random error count cannot exceed {available} symbols.");
        var random = CreateRandom(settings.Seed, trialIndex); var selected = new HashSet<int>();
        while (selected.Count < settings.ErrorCount) selected.Add(random.Next(start, FrameSymbolCount));
        return selected.Order().ToArray();
    }

    private static int[] BurstIndices(AfsErrorInjectionSettings settings, int trialIndex)
    {
        var start = settings.IncludeSyncAndSb1 ? 0 : PayloadStartSymbol;
        var available = FrameSymbolCount - start;
        if (settings.ErrorCount > available) throw new ArgumentOutOfRangeException(nameof(settings), $"Burst error count cannot exceed {available} symbols.");
        var first = CreateRandom(settings.Seed, trialIndex).Next(start, FrameSymbolCount - settings.ErrorCount + 1);
        return Enumerable.Range(first, settings.ErrorCount).ToArray();
    }

    private static int[] SyncIndices(AfsErrorInjectionSettings settings, int trialIndex)
    {
        if (settings.ErrorCount > SyncSymbolCount) throw new ArgumentOutOfRangeException(nameof(settings), $"Sync-loss error count cannot exceed {SyncSymbolCount} symbols.");
        var random = CreateRandom(settings.Seed, trialIndex); var selected = new HashSet<int>();
        while (selected.Count < settings.ErrorCount) selected.Add(random.Next(0, SyncSymbolCount));
        return selected.Order().ToArray();
    }

    private static Random CreateRandom(int seed, int trialIndex) => new(unchecked(seed * 397 ^ trialIndex));
}
