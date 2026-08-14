using System.Diagnostics;
using LnisAfsValidator.Core;
namespace LnisAfsValidator.Infrastructure;
/// <summary>외부 실행 파일 또는 WSL 명령을 실행하고 출력, 종료 상태와 제한시간을 수집한다.</summary>
public sealed class ExternalProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(ToolConfiguration config, IEnumerable<string> arguments, TimeSpan timeout, Action<ProcessLogLine>? sink, CancellationToken token)
    {
        var info = new ProcessStartInfo { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (config.Mode == ExecutionMode.Native) { info.FileName = config.ExecutablePath; info.WorkingDirectory = config.WorkingDirectory; }
        else
        {
            info.FileName = "wsl.exe"; info.WorkingDirectory = Environment.SystemDirectory;
            foreach (var a in new[] { "-d", config.WslDistribution, "--cd", config.WorkingDirectory, "--exec", config.ExecutablePath }) info.ArgumentList.Add(a);
        }
        foreach (var a in arguments) info.ArgumentList.Add(a);
        var lines = new List<ProcessLogLine>(); var gate = new object();
        using var process = new Process { StartInfo = info };
        process.Start();
        async Task ReadAsync(StreamReader reader, bool error)
        {
            while (await reader.ReadLineAsync(token) is { } text) { var line = new ProcessLogLine(DateTimeOffset.Now, error, text); lock (gate) lines.Add(line); sink?.Invoke(line); }
        }
        var output = ReadAsync(process.StandardOutput, false); var errors = ReadAsync(process.StandardError, true);
        using var timeoutCts = new CancellationTokenSource(timeout); using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
        var cancelled = false; var timedOut = false;
        try { await process.WaitForExitAsync(linked.Token); await Task.WhenAll(output, errors); }
        catch (OperationCanceledException) { cancelled = token.IsCancellationRequested; timedOut = !cancelled; if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); }
        return new(process.ExitCode, timedOut, cancelled, lines.ToArray());
    }
}
