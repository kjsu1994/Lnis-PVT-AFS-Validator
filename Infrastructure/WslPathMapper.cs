namespace LnisAfsValidator.Infrastructure;
/// <summary>Windows와 WSL에서 동일한 파일을 가리키도록 경로 형식을 변환한다.</summary>
public static class WslPathMapper
{
    public static string ToUnc(string distribution, string linuxPath)
    {
        if (!linuxPath.StartsWith('/') || linuxPath.Split('/').Any(x => x == "..")) throw new ArgumentException("A safe absolute Linux path is required.", nameof(linuxPath));
        return @"\\wsl.localhost\" + distribution + "\\" + linuxPath.TrimStart('/').Replace('/', '\\');
    }
}
