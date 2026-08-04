using System.Globalization;
using System.Text.RegularExpressions;
using LnisAfsValidator.Core;
namespace LnisAfsValidator.Infrastructure;
public sealed partial class LansAfsSimSource(ToolConfiguration config, ExternalProcessRunner runner, TimeSpan timeout) : IIqDataSource
{
    public async Task<(IqArtifact, GeneratorBaseline, ProcessRunResult)> AcquireAsync(TestScenario s, string runDirectory, IProgress<RunProgress>? progress, Action<ProcessLogLine>? log, CancellationToken token)
    {
        progress?.Report(new(RunState.Generating, 5, "Generating AFS INT8X2 I/Q"));
        var linuxOutput = config.WslRunRoot.TrimEnd('/') + "/" + Guid.NewGuid().ToString("N") + "/afs_iq2.bin";
        var output = config.Mode == ExecutionMode.Wsl ? WslPathMapper.ToUnc(config.WslDistribution, linuxOutput) : Path.Combine(runDirectory, "afs_iq2.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var target = config.Mode == ExecutionMode.Wsl ? linuxOutput : output;
        var args = new[] { "-t", s.DurationSeconds.ToString(CultureInfo.InvariantCulture), "-s", s.SampleRateMHz.ToString(CultureInfo.InvariantCulture), "-e", s.AlmanacPath, "-b", "2", "-l", FormattableString.Invariant($"{s.ReferencePosition.LatitudeDegrees}:{s.ReferencePosition.LongitudeDegrees}:{s.ReferencePosition.HeightMeters}"), target };
        var result = await runner.RunAsync(config, args, timeout, log, token);
        if (result.ExitCode != 0 || result.TimedOut || result.Cancelled) throw new InvalidOperationException("LANS-AFS-SIM did not complete successfully.");
        if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new InvalidDataException("Generator produced no I/Q file.");
        var baseline = ParseBaseline(result.Lines);
        var hash = await Hashing.Sha256Async(output, token); var info = new FileInfo(output);
        progress?.Report(new(RunState.Generating, 25, $"Generated {info.Length:N0} bytes"));
        return (new(output, info.Length, hash, new("PocketSDR.INT8X2", s.SampleRateMHz)), baseline, result);
    }

    private static GeneratorBaseline ParseBaseline(IEnumerable<ProcessLogLine> lines)
    {
        LunarPosition? pos = null; DateTimeOffset? start = null; var week = 0; var itow = 0; var toi = 0;
        foreach (var line in lines.Select(x => x.Text))
        {
            var m = LlhRegex().Match(line); if (m.Success) pos = new(D(m, 1), D(m, 2), D(m, 3));
            m = StartRegex().Match(line); if (m.Success) start = new DateTimeOffset(I(m, 1), I(m, 2), I(m, 3), I(m, 4), I(m, 5), 0, TimeSpan.Zero).AddSeconds(D(m, 6));
            m = AfsRegex().Match(line); if (m.Success) { week = I(m, 1); itow = I(m, 2); toi = I(m, 3); }
        }
        return new(pos ?? throw new InvalidDataException("Generator llh was not found."), start ?? throw new InvalidDataException("Generator start time was not found."), week, itow, toi);
    }
    private static int I(Match m, int i) => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
    private static double D(Match m, int i) => double.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
    [GeneratedRegex(@"llh\s*=\s*([+-]?\d+(?:\.\d+)?),\s*([+-]?\d+(?:\.\d+)?),\s*([+-]?\d+(?:\.\d+)?)")] private static partial Regex LlhRegex();
    [GeneratedRegex(@"Start time\s*=\s*(\d{4})/(\d{2})/(\d{2}),(\d{2}):(\d{2}):(\d+(?:\.\d+)?)")] private static partial Regex StartRegex();
    [GeneratedRegex(@"AFS time:\s*WN\s*=\s*(\d+),\s*ITOW\s*=\s*(\d+),\s*TOI\s*=\s*(\d+)")] private static partial Regex AfsRegex();
}
