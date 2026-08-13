using LnisAfsValidator.Infrastructure;

namespace LnisAfsValidator.Tests;

public sealed class AfsExperimentInputParserTests
{
    [Fact]
    public void AcceptsCommaSpaceSemicolonAndRemovesDuplicates()
    {
        Assert.Equal([1, 2, 5, 10], AfsExperimentInputParser.ParseErrorCounts("10, 2  5;1，2"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1,abc")]
    [InlineData("0,1")]
    public void RejectsInvalidInput(string text)
    {
        Assert.Throws<ArgumentException>(() => AfsExperimentInputParser.ParseErrorCounts(text));
    }
}
