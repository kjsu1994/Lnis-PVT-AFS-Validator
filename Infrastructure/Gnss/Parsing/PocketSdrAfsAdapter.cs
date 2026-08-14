using System.Globalization;
using LnisAfsValidator.Core;
namespace LnisAfsValidator.Infrastructure;
/// <summary>PocketSDR-AFS로 IQ 파일을 처리하고 로그를 수신 증거 모델로 변환한다.</summary>
public sealed class PocketSdrAfsAdapter(ToolConfiguration config, ExternalProcessRunner runner, IReceiverLogParser parser, TimeSpan timeout) : IAfsReceiverAdapter
{
    public async Task<(ReceiverEvidence, ProcessRunResult, string)> ProcessAsync(IqArtifact artifact, TestScenario s, string runDirectory, IProgress<RunProgress>? progress, Action<ProcessLogLine>? log, CancellationToken token)
    {
        progress?.Report(new(RunState.Processing, 80, "Running PocketSDR-AFS"));
        var id = Guid.NewGuid().ToString("N"); var linuxDirectory = config.WslRunRoot.TrimEnd('/') + "/" + id;
        var directory = config.Mode == ExecutionMode.Wsl ? WslPathMapper.ToUnc(config.WslDistribution, linuxDirectory) : runDirectory;
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "received_iq2.bin");
        if (!Path.GetFullPath(artifact.FilePath).Equals(Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase)) await CopyAsync(artifact.FilePath, input, token);
        var rawLog = Path.Combine(directory, "pocket_trk.log");
        var toolInput = config.Mode == ExecutionMode.Wsl ? linuxDirectory + "/received_iq2.bin" : input;
        var toolLog = config.Mode == ExecutionMode.Wsl ? linuxDirectory + "/pocket_trk.log" : rawLog;
        var rate = s.SampleRateMHz.ToString(CultureInfo.InvariantCulture);
        var args = new[] { "-sig", "AFSD", "-prn", s.Prns, "-sig", "AFSP", "-prn", s.Prns, "-fmt", "INT8X2", "-f", rate, "-IQ", "2", "-ti", "0", "-log", toolLog, toolInput };
        var result = await runner.RunAsync(config, args, timeout, log, token);
        if (result.ExitCode != 0 || result.TimedOut || result.Cancelled) { CleanStaged(input, artifact.FilePath); return (EmptyEvidence(), result, rawLog); }
        if (!File.Exists(rawLog)) throw new InvalidDataException("PocketSDR-AFS log was not created.");
        var evidence = parser.Parse(await File.ReadAllLinesAsync(rawLog, token));
        var savedLog = Path.Combine(runDirectory, "pocket_trk.log"); if (!Path.GetFullPath(rawLog).Equals(Path.GetFullPath(savedLog), StringComparison.OrdinalIgnoreCase)) await CopyLogAsync(rawLog, savedLog, token);
        CleanStaged(input, artifact.FilePath);
        progress?.Report(new(RunState.Processing, 92, $"Parsed {evidence.Sb2DecodedPrns.Count} SB2 satellites"));
        return (evidence, result, savedLog);
    }
    private static async Task CopyAsync(string source, string target, CancellationToken token)
    {
        await using var s = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        await using var t = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        await s.CopyToAsync(t, 1024 * 1024, token);
    }
    private static ReceiverEvidence EmptyEvidence() => new([], [], 0, 0, 0, null, null, null, null, []);
    private static async Task CopyLogAsync(string source, string target, CancellationToken token) { await using var s = File.OpenRead(source); await using var t = File.Create(target); await s.CopyToAsync(t, token); }
    private static void CleanStaged(string staged, string original) { if (!Path.GetFullPath(staged).Equals(Path.GetFullPath(original), StringComparison.OrdinalIgnoreCase) && File.Exists(staged)) File.Delete(staged); }
}
