using LnisAfsValidator.Core;
namespace LnisAfsValidator.Tests;
public sealed class VerdictTests
{
    [Fact] public void CompleteEvidencePasses()
    {
        var baseline = new GeneratorBaseline(new(-89.66, 129.2, 100), new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), 0, 0, 0);
        var scenario = new TestScenario("test", baseline.Position, 1, .01, MinimumSatellites: 4);
        var evidence = new ReceiverEvidence([2,3,4,5], [2,3,4,5], 0, 0, 0, 4, baseline.Position, baseline.StartTime.AddSeconds(12), 12, []);
        var receipt = new TransferReceipt(true, "verified", "x", 10, 1, new string('A', 64)); var process = new ProcessRunResult(0, false, false, []);
        Assert.Equal(Verdict.Pass, new VerdictEvaluator().Evaluate(Guid.NewGuid(), receipt, scenario, baseline, evidence, process).Verdict);
    }
    [Fact] public void MissingPvtFails()
    {
        var b = new GeneratorBaseline(new(0,0,0), DateTimeOffset.UnixEpoch, 0, 0, 0); var s = new TestScenario("t", b.Position, 1, 1, MinimumSatellites: 4);
        var e = new ReceiverEvidence([1,2,3,4], [1,2,3,4], 0, 0, 0, null, null, null, null, []);
        Assert.Equal(Verdict.Fail, new VerdictEvaluator().Evaluate(Guid.NewGuid(), new(true,"ok","x",1,1,new string('A',64)), s, b, e, new(0,false,false,[])).Verdict);
    }
}
