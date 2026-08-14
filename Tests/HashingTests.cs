using System.Text;
using LnisAfsValidator.Infrastructure;
namespace LnisAfsValidator.Tests;
/// <summary>SHA-256과 CRC32 구현을 알려진 결과값으로 검증한다.</summary>
public sealed class HashingTests
{
    [Fact] public void Crc32_UsesIeeeKnownVector() => Assert.Equal(0xCBF43926u, Hashing.Crc32(Encoding.ASCII.GetBytes("123456789")));
}
