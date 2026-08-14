using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;
using Microsoft.Win32;

namespace LnisAfsValidator.App;

/// <summary>Test A 정상 송신과 Test E UDP Frame Drop 송신만 담당한다.</summary>
public sealed class AfsSenderViewModel : ObservableViewModel
{
    private readonly IAfsSessionService sessionService;
    private CancellationTokenSource? cancellation;
    private string capturePath = "", broadcastAddress = "255.255.255.255";
    private string state = "Idle", verdict = "-", error = "", resultDirectory = "";
    private double progress, dropRate;

    public string CapturePath { get => capturePath; set => Set(ref capturePath, value); }
    public string BroadcastAddress { get => broadcastAddress; set => Set(ref broadcastAddress, value); }
    public int DataPort { get; set; } = 45821;
    public int ResultPort { get; set; } = 45822;
    public int RepeatCount { get; set; } = 3;
    public int ResultTimeoutSeconds { get; set; } = 30;
    public double DropRatePercent { get => dropRate; set => Set(ref dropRate, value); }
    public int DropSeed { get; set; } = 1;
    public string State { get => state; private set => Set(ref state, value); }
    public string Verdict { get => verdict; private set => Set(ref verdict, value); }
    public string Error { get => error; private set => Set(ref error, value); }
    public string ResultDirectory { get => resultDirectory; private set => Set(ref resultDirectory, value); }
    public double Progress { get => progress; private set => Set(ref progress, value); }
    public ObservableCollection<PerformanceMetric> Metrics { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public ICommand StartCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BrowseCaptureCommand { get; }
    public ICommand OpenResultsCommand { get; }

    public AfsSenderViewModel(IAfsSessionService? sessionService = null)
    {
        this.sessionService = sessionService ?? new AfsUdpSessionService();
        StartCommand = new AsyncCommand(StartAsync, () => cancellation is null);
        CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => cancellation is not null);
        BrowseCaptureCommand = new RelayCommand(BrowseCapture, () => cancellation is null);
        OpenResultsCommand = new RelayCommand(() => ResultFolderLauncher.Open(ResultDirectory), () => Directory.Exists(ResultDirectory));
        LoadSettings();
    }

    private async Task StartAsync()
    {
        Begin();
        try
        {
            Validate();
            var root = RunRoot();
            var sender = new AfsSenderSettings(CapturePath, root);
            var transport = new AfsTransportSettings(BroadcastAddress, DataPort, ResultPort, RepeatCount,
                ResultTimeoutSeconds: ResultTimeoutSeconds, SimulatedDropRatePercent: DropRatePercent, SimulatedDropSeed: DropSeed);
            await SaveSettingsAsync();
            var result = await sessionService.SendAsync(sender, transport, Reporter(), cancellation!.Token);
            foreach (var metric in result.Metrics) Metrics.Add(metric);
            ResultDirectory = result.ResultDirectory;
            Verdict = result.Verdict.ToString(); State = "Completed"; Progress = 100;
        }
        catch (OperationCanceledException) { State = "Cancelled"; Verdict = Core.Verdict.Inconclusive.ToString(); }
        catch (Exception ex) { Fail(ex); }
        finally { End(); }
    }

    private void Validate()
    {
        if (!File.Exists(CapturePath)) throw new FileNotFoundException("capture.graw 파일을 선택하세요.", CapturePath);
        if (!System.Net.IPAddress.TryParse(BroadcastAddress, out _)) throw new ArgumentException("올바른 IPv4 Broadcast 주소를 입력하세요.");
        if (RepeatCount is < 1 or > 20) throw new ArgumentException("중복 송신 횟수는 1~20 범위여야 합니다.");
        if (DropRatePercent is < 0 or > 100) throw new ArgumentException("Drop Rate는 0~100% 범위여야 합니다.");
    }

    private IProgress<AfsSessionProgress> Reporter() => new Progress<AfsSessionProgress>(p => { State = $"{p.Stage}: {p.Message}"; Progress = p.Percent; Logs.Add($"{DateTime.Now:HH:mm:ss} {p.Message}"); });
    private void Begin() { cancellation = new(); Error = ""; Verdict = "-"; Progress = 0; Metrics.Clear(); Logs.Clear(); RaiseCommands(); }
    private void End() { cancellation?.Dispose(); cancellation = null; RaiseCommands(); }
    private void Fail(Exception ex) { State = "Failed"; Verdict = Core.Verdict.Inconclusive.ToString(); Error = ex.Message; Logs.Add(ex.ToString()); }
    private void RaiseCommands() { (StartCommand as AsyncCommand)?.Raise(); (CancelCommand as RelayCommand)?.Raise(); (BrowseCaptureCommand as RelayCommand)?.Raise(); (OpenResultsCommand as RelayCommand)?.Raise(); }
    private void BrowseCapture() { var d = new OpenFileDialog { Filter = "GNSS RAW (*.graw)|*.graw|All files (*.*)|*.*" }; if (d.ShowDialog() == true) CapturePath = d.FileName; }
    private static string RunRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "Runs");
    private static string SettingsPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "sender-settings.json");
    private async Task SaveSettingsAsync() { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath())!); await File.WriteAllTextAsync(SettingsPath(), JsonSerializer.Serialize(new Saved(CapturePath, BroadcastAddress, DataPort, ResultPort, RepeatCount, ResultTimeoutSeconds, DropRatePercent, DropSeed), JsonOptions)); }
    private void LoadSettings() { try { if (File.Exists(SettingsPath())) { var x = JsonSerializer.Deserialize<Saved>(File.ReadAllText(SettingsPath())); if (x is null) return; CapturePath=x.CapturePath; BroadcastAddress=x.BroadcastAddress; DataPort=x.DataPort; ResultPort=x.ResultPort; RepeatCount=x.RepeatCount; ResultTimeoutSeconds=x.ResultTimeoutSeconds; DropRatePercent=x.DropRatePercent; DropSeed=x.DropSeed; return; } var legacy=LegacyAfsSettings.Load(); if (legacy is not { } old) return; CapturePath=LegacyAfsSettings.Text(old,"CapturePath",CapturePath); BroadcastAddress=LegacyAfsSettings.Text(old,"BroadcastAddress",BroadcastAddress); DataPort=LegacyAfsSettings.Integer(old,"DataPort",DataPort); ResultPort=LegacyAfsSettings.Integer(old,"ResultPort",ResultPort); ResultTimeoutSeconds=LegacyAfsSettings.Integer(old,"ResultTimeoutSeconds",ResultTimeoutSeconds); } catch (JsonException) { } }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Saved(string CapturePath, string BroadcastAddress, int DataPort, int ResultPort, int RepeatCount, int ResultTimeoutSeconds, double DropRatePercent, int DropSeed);
}
