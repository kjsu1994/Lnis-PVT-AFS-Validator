using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;
using Microsoft.Win32;

namespace LnisAfsValidator.App;

/// <summary>
/// AFS 검증 메인 화면의 입력값, 실행 명령, 진행 상태와 결과 표시를 관리한다.
/// 실제 AFS 송수신과 오류정정 시험은 Infrastructure 계층의 서비스에 위임한다.
/// </summary>
public sealed class AfsMainViewModel : INotifyPropertyChanged
{
    private CancellationTokenSource? cancellation;
    private string state = "Idle", verdict = "-", error = "", runDirectory = "", capturePath = "", almanacPath = "", broadcastAddress = "255.255.255.255";
    private double progress;
    public Array Roles => Enum.GetValues<RunRole>();
    public IReadOnlyList<AfsErrorInjectionMode> ErrorModes { get; } =
        [AfsErrorInjectionMode.Random, AfsErrorInjectionMode.Burst, AfsErrorInjectionMode.SyncLoss];
    public RunRole Role { get; set; } = RunRole.Local;
    public AfsErrorInjectionMode ErrorMode { get; set; } = AfsErrorInjectionMode.Random;
    public string ErrorCountsText { get; set; } = "1, 2, 5, 10, 20, 50";
    public int TrialsPerCondition { get; set; } = 100;
    public int ExperimentSeed { get; set; } = 1;
    public string CapturePath { get => capturePath; set => Set(ref capturePath, value); }
    public string AlmanacPath { get => almanacPath; set => Set(ref almanacPath, value); }
    public string BroadcastAddress { get => broadcastAddress; set => Set(ref broadcastAddress, value); }
    public int DataPort { get; set; } = 45821; public int ResultPort { get; set; } = 45822; public int ResultTimeoutSeconds { get; set; } = 30;
    public bool ApplyAvailability { get; set; } public double MinimumAvailability { get; set; } = 99;
    public bool ApplyAverageLatency { get; set; } public double MaximumAverageLatency { get; set; } = 1000;
    public bool ApplyMaximumLatency { get; set; } public double MaximumLatency { get; set; } = 2000;
    public bool ApplyThroughput { get; set; } public double MinimumThroughput { get; set; }
    public bool ApplyLoss { get; set; } public double MaximumLoss { get; set; } = 1;
    public bool ApplyDelivery { get; set; } public double MinimumDelivery { get; set; } = 99;
    public string State { get => state; private set => Set(ref state, value); } public string Verdict { get => verdict; private set => Set(ref verdict, value); }
    public string Error { get => error; private set => Set(ref error, value); } public string RunDirectory { get => runDirectory; private set => Set(ref runDirectory, value); }
    public double Progress { get => progress; private set => Set(ref progress, value); }
    public ObservableCollection<PerformanceMetric> Metrics { get; } = []; public ObservableCollection<string> Logs { get; } = [];
    public ICommand StartCommand { get; } public ICommand StartFecCommand { get; } public ICommand CancelCommand { get; } public ICommand OpenResultsCommand { get; }
    public ICommand BrowseCaptureCommand { get; } public ICommand BrowseAlmanacCommand { get; } public ICommand OpenErrorExperimentCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public AfsMainViewModel()
    {
        StartCommand = new AsyncCommand(StartAsync, () => cancellation is null); CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => cancellation is not null);
        StartFecCommand = new AsyncCommand(StartFecAsync, () => cancellation is null);
        OpenErrorExperimentCommand = new RelayCommand(OpenErrorExperiment, () => cancellation is null);
        OpenResultsCommand = new RelayCommand(OpenResults, () => Directory.Exists(RunDirectory)); BrowseCaptureCommand = new RelayCommand(BrowseCapture, () => cancellation is null); BrowseAlmanacCommand = new RelayCommand(BrowseAlmanac, () => cancellation is null); Load();
    }

    private async Task StartAsync()
    {
        // 한 번의 실행 동안 시작 명령을 비활성화하고 이전 실행의 화면 상태를 초기화한다.
        cancellation = new(); Refresh(); Error = ""; Verdict = "-"; Metrics.Clear(); Logs.Clear();
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "Runs");
            var thresholds = new Dictionary<string, MetricThreshold> { ["LinkAvailability"] = new(ApplyAvailability, MinimumAvailability, true), ["AverageLatency"] = new(ApplyAverageLatency, MaximumAverageLatency, false), ["MaximumLatency"] = new(ApplyMaximumLatency, MaximumLatency, false), ["Throughput"] = new(ApplyThroughput, MinimumThroughput, true), ["PacketLossRate"] = new(ApplyLoss, MaximumLoss, false), ["PacketDeliveryRate"] = new(ApplyDelivery, MinimumDelivery, true) };
            var test = new AfsTestSettings(CapturePath, AlmanacPath, root, Thresholds: thresholds); var network = new AfsTransportSettings(BroadcastAddress, DataPort, ResultPort, ResultTimeoutSeconds: ResultTimeoutSeconds);
            await SaveAsync(); var reporter = new Progress<AfsSessionProgress>(p => { State = $"{p.Stage}: {p.Message}"; Progress = p.Percent; Logs.Add($"{DateTime.Now:HH:mm:ss} {p.Message}"); });
            AfsSessionResult result;
            // Sender와 Receiver는 서로 다른 PC에서 독립 실행할 수 있고,
            // Local은 동일한 UDP 처리 경로를 루프백 주소로 한 프로세스 안에서 검증한다.
            if (Role == RunRole.Sender) result = await new AfsUdpSessionService().SendAsync(test, network, reporter, cancellation.Token);
            else if (Role == RunRole.Receiver) result = await new AfsUdpSessionService().ReceiveAsync(test, network, reporter, cancellation.Token);
            else
            {
                var localNetwork = network with { BroadcastAddress = "127.0.0.1" }; var receiver = new AfsUdpSessionService().ReceiveAsync(test, localNetwork, reporter, cancellation.Token);
                await Task.Delay(200, cancellation.Token); var sent = await new AfsUdpSessionService().SendAsync(test, localNetwork, reporter, cancellation.Token); await receiver; result = sent;
            }
            RunDirectory = result.ResultDirectory; Verdict = result.Verdict.ToString(); State = "Completed"; Progress = 100; foreach (var metric in result.Metrics) Metrics.Add(metric);
        }
        catch (OperationCanceledException) { State = "Cancelled"; Verdict = Core.Verdict.Inconclusive.ToString(); }
        catch (Exception ex) { State = "Failed"; Verdict = Core.Verdict.Inconclusive.ToString(); Error = ex.Message; Logs.Add(ex.ToString()); }
        finally { cancellation.Dispose(); cancellation = null; Refresh(); }
    }

    private void BrowseCapture() { var d = new OpenFileDialog { Filter = "GNSS RAW (*.graw)|*.graw|All files (*.*)|*.*" }; if (d.ShowDialog() == true) CapturePath = d.FileName; }
    private async Task StartFecAsync()
    {
        // 오류정정 시험은 입력한 오류 개수별로 동일 조건을 반복하여 성공률을 집계한다.
        cancellation = new(); Refresh(); Error = ""; Verdict = "-"; Metrics.Clear(); Logs.Clear();
        try
        {
            var counts = AfsExperimentInputParser.ParseErrorCounts(ErrorCountsText);
            var settings = new AfsErrorCorrectionExperimentSettings(ErrorMode, counts, TrialsPerCondition, ExperimentSeed);
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "Runs");
            await SaveAsync();
            var reporter = new Progress<AfsSessionProgress>(p =>
            {
                State = $"{p.Stage}: {p.Message}"; Progress = p.Percent;
                Logs.Add($"{DateTime.Now:HH:mm:ss} {p.Message}");
            });

            // 오류정정 시험은 네트워크 품질과 섞이지 않도록 로컬 코덱 경로에서 반복한다.
            // 각 반복의 실제 정상/오류 AFS 프레임은 결과 폴더 frames 아래에 함께 저장된다.
            var result = await new AfsErrorCorrectionExperimentService().RunAsync(settings, root, reporter, cancellation.Token);
            foreach (var row in result.Summaries)
            {
                var condition = $"{row.Mode} {row.ErrorCount}심볼";
                Metrics.Add(new(PerformanceCategory.DataIntegrity, $"{condition} LDPC 성공률", "SB2/SB3/SB4 블록의 LDPC parity 검증 성공 비율", "%", row.LdpcSuccessRate, MetricStatus.Measured));
                Metrics.Add(new(PerformanceCategory.DataIntegrity, $"{condition} CRC 성공률", "LDPC 복호 후 CRC-24Q까지 통과한 블록 비율", "%", row.CrcSuccessRate, MetricStatus.Measured));
                Metrics.Add(new(PerformanceCategory.DataIntegrity, $"{condition} 프레임 복원률", "세 subframe 데이터가 원본과 모두 일치한 프레임 비율", "%", row.FrameRestoreRate, MetricStatus.Measured));
                if (row.Mode == AfsErrorInjectionMode.SyncLoss)
                    Metrics.Add(new(PerformanceCategory.DataIntegrity, $"{condition} SP 수용률", "훼손된 동기 패턴을 현재 프레임 디코더가 수용한 비율", "%", row.SyncAcceptanceRate, MetricStatus.Measured));
            }
            RunDirectory = result.ResultDirectory; Verdict = "Measured"; State = "Completed"; Progress = 100;
            Logs.Add("정상·오류 AFS 바이너리와 CSV/JSON 결과 저장 완료");
        }
        catch (OperationCanceledException) { State = "Cancelled"; Verdict = Core.Verdict.Inconclusive.ToString(); }
        catch (Exception ex) { State = "Failed"; Verdict = Core.Verdict.Inconclusive.ToString(); Error = ex.Message; Logs.Add(ex.ToString()); }
        finally { cancellation.Dispose(); cancellation = null; Refresh(); }
    }

    private void BrowseAlmanac() { var d = new OpenFileDialog { Filter = "Almanac (*.txt)|*.txt|All files (*.*)|*.*" }; if (d.ShowDialog() == true) AlmanacPath = d.FileName; }
    private void OpenErrorExperiment() { var window = new AfsErrorExperimentWindow(CapturePath, AlmanacPath); window.Show(); }
    private void OpenResults() { if (Directory.Exists(RunDirectory)) Process.Start(new ProcessStartInfo(RunDirectory) { UseShellExecute = true }); }
    private string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "afs-settings.json");
    private async Task SaveAsync() { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(new Saved(Role, CapturePath, AlmanacPath, BroadcastAddress, DataPort, ResultPort, ResultTimeoutSeconds, ErrorMode, ErrorCountsText, TrialsPerCondition, ExperimentSeed), new JsonSerializerOptions { WriteIndented = true })); }
    private void Load() { try { if (!File.Exists(SettingsPath)) return; var x = JsonSerializer.Deserialize<Saved>(File.ReadAllText(SettingsPath)); if (x is null) return; Role = x.Role; CapturePath = x.CapturePath; AlmanacPath = x.AlmanacPath; BroadcastAddress = x.BroadcastAddress; DataPort = x.DataPort; ResultPort = x.ResultPort; ResultTimeoutSeconds = x.ResultTimeoutSeconds; ErrorMode = x.ErrorMode; ErrorCountsText = x.ErrorCountsText ?? ErrorCountsText; TrialsPerCondition = x.TrialsPerCondition <= 0 ? TrialsPerCondition : x.TrialsPerCondition; ExperimentSeed = x.ExperimentSeed; } catch (JsonException) { } }
    private void Refresh() { (StartCommand as AsyncCommand)?.Raise(); (StartFecCommand as AsyncCommand)?.Raise(); (CancelCommand as RelayCommand)?.Raise(); (OpenResultsCommand as RelayCommand)?.Raise(); (BrowseCaptureCommand as RelayCommand)?.Raise(); (BrowseAlmanacCommand as RelayCommand)?.Raise(); (OpenErrorExperimentCommand as RelayCommand)?.Raise(); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
    private sealed record Saved(RunRole Role, string CapturePath, string AlmanacPath, string BroadcastAddress, int DataPort, int ResultPort, int ResultTimeoutSeconds,
        AfsErrorInjectionMode ErrorMode = AfsErrorInjectionMode.Random, string? ErrorCountsText = "1, 2, 5, 10, 20, 50", int TrialsPerCondition = 100, int ExperimentSeed = 1);
}
