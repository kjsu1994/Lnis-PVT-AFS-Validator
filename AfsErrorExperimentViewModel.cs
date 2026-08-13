using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;
using Microsoft.Win32;

namespace LnisAfsValidator.App;

public sealed class AfsErrorExperimentViewModel : INotifyPropertyChanged
{
    private CancellationTokenSource? cancellation;
    private string capturePath = "", almanacPath = "", broadcastAddress = "127.0.0.1";
    private string state = "Idle", verdict = "-", error = "", resultDirectory = "";
    private double progress;

    public IReadOnlyList<AfsErrorInjectionMode> ErrorModes { get; } = [AfsErrorInjectionMode.Random, AfsErrorInjectionMode.Burst, AfsErrorInjectionMode.SyncLoss];
    public Array Roles => Enum.GetValues<RunRole>();
    public AfsErrorInjectionMode ErrorMode { get; set; } = AfsErrorInjectionMode.Random;
    public string ErrorCountsText { get; set; } = "1, 2, 5, 10, 20, 50";
    public int TrialsPerCondition { get; set; } = 100;
    public int ExperimentSeed { get; set; } = 1;
    public RunRole UdpRole { get; set; } = RunRole.Local;
    public string CapturePath { get => capturePath; set => Set(ref capturePath, value); }
    public string AlmanacPath { get => almanacPath; set => Set(ref almanacPath, value); }
    public string BroadcastAddress { get => broadcastAddress; set => Set(ref broadcastAddress, value); }
    public int DataPort { get; set; } = 45821;
    public int ResultPort { get; set; } = 45822;
    public int RepeatCount { get; set; } = 3;
    public double UdpDropRatePercent { get; set; } = 1;
    public int UdpDropSeed { get; set; } = 1;
    public string State { get => state; private set => Set(ref state, value); }
    public string Verdict { get => verdict; private set => Set(ref verdict, value); }
    public string Error { get => error; private set => Set(ref error, value); }
    public string ResultDirectory { get => resultDirectory; private set => Set(ref resultDirectory, value); }
    public double Progress { get => progress; private set => Set(ref progress, value); }
    public ObservableCollection<PerformanceMetric> Metrics { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public ICommand RunFecCommand { get; }
    public ICommand RunUdpDropCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenResultsCommand { get; }
    public ICommand BrowseCaptureCommand { get; }
    public ICommand BrowseAlmanacCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public AfsErrorExperimentViewModel(string? initialCapturePath = null, string? initialAlmanacPath = null)
    {
        CapturePath = initialCapturePath ?? ""; AlmanacPath = initialAlmanacPath ?? "";
        RunFecCommand = new AsyncCommand(RunFecAsync, () => cancellation is null);
        RunUdpDropCommand = new AsyncCommand(RunUdpDropAsync, () => cancellation is null);
        CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => cancellation is not null);
        OpenResultsCommand = new RelayCommand(OpenResults, () => Directory.Exists(ResultDirectory));
        BrowseCaptureCommand = new RelayCommand(BrowseCapture, () => cancellation is null);
        BrowseAlmanacCommand = new RelayCommand(BrowseAlmanac, () => cancellation is null);
    }

    private async Task RunFecAsync()
    {
        BeginRun();
        try
        {
            var counts = AfsExperimentInputParser.ParseErrorCounts(ErrorCountsText);
            var root = ResultRoot(); var reporter = Reporter();
            if (ErrorMode == AfsErrorInjectionMode.SyncLoss)
            {
                // Test D는 독립 프레임 복호가 아니라 3프레임 연속 스트림에서 다음 SP를 다시 찾는다.
                var result = await new AfsSyncRecoveryExperimentService().RunAsync(new(counts, TrialsPerCondition, ExperimentSeed), root, reporter, cancellation!.Token);
                foreach (var row in result.Summaries)
                {
                    var name = $"SP {row.SyncErrorCount}심볼";
                    AddMetric($"{name} 손상 프레임 거부율", "SP 훼손 프레임이 동기 검사에서 거부된 비율", row.DamagedFrameRejectionRate, "%");
                    AddMetric($"{name} Sync 복구율", "다음 정상 프레임의 SP를 다시 찾은 비율", row.SyncRecoveryRate, "%");
                    AddMetric($"{name} Decode 복구율", "재탐색한 다음 프레임이 CRC까지 정상 복호된 비율", row.DecodeRecoveryRate, "%");
                    AddMetric($"{name} 평균 복구시간", "손상 프레임 시작부터 다음 정상 SP까지의 AFS 논리시간", row.AverageRecoverySeconds, "s");
                }
                ResultDirectory = result.ResultDirectory;
            }
            else
            {
                var result = await new AfsErrorCorrectionExperimentService().RunAsync(new(ErrorMode, counts, TrialsPerCondition, ExperimentSeed), root, reporter, cancellation!.Token);
                foreach (var row in result.Summaries)
                {
                    var name = $"{row.Mode} {row.ErrorCount}심볼";
                    AddMetric($"{name} LDPC 성공률", "SB2/SB3/SB4 LDPC parity 검증 성공 비율", row.LdpcSuccessRate, "%");
                    AddMetric($"{name} CRC 성공률", "LDPC 복호 후 CRC-24Q 통과 비율", row.CrcSuccessRate, "%");
                    AddMetric($"{name} 프레임 복원률", "세 subframe이 원본과 모두 일치한 프레임 비율", row.FrameRestoreRate, "%");
                    AddMetric($"{name} 평균 변경 비트", "LDPC가 수신 hard decision에서 변경한 평균 비트 수", row.AverageChangedBits, "bit");
                }
                ResultDirectory = result.ResultDirectory;
            }
            Complete("Measured");
        }
        catch (OperationCanceledException) { Cancelled(); }
        catch (Exception ex) { Failed(ex); }
        finally { EndRun(); }
    }

    private async Task RunUdpDropAsync()
    {
        BeginRun();
        try
        {
            if (RepeatCount is < 1 or > 20) throw new ArgumentException("UDP 중복 송신 횟수는 1~20 범위여야 합니다.");
            var test = new AfsTestSettings(CapturePath, AlmanacPath, ResultRoot());
            var network = new AfsTransportSettings(BroadcastAddress, DataPort, ResultPort, RepeatCount,
                SimulatedDropRatePercent: UdpDropRatePercent, SimulatedDropSeed: UdpDropSeed);
            var reporter = Reporter(); AfsSessionResult result;
            if (UdpRole == RunRole.Sender) result = await new AfsUdpSessionService().SendAsync(test, network, reporter, cancellation!.Token);
            else if (UdpRole == RunRole.Receiver) result = await new AfsUdpSessionService().ReceiveAsync(test, network, reporter, cancellation!.Token);
            else
            {
                // 로컬 시험에서도 실제 UDP 소켓과 패킷 직렬화를 그대로 통과한다.
                var local = network with { BroadcastAddress = "127.0.0.1" };
                var receiver = new AfsUdpSessionService().ReceiveAsync(test, local, reporter, cancellation!.Token);
                await Task.Delay(200, cancellation.Token);
                var sender = await new AfsUdpSessionService().SendAsync(test, local, reporter, cancellation.Token);
                await receiver; result = sender;
            }
            foreach (var metric in result.Metrics) Metrics.Add(metric);
            ResultDirectory = result.ResultDirectory; Complete(result.Verdict.ToString());
        }
        catch (OperationCanceledException) { Cancelled(); }
        catch (Exception ex) { Failed(ex); }
        finally { EndRun(); }
    }

    private void AddMetric(string name, string description, double? value, string unit) => Metrics.Add(new(PerformanceCategory.DataIntegrity, name, description, unit, value, value is null ? MetricStatus.NotApplicable : MetricStatus.Measured));
    private IProgress<AfsSessionProgress> Reporter() => new Progress<AfsSessionProgress>(p => { State = $"{p.Stage}: {p.Message}"; Progress = p.Percent; Logs.Add($"{DateTime.Now:HH:mm:ss} {p.Message}"); });
    private static string ResultRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "Runs");
    private void BeginRun() { cancellation = new(); Error = ""; Verdict = "-"; State = "Starting"; Progress = 0; Metrics.Clear(); Logs.Clear(); RefreshCommands(); }
    private void Complete(string finalVerdict) { Verdict = finalVerdict; State = "Completed"; Progress = 100; Logs.Add("실제 시험데이터와 결과 파일 저장 완료"); }
    private void Cancelled() { State = "Cancelled"; Verdict = Core.Verdict.Inconclusive.ToString(); }
    private void Failed(Exception ex) { State = "Failed"; Verdict = Core.Verdict.Inconclusive.ToString(); Error = ex.Message; Logs.Add(ex.ToString()); }
    private void EndRun() { cancellation?.Dispose(); cancellation = null; RefreshCommands(); }
    private void BrowseCapture() { var dialog = new OpenFileDialog { Filter = "GNSS RAW (*.graw)|*.graw|All files (*.*)|*.*" }; if (dialog.ShowDialog() == true) CapturePath = dialog.FileName; }
    private void BrowseAlmanac() { var dialog = new OpenFileDialog { Filter = "Almanac (*.txt)|*.txt|All files (*.*)|*.*" }; if (dialog.ShowDialog() == true) AlmanacPath = dialog.FileName; }
    private void OpenResults() { if (Directory.Exists(ResultDirectory)) Process.Start(new ProcessStartInfo(ResultDirectory) { UseShellExecute = true }); }
    private void RefreshCommands() { (RunFecCommand as AsyncCommand)?.Raise(); (RunUdpDropCommand as AsyncCommand)?.Raise(); (CancelCommand as RelayCommand)?.Raise(); (OpenResultsCommand as RelayCommand)?.Raise(); (BrowseCaptureCommand as RelayCommand)?.Raise(); (BrowseAlmanacCommand as RelayCommand)?.Raise(); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
}
