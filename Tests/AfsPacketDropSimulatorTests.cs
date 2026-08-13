using LnisAfsValidator.Infrastructure;

namespace LnisAfsValidator.Tests;

public sealed class AfsPacketDropSimulatorTests
{
    [Fact]
    public void SameSeedProducesSameDropPattern()
    {
        var first = Enumerable.Range(0, 1000).Select(i => AfsPacketDropSimulator.ShouldDrop((uint)(i / 3), i % 3, 5, 77)).ToArray();
        var second = Enumerable.Range(0, 1000).Select(i => AfsPacketDropSimulator.ShouldDrop((uint)(i / 3), i % 3, 5, 77)).ToArray();
        Assert.Equal(first, second);
        Assert.InRange(first.Count(x => x), 30, 70);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100, true)]
    public void HandlesBoundaryRates(double rate, bool expected)
    {
        Assert.Equal(expected, AfsPacketDropSimulator.ShouldDrop(1, 0, rate, 1));
    }
}
