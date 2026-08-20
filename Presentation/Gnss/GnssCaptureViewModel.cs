using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using LnisAfsValidator.Core;
using Microsoft.Win32;

namespace LnisAfsValidator.App;

/// <summary>프로토콜 독립 COM 수집, 원본 보존과 capture.graw 생성 화면 상태를 관리한다.</summary>
public sealed class GnssCaptureViewModel : ObservableViewModel
{
    private readonly IGnssCaptureService captureService;
    private readonly IGnssSerialPortCatalog portCatalog;
    private readonly IGnssProtocolAdapterCatalog protocolCatalog;
    private CancellationTokenSource? cancellation;
    private string selectedPort = string.Empty;
    private int baudRate = 115200;
    private GnssProtocolDescriptor? selectedProtocol;
    private string protocolDescription = string.Empty;
    private string outputRoot = AppWorkspacePaths.DefaultCapturesRoot;
    private string sessionName = "GNSS-Capture";
    private string receiverModel = "미지정 장비";
    private string firmwareVersion = "unknown";
    private bool dtrEnable;
    private bool rtsEnable;
    private bool isBusy;
    private string state = "대기";
    private string error = string.Empty;
    private string resultDirectory = string.Empty;
    private string rawPath = string.Empty;
    private string canonicalPath = string.Empty;
    private GnssCaptureStatistics statistics = new(0, 0, 0, 0, 0, 0, 0, 0);

    public GnssCaptureViewModel(
        IGnssCaptureService captureService,
        IGnssSerialPortCatalog portCatalog,
        IGnssProtocolAdapterCatalog protocolCatalog)
    {
        this.protocolCatalog = protocolCatalog;
        this.captureService = captureService;
        this.portCatalog = portCatalog;
        Protocols = new(this.protocolCatalog.Protocols);
        BaudRates = [9600, 38400, 57600, 115200, 230400, 460800, 921600];
        RefreshPortsCommand = new RelayCommand(RefreshPorts, () => !IsBusy);
        BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
        StartCommand = new AsyncCommand(StartAsync, () => !IsBusy);
        StopCommand = new RelayCommand(() => cancellation?.Cancel(), () => IsBusy);
        OpenResultsCommand = new RelayCommand(() => ResultFolderLauncher.Open(ResultDirectory), () => Directory.Exists(ResultDirectory));
        LoadSettings();
        SelectedProtocol ??= Protocols.FirstOrDefault(x => x.Id == "raw-only") ?? Protocols.FirstOrDefault();
        RefreshPorts();
    }

    public ObservableCollection<string> Ports { get; } = [];
    public ObservableCollection<GnssProtocolDescriptor> Protocols { get; }
    public IReadOnlyList<int> BaudRates { get; }
    public string SelectedPort { get => selectedPort; set => Set(ref selectedPort, value); }
    public int BaudRate { get => baudRate; set => Set(ref baudRate, value); }
    public GnssProtocolDescriptor? SelectedProtocol
    {
        get => selectedProtocol;
        set { if (Set(ref selectedProtocol, value)) ProtocolDescription = value?.Description ?? string.Empty; }
    }
    public string ProtocolDescription { get => protocolDescription; private set => Set(ref protocolDescription, value); }
    public string OutputRoot { get => outputRoot; set => Set(ref outputRoot, value); }
    public string SessionName { get => sessionName; set => Set(ref sessionName, value); }
    public string ReceiverModel { get => receiverModel; set => Set(ref receiverModel, value); }
    public string FirmwareVersion { get => firmwareVersion; set => Set(ref firmwareVersion, value); }
    public bool DtrEnable { get => dtrEnable; set => Set(ref dtrEnable, value); }
    public bool RtsEnable { get => rtsEnable; set => Set(ref rtsEnable, value); }
    public bool IsBusy { get => isBusy; private set { if (Set(ref isBusy, value)) RaiseCommands(); } }
    public string State { get => state; private set => Set(ref state, value); }
    public string Error { get => error; private set => Set(ref error, value); }
    public string ResultDirectory { get => resultDirectory; private set { if (Set(ref resultDirectory, value)) ((RelayCommand)OpenResultsCommand).Raise(); } }
    public string RawPath { get => rawPath; private set => Set(ref rawPath, value); }
    public string CanonicalPath { get => canonicalPath; private set => Set(ref canonicalPath, value); }
    public GnssCaptureStatistics Statistics { get => statistics; private set => Set(ref statistics, value); }
    public ObservableCollection<string> Logs { get; } = [];
    public ICommand RefreshPortsCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand OpenResultsCommand { get; }
    public event EventHandler<string>? CanonicalCaptureReady;

    private async Task StartAsync()
    {
        try
        {
            Validate(); IsBusy = true; Error = string.Empty; State = "COM 연결 중";
            ResultDirectory = RawPath = CanonicalPath = string.Empty; Statistics = new(0, 0, 0, 0, 0, 0, 0, 0); Logs.Clear();
            cancellation = new CancellationTokenSource(); await SaveSettingsAsync();
            var settings = new GnssSerialCaptureSettings(SelectedPort, BaudRate, SelectedProtocol!.Id, OutputRoot, SessionName, ReceiverModel, FirmwareVersion, DtrEnable, RtsEnable);
            var progress = new Progress<GnssCaptureProgress>(x => { Statistics = x.Statistics; State = x.Message; Logs.Add($"[{DateTime.Now:HH:mm:ss}] {x.Message}"); });
            var result = await captureService.CaptureAsync(settings, progress, cancellation.Token);
            ResultDirectory = result.Directory; RawPath = result.RawSerialPath; CanonicalPath = result.CanonicalPath; Statistics = result.Statistics;
            Error = result.Error ?? string.Empty; State = result.Error is null ? "수집 완료" : "수집 실패";
            if (File.Exists(CanonicalPath)) CanonicalCaptureReady?.Invoke(this, CanonicalPath);
        }
        catch (Exception ex) { Error = ex.Message; State = "수집 실패"; }
        finally { cancellation?.Dispose(); cancellation = null; IsBusy = false; }
    }

    private void RefreshPorts()
    {
        var previous = SelectedPort; Ports.Clear();
        foreach (var port in portCatalog.GetPortNames()) Ports.Add(port);
        if (!string.IsNullOrWhiteSpace(previous)) SelectedPort = previous; else if (Ports.Count > 0) SelectedPort = Ports[0];
    }

    private void BrowseOutput()
    {
        var dialog = new OpenFolderDialog { Title = "GNSS 캡처 저장 폴더", InitialDirectory = OutputRoot };
        if (dialog.ShowDialog() == true) OutputRoot = dialog.FolderName;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(SelectedPort)) throw new InvalidOperationException("COM 포트를 선택하거나 직접 입력하세요.");
        if (SelectedProtocol is null) throw new InvalidOperationException("프로토콜 어댑터를 선택하세요.");
        if (string.IsNullOrWhiteSpace(OutputRoot)) throw new InvalidOperationException("저장 폴더를 선택하세요.");
    }

    private void RaiseCommands()
    {
        ((RelayCommand)RefreshPortsCommand).Raise(); ((RelayCommand)BrowseOutputCommand).Raise(); ((AsyncCommand)StartCommand).Raise(); ((RelayCommand)StopCommand).Raise();
    }

    private static string SettingsPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "gnss-capture-settings.json");
    private async Task SaveSettingsAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath())!);
        var saved = new Saved(SelectedPort, BaudRate, SelectedProtocol?.Id ?? "raw-only", OutputRoot, SessionName, ReceiverModel, FirmwareVersion, DtrEnable, RtsEnable);
        await File.WriteAllTextAsync(SettingsPath(), JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
    }
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath())) return;
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(SettingsPath())); if (saved is null) return;
            SelectedPort = saved.PortName; BaudRate = saved.BaudRate; OutputRoot = AppWorkspacePaths.ResolveCapturesRoot(saved.OutputRoot); SessionName = saved.SessionName;
            ReceiverModel = saved.ReceiverModel; FirmwareVersion = saved.FirmwareVersion; DtrEnable = saved.DtrEnable; RtsEnable = saved.RtsEnable;
            SelectedProtocol = Protocols.FirstOrDefault(x => x.Id.Equals(saved.ProtocolId, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException) { }
    }

    private sealed record Saved(string PortName, int BaudRate, string ProtocolId, string? OutputRoot, string SessionName, string ReceiverModel, string FirmwareVersion, bool DtrEnable, bool RtsEnable);
}
