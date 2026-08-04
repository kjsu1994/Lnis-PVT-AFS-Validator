namespace LnisAfsValidator.Core;
public interface IIqDataSource { Task<(IqArtifact Artifact, GeneratorBaseline Baseline, ProcessRunResult Process)> AcquireAsync(TestScenario scenario, string runDirectory, IProgress<RunProgress>? progress, Action<ProcessLogLine>? log, CancellationToken token); }
public interface IAfsReceiverAdapter { Task<(ReceiverEvidence Evidence, ProcessRunResult Process, string RawLogPath)> ProcessAsync(IqArtifact artifact, TestScenario scenario, string runDirectory, IProgress<RunProgress>? progress, Action<ProcessLogLine>? log, CancellationToken token); }
public interface IReceiverLogParser { ReceiverEvidence Parse(IEnumerable<string> lines); }
public interface IVerdictEvaluator { ValidationResult Evaluate(Guid id, TransferReceipt transfer, TestScenario scenario, GeneratorBaseline baseline, ReceiverEvidence? evidence, ProcessRunResult? process, bool cancelled = false); }
public interface IArtifactTransport
{
    Task<(TransferReceipt Receipt, ValidationResult? RemoteResult)> SendAsync(string host, int port, TransferManifest manifest, string filePath, TimeSpan connectTimeout, TimeSpan idleTimeout, IProgress<RunProgress>? progress, CancellationToken token);
    Task<ReceivedTransfer> ReceiveAsync(string bindAddress, int port, string directory, long maxBytes, TimeSpan idleTimeout, IProgress<RunProgress>? progress, CancellationToken token);
}
public sealed record ReceivedTransfer(TransferManifest Manifest, TransferReceipt Receipt, Func<ValidationResult, CancellationToken, Task> SendResultAsync, IAsyncDisposable Connection);
public interface IRunStore
{
    string CreateRunDirectory(Guid id);
    Task SaveJsonAsync<T>(string directory, string name, T value, CancellationToken token);
    Task SaveTextAsync(string directory, string name, IEnumerable<ProcessLogLine> lines, CancellationToken token);
    Task ApplyRetentionAsync(Verdict verdict, IEnumerable<string> iqFiles, CancellationToken token);
}
