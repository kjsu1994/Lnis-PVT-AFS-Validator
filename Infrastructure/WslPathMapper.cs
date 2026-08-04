namespace LnisAfsValidator.Infrastructure;
public static class WslPathMapper
{
    public static string ToUnc(string distribution, string linuxPath)
    {
        if (!linuxPath.StartsWith('/') || linuxPath.Split('/').Any(x => x == "..")) throw new ArgumentException("A safe absolute Linux path is required.", nameof(linuxPath));
        return @"\\wsl.localhost\" + distribution + "\\" + linuxPath.TrimStart('/').Replace('/', '\\');
    }
}
