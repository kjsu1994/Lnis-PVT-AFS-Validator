using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using LnisAfsValidator.Core;
using Microsoft.Win32;

namespace LnisAfsValidator.App;

/// <summary>파일 또는 COM으로 준비한 GNSS RAW에 선택한 Test A~E 조건을 적용해 송신한다.</summary>
public sealed class AfsSenderViewModel : ObservableViewModel
{
    private readonly IAfsSessionService sessionService;
    private CancellationTokenSource? cancellation;
    private string capturePath = "", broadcastAddress = "255.255.255.255";
    private string resultRoot = AppWorkspacePaths.DefaultRunsRoot;
    private string state = "Idle", verdict = "-", error = "", resultDirectory = "";
    private double progress, dropRate;
    private AfsEndToEndTestType selectedTest = AfsEndToEndTestType.TestA_Normal;

    public string CapturePath { get => capturePath; set => Set(ref capturePath, value); }
    public string BroadcastAddress { get => broadcastAddress; set => Set(ref broadcastAddress, value); }
    public string ResultRoot { get => resultRoot; set => Set(ref resultRoot, value); }
    public int DataPort { get; set; } = 45821;
    public int ResultPort { get; set; } = 45822;
    public int RepeatCount { get; set; } = 3;
    public int ResultTimeoutSeconds { get; set; } = 30;
    public IReadOnlyList<AfsEndToEndTestType> TestTypes { get; } = Enum.GetValues<AfsEndToEndTestType>();
    public AfsEndToEndTestType SelectedTest { get => selectedTest; set => Set(ref selectedTest, value); }
    public int ErrorCount { get; set; } = 1;
    public int ErrorSeed { get; set; } = 1;
    public int SyncDamageInterval { get; set; } = 10;
    public double DropRatePercent { get => dropRate; set => Set(ref dropRate, value); }
    public int DropSeed { get; set; } = 1;
    public string State { get => state; private set => Set(ref state, value); }
    public string Verdict { get => verdict; private set => Set(ref verdict, value); }
    public string Error { get => error; private set => Set(ref error, value); }
    public string ResultDirectory { get => resultDirectory; private set => Set(ref resultDirectory, value); }
    public double Progress { get => progress; private set => Set(ref progress, value); }
    public ObservableCollection<PerformanceMetric> Metrics { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public GnssCaptureViewModel GnssCapture { get; }
    public ICommand StartCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BrowseCaptureCommand { get; }
    public ICommand BrowseResultRootCommand { get; }
    public ICommand OpenResultsCommand { get; }

    public AfsSenderViewModel(
        IAfsSessionService sessionService,
        GnssCaptureViewModel gnssCapture)
    {
        this.sessionService = sessionService;
        GnssCapture = gnssCapture;
        GnssCapture.CanonicalCaptureReady += (_, path) => CapturePath = path;
        StartCommand = new AsyncCommand(StartAsync, () => cancellation is null);
        CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => cancellation is not null);
        BrowseCaptureCommand = new RelayCommand(BrowseCapture, () => cancellation is null);
        BrowseResultRootCommand = new RelayCommand(BrowseResultRoot, () => cancellation is null);
        OpenResultsCommand = new RelayCommand(() => ResultFolderLauncher.Open(ResultDirectory), () => Directory.Exists(ResultDirectory));
        LoadSettings();
    }

    private async Task StartAsync()
    {
        Begin();
        try
        {
            Validate();
            var sender = new AfsSenderSettings(CapturePath, ResultRoot, TestType: SelectedTest,
                ErrorCount: ErrorCount, ErrorSeed: ErrorSeed, SyncDamageInterval: SyncDamageInterval);
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
        if (string.IsNullOrWhiteSpace(ResultRoot)) throw new ArgumentException("결과 저장 폴더를 선택하세요.");
        if (!System.Net.IPAddress.TryParse(BroadcastAddress, out _)) throw new ArgumentException("올바른 IPv4 Broadcast 주소를 입력하세요.");
        if (RepeatCount is < 1 or > 20) throw new ArgumentException("중복 송신 횟수는 1~20 범위여야 합니다.");
        if (DropRatePercent is < 0 or > 100) throw new ArgumentException("Drop Rate는 0~100% 범위여야 합니다.");
        if (SelectedTest is AfsEndToEndTestType.TestB_RandomErrors or AfsEndToEndTestType.TestC_BurstErrors && ErrorCount is < 1 or > AfsProtocolLimits.SubframePayloadSymbolCount) throw new ArgumentException("Test B/C 오류 개수는 1~5880 범위여야 합니다.");
        if (SelectedTest == AfsEndToEndTestType.TestD_SyncRecovery && ErrorCount is < 1 or > AfsProtocolLimits.SyncPatternSymbolCount) throw new ArgumentException("Test D SP 오류 개수는 1~68 범위여야 합니다.");
        if (SyncDamageInterval < 1) throw new ArgumentException("Test D 손상 간격은 1 이상이어야 합니다.");
    }

    private IProgress<AfsSessionProgress> Reporter() => new Progress<AfsSessionProgress>(p => { State = $"{p.Stage}: {p.Message}"; Progress = p.Percent; Logs.Add($"{DateTime.Now:HH:mm:ss} {p.Message}"); });
    private void Begin() { cancellation = new(); Error = ""; Verdict = "-"; Progress = 0; Metrics.Clear(); Logs.Clear(); RaiseCommands(); }
    private void End() { cancellation?.Dispose(); cancellation = null; RaiseCommands(); }
    private void Fail(Exception ex) { State = "Failed"; Verdict = Core.Verdict.Inconclusive.ToString(); Error = ex.Message; Logs.Add(ex.ToString()); }
    private void RaiseCommands() { (StartCommand as AsyncCommand)?.Raise(); (CancelCommand as RelayCommand)?.Raise(); (BrowseCaptureCommand as RelayCommand)?.Raise(); (BrowseResultRootCommand as RelayCommand)?.Raise(); (OpenResultsCommand as RelayCommand)?.Raise(); }
    private void BrowseCapture() { var d = new OpenFileDialog { Filter = "GNSS RAW (*.graw)|*.graw|All files (*.*)|*.*" }; if (d.ShowDialog() == true) CapturePath = d.FileName; }
    private void BrowseResultRoot() { var d = new OpenFolderDialog { Title = "송신 결과 저장 폴더", InitialDirectory = Directory.Exists(ResultRoot) ? ResultRoot : AppWorkspacePaths.DefaultWorkspaceRoot }; if (d.ShowDialog() == true) ResultRoot = d.FolderName; }
    private static string SettingsPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "sender-settings.json");
    private async Task SaveSettingsAsync() { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath())!); await File.WriteAllTextAsync(SettingsPath(), JsonSerializer.Serialize(new Saved(CapturePath, BroadcastAddress, DataPort, ResultPort, RepeatCount, ResultTimeoutSeconds, SelectedTest, ErrorCount, ErrorSeed, SyncDamageInterval, DropRatePercent, DropSeed, ResultRoot), JsonOptions)); }
    private void LoadSettings() { try { if (File.Exists(SettingsPath())) { var x = JsonSerializer.Deserialize<Saved>(File.ReadAllText(SettingsPath())); if (x is null) return; CapturePath=x.CapturePath; BroadcastAddress=x.BroadcastAddress; DataPort=x.DataPort; ResultPort=x.ResultPort; RepeatCount=x.RepeatCount; ResultTimeoutSeconds=x.ResultTimeoutSeconds; SelectedTest=x.SelectedTest; ErrorCount=x.ErrorCount > 0 ? x.ErrorCount : ErrorCount; ErrorSeed=x.ErrorSeed; SyncDamageInterval=x.SyncDamageInterval > 0 ? x.SyncDamageInterval : SyncDamageInterval; DropRatePercent=x.DropRatePercent; DropSeed=x.DropSeed; ResultRoot=AppWorkspacePaths.ResolveRunsRoot(x.ResultRoot); return; } var legacy=LegacyAfsSettings.Load(); if (legacy is not { } old) return; CapturePath=LegacyAfsSettings.Text(old,"CapturePath",CapturePath); BroadcastAddress=LegacyAfsSettings.Text(old,"BroadcastAddress",BroadcastAddress); DataPort=LegacyAfsSettings.Integer(old,"DataPort",DataPort); ResultPort=LegacyAfsSettings.Integer(old,"ResultPort",ResultPort); ResultTimeoutSeconds=LegacyAfsSettings.Integer(old,"ResultTimeoutSeconds",ResultTimeoutSeconds); } catch (JsonException) { } }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Saved(string CapturePath, string BroadcastAddress, int DataPort, int ResultPort, int RepeatCount, int ResultTimeoutSeconds, AfsEndToEndTestType SelectedTest, int ErrorCount, int ErrorSeed, int SyncDamageInterval, double DropRatePercent, int DropSeed, string? ResultRoot);
}
