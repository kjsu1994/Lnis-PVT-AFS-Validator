using LnisAfsValidator.Core;
namespace LnisAfsValidator.Infrastructure;
public sealed class TestOrchestrator(ApplicationSettings settings)
{
    private readonly IArtifactTransport transport = new TcpArtifactTransport();
    private readonly IVerdictEvaluator evaluator = new VerdictEvaluator();
    private readonly IRunStore store = new RunStore(settings.ResultRoot);
    private readonly ExternalProcessRunner runner = new();

    public async Task<RunOutcome> RunAsync(IProgress<RunProgress>? progress, Action<ProcessLogLine>? log, CancellationToken token)
    {
        Validate();
        if (settings.Role == RunRole.Receiver) return await RunReceiverAsync(progress, log, token);
        if (settings.Role == RunRole.Local)
        {
            var receiver = RunReceiverAsync(progress, log, token); var sender = await RunSenderAsync(progress, log, token); await receiver; return sender;
        }
        return await RunSenderAsync(progress, log, token);
    }

    private async Task<RunOutcome> RunSenderAsync(IProgress<RunProgress>? progress, Action<ProcessLogLine>? log, CancellationToken token)
    {
        var id = Guid.NewGuid(); var directory = store.CreateRunDirectory(id);
        var source = new LansAfsSimSource(settings.Generator, runner, TimeSpan.FromMinutes(settings.ProcessTimeoutMinutes));
        var (artifact, baseline, genProcess) = await source.AcquireAsync(settings.Scenario, directory, progress, log, token);
        await store.SaveTextAsync(directory, "generator.log", genProcess.Lines, token);
        var manifest = new TransferManifest(id, Path.GetFileName(artifact.FilePath), artifact.Length, artifact.Sha256, settings.ChunkSizeBytes, artifact.Format, settings.Scenario, baseline);
        await store.SaveJsonAsync(directory, "transfer-manifest.json", manifest, token);
        var sent = await transport.SendAsync(settings.RemoteAddress, settings.Port, manifest, artifact.FilePath, TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds), TimeSpan.FromSeconds(settings.IdleTimeoutSeconds), progress, token);
        var result = sent.RemoteResult ?? (sent.Receipt.Success
            ? new ValidationResult(id, Verdict.Inconclusive, DateTimeOffset.Now, [new("Remote result", true, Verdict.Inconclusive, "Remote result was not returned")])
            : evaluator.Evaluate(id, sent.Receipt, settings.Scenario, baseline, null, null));
        await store.SaveJsonAsync(directory, "result.json", result, token); await store.ApplyRetentionAsync(result.Verdict, [artifact.FilePath], token);
        progress?.Report(new(RunState.Completed, 100, result.Verdict.ToString())); return new(directory, result);
    }

    private async Task<RunOutcome> RunReceiverAsync(IProgress<RunProgress>? progress, Action<ProcessLogLine>? log, CancellationToken token)
    {
        var directory = store.CreateRunDirectory(Guid.NewGuid());
        var received = await transport.ReceiveAsync(settings.Role == RunRole.Local ? "127.0.0.1" : "0.0.0.0", settings.Port, directory, settings.MaximumFileBytes, TimeSpan.FromSeconds(settings.IdleTimeoutSeconds), progress, token);
        await using var connection = received.Connection;
        await store.SaveJsonAsync(directory, "transfer-manifest.json", received.Manifest, token);
        ReceiverEvidence? evidence = null; ProcessRunResult? process = null;
        if (received.Receipt.Success && received.Receipt.FilePath is not null)
        {
            var artifact = new IqArtifact(received.Receipt.FilePath, received.Receipt.BytesReceived, received.Receipt.Sha256!, received.Manifest.Format);
            var adapter = new PocketSdrAfsAdapter(settings.Receiver, runner, new PocketSdrLogParser(), TimeSpan.FromMinutes(settings.ProcessTimeoutMinutes));
            var (parsed, receiverProcess, _) = await adapter.ProcessAsync(artifact, received.Manifest.Scenario, directory, progress, log, token);
            evidence = parsed; process = receiverProcess; await store.SaveTextAsync(directory, "receiver-process.log", process.Lines, token);
        }
        progress?.Report(new(RunState.Evaluating, 96, "Evaluating evidence"));
        var result = evaluator.Evaluate(received.Manifest.TestId, received.Receipt, received.Manifest.Scenario, received.Manifest.Baseline, evidence, process);
        await store.SaveJsonAsync(directory, "result.json", result, token);
        if (received.Receipt.Success) await received.SendResultAsync(result, token);
        await store.ApplyRetentionAsync(result.Verdict, received.Receipt.FilePath is null ? [] : [received.Receipt.FilePath], token);
        progress?.Report(new(RunState.Completed, 100, result.Verdict.ToString())); return new(directory, result);
    }

    private void Validate()
    {
        var s = settings.Scenario;
        if (s.PositionToleranceMeters <= 0 || s.TimeToleranceSeconds <= 0) throw new ArgumentException("Position and time tolerances are required.");
        if (s.DurationSeconds <= 0 || s.SampleRateMHz <= 0) throw new ArgumentException("Duration and sample rate must be positive.");
        if (s.MinimumSatellites < 4) throw new ArgumentException("At least four satellites are required for PVT.");
        if (settings.Port is < 1 or > 65535) throw new ArgumentException("Port is outside the valid range.");
        if (settings.Role != RunRole.Receiver && string.IsNullOrWhiteSpace(settings.Generator.ExecutablePath)) throw new ArgumentException("Generator path is required.");
        if (settings.Role != RunRole.Sender && string.IsNullOrWhiteSpace(settings.Receiver.ExecutablePath)) throw new ArgumentException("Receiver path is required.");
    }
}
