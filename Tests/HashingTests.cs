using System.Text;
using LnisAfsValidator.Infrastructure;
namespace LnisAfsValidator.Tests;
public sealed class HashingTests
{
    [Fact] public void Crc32_UsesIeeeKnownVector() => Assert.Equal(0xCBF43926u, Hashing.Crc32(Encoding.ASCII.GetBytes("123456789")));
    [Fact] public void WslPathMapper_RejectsTraversal() => Assert.Throws<ArgumentException>(() => WslPathMapper.ToUnc("Ubuntu", "/home/../data"));
    [Fact] public void WslPathMapper_MapsAbsolutePath() => Assert.Equal(@"\\wsl.localhost\Ubuntu\home\imt\a.bin", WslPathMapper.ToUnc("Ubuntu", "/home/imt/a.bin"));
}
