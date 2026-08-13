using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;

namespace LnisAfsValidator.Tests;

/// <summary>Random, Burst, SyncLoss 오류 위치와 seed 기반 재현성을 검증한다.</summary>
public sealed class AfsErrorInjectorTests
{
    [Fact]
    public void RandomErrorsAreDeterministicAndDoNotTouchSyncByDefault()
    {
        var settings = new AfsErrorInjectionSettings(AfsErrorInjectionMode.Random, 50, Seed: 1234);
        var first = AfsErrorInjector.Inject(new byte[750], settings, 7);
        var second = AfsErrorInjector.Inject(new byte[750], settings, 7);
        Assert.Equal(first.Frame, second.Frame);
        Assert.Equal(first.FlippedSymbolIndices, second.FlippedSymbolIndices);
        Assert.Equal(50, first.FlippedSymbolIndices.Distinct().Count());
        Assert.All(first.FlippedSymbolIndices, index => Assert.InRange(index, 120, 5999));
    }

    [Fact]
    public void BurstErrorsAreOneContiguousRange()
    {
        var result = AfsErrorInjector.Inject(new byte[750], new(AfsErrorInjectionMode.Burst, 100, Seed: 42), 3);
        Assert.Equal(100, result.FlippedSymbolIndices.Count);
        Assert.Equal(99, result.FlippedSymbolIndices[^1] - result.FlippedSymbolIndices[0]);
    }

    [Fact]
    public void SyncLossOnlyChangesSynchronizationPattern()
    {
        var result = AfsErrorInjector.Inject(new byte[750], new(AfsErrorInjectionMode.SyncLoss, 20, Seed: 9), 1);
        Assert.Equal(20, result.FlippedSymbolIndices.Count);
        Assert.All(result.FlippedSymbolIndices, index => Assert.InRange(index, 0, 67));
    }
}
