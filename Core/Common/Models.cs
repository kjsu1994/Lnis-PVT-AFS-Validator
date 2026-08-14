using System.Text.Json.Serialization;
namespace LnisAfsValidator.Core;
// 기존 IQ 생성·전송·수신기 검증 경로에서 공유하는 실행 설정과 결과 모델이다.
[JsonConverter(typeof(JsonStringEnumConverter))] public enum RunRole { Sender, Receiver, Local }
[JsonConverter(typeof(JsonStringEnumConverter))] public enum ExecutionMode { Native, Wsl }
[JsonConverter(typeof(JsonStringEnumConverter))] public enum Verdict { Pass, Fail, Inconclusive }
[JsonConverter(typeof(JsonStringEnumConverter))] public enum RunState { Idle, Preparing, Generating, Listening, Connecting, Transferring, Verifying, Processing, Evaluating, Completed, Cancelled, Failed }
public sealed record LunarPosition(double LatitudeDegrees, double LongitudeDegrees, double HeightMeters);
public sealed record ToolConfiguration(ExecutionMode Mode, string ExecutablePath, string WorkingDirectory, string WslDistribution = "Ubuntu", string WslRunRoot = "/home/imt/.local/share/lnis-afs-validator");
public sealed record TestScenario(string Name, LunarPosition ReferencePosition, double PositionToleranceMeters, double TimeToleranceSeconds, int DurationSeconds = 90, double SampleRateMHz = 12.0, string AlmanacPath = "default_almanac.txt", string Prns = "2-8", int MinimumSatellites = 4);
public sealed record ApplicationSettings(RunRole Role, string RemoteAddress, int Port, int ChunkSizeBytes, long MaximumFileBytes, int ConnectTimeoutSeconds, int IdleTimeoutSeconds, int ProcessTimeoutMinutes, string ResultRoot, ToolConfiguration Generator, ToolConfiguration Receiver, TestScenario Scenario);
public sealed record IqFormatDescriptor(string FormatId, double SampleRateMHz, int Channels = 1);
public sealed record IqArtifact(string FilePath, long Length, string Sha256, IqFormatDescriptor Format);
public sealed record GeneratorBaseline(LunarPosition Position, DateTimeOffset StartTime, int AfsWeek, int IntervalOfWeek, int TimeOfInterval);
public sealed record ProcessLogLine(DateTimeOffset Timestamp, bool IsError, string Text);
public sealed record ProcessRunResult(int ExitCode, bool TimedOut, bool Cancelled, IReadOnlyList<ProcessLogLine> Lines);
public sealed record TransferManifest(Guid TestId, string DisplayFileName, long FileLength, string Sha256, int ChunkSize, IqFormatDescriptor Format, TestScenario Scenario, GeneratorBaseline Baseline);
public sealed record TransferReceipt(bool Success, string Reason, string? FilePath, long BytesReceived, int ChunksReceived, string? Sha256);
public sealed record ReceiverEvidence(HashSet<int> AcquiredPrns, HashSet<int> Sb2DecodedPrns, int Sb3DecodedCount, int Sb4DecodedCount, int FrameErrorCount, int? ObservedSatelliteCount, LunarPosition? Position, DateTimeOffset? PositionTime, double? ReceiverRelativeTimeSeconds, List<string> ParserWarnings);
public sealed record CheckResult(string Name, bool Required, Verdict Verdict, string Detail);
public sealed record ValidationResult(Guid TestId, Verdict Verdict, DateTimeOffset CompletedAt, IReadOnlyList<CheckResult> Checks, ReceiverEvidence? Evidence = null, double? PositionErrorMeters = null, double? TimeErrorSeconds = null);
public sealed record RunProgress(RunState State, double Percent, string Message);
public sealed record RunOutcome(string RunDirectory, ValidationResult Result);
