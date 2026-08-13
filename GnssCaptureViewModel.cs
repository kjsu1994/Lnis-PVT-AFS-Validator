using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;

namespace LnisAfsValidator.App;

/// <summary>GNSS 데이터를 시리얼 포트에서 받을지 저장 파일에서 재생할지 구분한다.</summary>
public enum GnssCaptureMode { Serial, FileReplay }

/// <summary>GNSS 캡처 입력 설정, 실행·취소 명령, 통계와 로그 표시를 관리한다.</summary>
public sealed class GnssCaptureViewModel : INotifyPropertyChanged
{
    private readonly IGnssCaptureService service = new GnssCaptureService();
    private CancellationTokenSource? cancellation;
    private GnssCaptureMode mode;
    private string portName = "", replayPath = "", sessionName = "ZED-F9P capture", state = "Idle", runDirectory = "", error = "";
    private int baudRate = 115200;
    private GnssCaptureStatistics statistics = new(0, 0, 0, 0, 0, 0, 0, 0);
    public Array Modes { get; } = Enum.GetValues<GnssCaptureMode>();
    public ObservableCollection<string> Ports { get; } = [];
    public GnssCaptureMode Mode { get => mode; set => Set(ref mode, value); }
    public string PortName { get => portName; set => Set(ref portName, value); }
    public int BaudRate { get => baudRate; set => Set(ref baudRate, value); }
    public string ReplayPath { get => replayPath; set => Set(ref replayPath, value); }
    public string SessionName { get => sessionName; set => Set(ref sessionName, value); }
    public string State { get => state; private set => Set(ref state, value); }
    public string RunDirectory { get => runDirectory; private set { if (Set(ref runDirectory, value)) (OpenResultsCommand as RelayCommand)?.Raise(); } }
    public string Error { get => error; private set => Set(ref error, value); }
    public GnssCaptureStatistics Statistics { get => statistics; private set => Set(ref statistics, value); }
    public string ResultRoot { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "GnssCaptures");
    public ICommand RefreshPortsCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand OpenResultsCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public GnssCaptureViewModel()
    {
        RefreshPortsCommand = new RelayCommand(RefreshPorts, () => cancellation is null); StartCommand = new AsyncCommand(StartAsync, () => cancellation is null);
        StopCommand = new RelayCommand(() => cancellation?.Cancel(), () => cancellation is not null); OpenResultsCommand = new RelayCommand(OpenResults, () => Directory.Exists(RunDirectory)); RefreshPorts();
    }
    private void RefreshPorts() { var selected = PortName; Ports.Clear(); foreach (var port in SerialPort.GetPortNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) Ports.Add(port); PortName = Ports.Contains(selected) ? selected : Ports.FirstOrDefault() ?? ""; }
    private async Task StartAsync()
    {
        cancellation = new(); RefreshCommands(); Error = ""; RunDirectory = ""; Statistics = new(0, 0, 0, 0, 0, 0, 0, 0); State = "Preparing";
        try
        {
            Directory.CreateDirectory(ResultRoot); var progress = new Progress<GnssCaptureProgress>(p => { Statistics = p.Statistics; State = p.Message; });
            var result = Mode == GnssCaptureMode.Serial ? await service.CaptureSerialAsync(PortName, BaudRate, SessionName, ResultRoot, progress, cancellation.Token) : await service.ReplayFileAsync(ReplayPath, SessionName, ResultRoot, progress, cancellation.Token);
            Statistics = result.Statistics; RunDirectory = result.Directory; State = result.Completed ? "Completed" : result.Error ?? "Failed"; Error = result.Completed || result.Error == "Cancelled" ? "" : result.Error ?? "";
        }
        catch (Exception ex) { State = "Failed"; Error = ex.Message; }
        finally { cancellation.Dispose(); cancellation = null; RefreshCommands(); }
    }
    private void OpenResults() { if (Directory.Exists(RunDirectory)) Process.Start(new ProcessStartInfo(RunDirectory) { UseShellExecute = true }); }
    private void RefreshCommands() { (RefreshPortsCommand as RelayCommand)?.Raise(); (StartCommand as AsyncCommand)?.Raise(); (StopCommand as RelayCommand)?.Raise(); (OpenResultsCommand as RelayCommand)?.Raise(); }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; PropertyChanged?.Invoke(this, new(name)); return true; }
}
