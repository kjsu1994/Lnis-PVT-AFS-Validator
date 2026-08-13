using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;

namespace LnisAfsValidator.Tests;

/// <summary>비트 오프셋이 어긋난 스트림에서도 SP 탐색과 프레임 추출이 가능한지 검증한다.</summary>
public sealed class AfsFrameSynchronizerTests
{
    [Fact]
    public void FindsNextFrameAfterDamagedSynchronizationPattern()
    {
        var sync = new byte[] { 0xCC, 0x63, 0xF7, 0x45, 0x36, 0xF4, 0x9E, 0x04, 0xA0 };
        var first = new byte[750]; var damaged = new byte[750]; var third = new byte[750];
        Array.Copy(sync, first, sync.Length); Array.Copy(sync, damaged, sync.Length); Array.Copy(sync, third, sync.Length);
        damaged = AfsErrorInjector.Inject(damaged, new(AfsErrorInjectionMode.SyncLoss, 20, Seed: 3), 0).Frame;
        var stream = first.Concat(damaged).Concat(third).ToArray();

        var offsets = AfsFrameSynchronizer.FindSyncOffsets(stream, 18000);

        Assert.Contains(0, offsets);
        Assert.DoesNotContain(6000, offsets);
        Assert.Contains(12000, offsets);
        Assert.Equal(third, AfsFrameSynchronizer.ExtractFrame(stream, 12000));
    }
}
