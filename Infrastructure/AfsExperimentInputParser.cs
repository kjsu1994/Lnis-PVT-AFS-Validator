namespace LnisAfsValidator.Infrastructure;

public static class AfsExperimentInputParser
{
    public static IReadOnlyList<int> ParseErrorCounts(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("오류 개수를 입력하세요.");

        // 사용자가 한글 문서에서 값을 붙여 넣어도 되도록 쉼표, 공백, 세미콜론을 모두 구분자로 허용한다.
        var tokens = text.Split([',', '，', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new List<int>();
        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out var value) || value <= 0)
                throw new ArgumentException($"'{token}'은 올바른 오류 개수가 아닙니다. 1 이상의 정수를 입력하세요.");
            if (!values.Contains(value)) values.Add(value);
        }
        if (values.Count == 0) throw new ArgumentException("오류 개수를 하나 이상 입력하세요.");
        values.Sort();
        return values;
    }
}
