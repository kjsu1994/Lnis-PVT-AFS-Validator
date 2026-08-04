using System.Globalization;
using LnisAfsValidator.Core;
namespace LnisAfsValidator.Infrastructure;
public sealed class PocketSdrLogParser : IReceiverLogParser
{
    public ReceiverEvidence Parse(IEnumerable<string> lines)
    {
        var acq = new HashSet<int>(); var sb2 = new HashSet<int>(); var warnings = new List<string>();
        var sb3 = 0; var sb4 = 0; var errors = 0; int? nsat = null; LunarPosition? pos = null; DateTimeOffset? time = null; double? relative = null;
        foreach (var raw in lines)
        {
            var p = raw.Trim().Split(',');
            try
            {
                if (p.Length >= 5 && p[0] == "$LOG" && p[4].StartsWith("SIGNAL FOUND", StringComparison.Ordinal)) acq.Add(I(p[3]));
                else if (p.Length >= 4 && p[0] == "$SB2") sb2.Add(I(p[3]));
                else if (p.Length >= 4 && p[0] == "$SB3") sb3++;
                else if (p.Length >= 4 && p[0] == "$SB4") sb4++;
                else if (p.Length >= 5 && p[0] == "$LOG" && p[4].Contains("FRAME ERROR", StringComparison.Ordinal)) errors++;
                else if (p.Length >= 13 && p[0] == "$POS")
                {
                    relative = D(p[1]); time = new DateTimeOffset(I(p[2]), I(p[3]), I(p[4]), I(p[5]), I(p[6]), 0, TimeSpan.Zero).AddSeconds(D(p[7]));
                    pos = new(D(p[8]), D(p[9]), D(p[10])); nsat = I(p[12]);
                }
            }
            catch (Exception ex) { warnings.Add($"Malformed recognized line: {ex.Message}: {raw}"); }
        }
        return new(acq, sb2, sb3, sb4, errors, nsat, pos, time, relative, warnings);
    }
    private static int I(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static double D(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}
