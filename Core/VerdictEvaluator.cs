namespace LnisAfsValidator.Core;
/// <summary>전송, 외부 프로세스와 수신 증거를 시험 허용오차에 따라 최종 판정한다.</summary>
public sealed class VerdictEvaluator : IVerdictEvaluator
{
    public ValidationResult Evaluate(Guid id, TransferReceipt t, TestScenario s, GeneratorBaseline b, ReceiverEvidence? e, ProcessRunResult? p, bool cancelled = false)
    {
        var c = new List<CheckResult>(); if (cancelled) c.Add(new("Execution", true, Verdict.Inconclusive, "Cancelled")); Add(c, "Transfer integrity", t.Success, t.Reason);
        if (p is null) c.Add(new("Receiver process", true, Verdict.Inconclusive, "No process result")); else Add(c, "Receiver process", p.ExitCode == 0 && !p.TimedOut && !p.Cancelled, p.TimedOut ? "Timed out" : p.Cancelled ? "Cancelled" : $"Exit {p.ExitCode}");
        double? pe = null, te = null;
        if (e is null) c.Add(new("Receiver evidence", true, Verdict.Inconclusive, "No receiver log"));
        else
        {
            Add(c, "Signal acquisition", e.AcquiredPrns.Count >= s.MinimumSatellites, $"{e.AcquiredPrns.Count}/{s.MinimumSatellites}");
            Add(c, "SB2 LDPC/CRC and frame sync", e.Sb2DecodedPrns.Count >= s.MinimumSatellites, $"{e.Sb2DecodedPrns.Count}/{s.MinimumSatellites}");
            Add(c, "PVT", e.Position is not null && e.PositionTime is not null, e.Position is null ? "No $POS" : "$POS found");
            Add(c, "Observed satellites", e.ObservedSatelliteCount >= s.MinimumSatellites, $"{e.ObservedSatelliteCount?.ToString() ?? "unknown"}/{s.MinimumSatellites}");
            if (e.Position is not null) { pe = Distance(b.Position, e.Position); Add(c, "Position error", pe <= s.PositionToleranceMeters, $"{pe:F3} m / {s.PositionToleranceMeters:F3} m"); } else c.Add(new("Position error", true, Verdict.Inconclusive, "Unavailable"));
            if (e.PositionTime is not null && e.ReceiverRelativeTimeSeconds is not null) { te = Math.Abs((e.PositionTime.Value - b.StartTime.AddSeconds(e.ReceiverRelativeTimeSeconds.Value)).TotalSeconds); Add(c, "Time error", te <= s.TimeToleranceSeconds, $"{te:F6} s / {s.TimeToleranceSeconds:F6} s"); } else c.Add(new("Time error", true, Verdict.Inconclusive, "Unavailable"));
        }
        var v = c.Any(x => x.Required && x.Verdict == Verdict.Fail) ? Verdict.Fail : c.Any(x => x.Required && x.Verdict == Verdict.Inconclusive) ? Verdict.Inconclusive : Verdict.Pass;
        return new(id, v, DateTimeOffset.Now, c, e, pe, te);
    }
    private static void Add(List<CheckResult> c, string n, bool ok, string d) => c.Add(new(n, true, ok ? Verdict.Pass : Verdict.Fail, d));
    private static double Distance(LunarPosition a, LunarPosition b)
    {
        static double[] X(LunarPosition p) { const double r0 = 1737400; var a = p.LatitudeDegrees * Math.PI / 180; var o = p.LongitudeDegrees * Math.PI / 180; var r = r0 + p.HeightMeters; return [r * Math.Cos(a) * Math.Cos(o), r * Math.Cos(a) * Math.Sin(o), r * Math.Sin(a)]; }
        var x = X(a); var y = X(b); return Math.Sqrt(x.Zip(y, (m, n) => (m - n) * (m - n)).Sum());
    }
}
