using LnisAfsValidator.Infrastructure;

namespace LnisAfsValidator.Tests;

/// <summary>오류 개수 입력의 정상 변환, 정렬·중복 처리와 잘못된 값 거부를 검증한다.</summary>
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
