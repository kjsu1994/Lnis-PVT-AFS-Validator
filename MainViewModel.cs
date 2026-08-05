using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;
namespace LnisAfsValidator.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public GnssCaptureViewModel GnssCapture { get; } = new();
    private CancellationTokenSource? cancellation;
    private string state = "Idle", verdict = "-", error = "", runDirectory = "";
    private double progress;
    public Array Roles => Enum.GetValues<RunRole>(); public Array Modes => Enum.GetValues<ExecutionMode>();
    public RunRole Role { get; set; } = RunRole.Local; public string RemoteAddress { get; set; } = "127.0.0.1"; public int Port { get; set; } = 45821;
    public ExecutionMode GeneratorMode { get; set; } = ExecutionMode.Wsl; public string GeneratorPath { get; set; } = "/home/imt/LANS-AFS-SIM/afs_sim"; public string GeneratorWorking { get; set; } = "/home/imt/LANS-AFS-SIM";
    public ExecutionMode ReceiverMode { get; set; } = ExecutionMode.Wsl; public string ReceiverPath { get; set; } = "/home/imt/PocketSDR-AFS/app/pocket_trk/pocket_trk"; public string ReceiverWorking { get; set; } = "/home/imt/PocketSDR-AFS/app/pocket_trk";
    public string WslDistribution { get; set; } = "Ubuntu"; public string WslRunRoot { get; set; } = "/home/imt/.local/share/lnis-afs-validator";
    public string ScenarioName { get; set; } = "AFS 90-second test"; public double Latitude { get; set; } = -89.66; public double Longitude { get; set; } = 129.20; public double Height { get; set; } = 100;
    public double PositionTolerance { get; set; } public double TimeTolerance { get; set; } public int Duration { get; set; } = 90; public double SampleRate { get; set; } = 12; public string AlmanacPath { get; set; } = "default_almanac.txt"; public string Prns { get; set; } = "2-8"; public int MinimumSatellites { get; set; } = 4;
    public string State { get => state; private set => Set(ref state, value); } public string Verdict { get => verdict; private set => Set(ref verdict, value); } public string Error { get => error; private set => Set(ref error, value); }
    public string RunDirectory { get => runDirectory; private set => Set(ref runDirectory, value); } public double Progress { get => progress; private set => Set(ref progress, value); }
    public ObservableCollection<string> Logs { get; } = []; public ObservableCollection<CheckResult> Checks { get; } = [];
    public ICommand StartCommand { get; } public ICommand CancelCommand { get; } public ICommand OpenResultsCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        StartCommand = new AsyncCommand(StartAsync, () => cancellation is null); CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => cancellation is not null);
        OpenResultsCommand = new RelayCommand(OpenResults, () => Directory.Exists(RunDirectory)); LoadSettings();
    }

    private async Task StartAsync()
    {
        cancellation = new(); RefreshCommands(); Error = ""; Verdict = "-"; Logs.Clear(); Checks.Clear();
        try
        {
            var settings = BuildSettings(); await SaveSettingsAsync(settings); var reporter = new Progress<RunProgress>(p => { State = p.State + ": " + p.Message; Progress = p.Percent; });
            var outcome = await new TestOrchestrator(settings).RunAsync(reporter, AddLog, cancellation.Token); RunDirectory = outcome.RunDirectory; Verdict = outcome.Result.Verdict.ToString();
            foreach (var check in outcome.Result.Checks) Checks.Add(check);
        }
        catch (OperationCanceledException) { State = "Cancelled"; Verdict = LnisAfsValidator.Core.Verdict.Inconclusive.ToString(); }
        catch (Exception ex) { State = "Failed"; Error = ex.Message; Verdict = LnisAfsValidator.Core.Verdict.Inconclusive.ToString(); AddLog(new(DateTimeOffset.Now, true, ex.ToString())); }
        finally { cancellation.Dispose(); cancellation = null; RefreshCommands(); }
    }

    private ApplicationSettings BuildSettings()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "Runs");
        var scenario = new TestScenario(ScenarioName, new(Latitude, Longitude, Height), PositionTolerance, TimeTolerance, Duration, SampleRate, AlmanacPath, Prns, MinimumSatellites);
        var generator = new ToolConfiguration(GeneratorMode, GeneratorPath, GeneratorWorking, WslDistribution, WslRunRoot);
        var receiver = new ToolConfiguration(ReceiverMode, ReceiverPath, ReceiverWorking, WslDistribution, WslRunRoot);
        return new(Role, RemoteAddress, Port, 1024 * 1024, 32L * 1024 * 1024 * 1024, 30, 60, 30, root, generator, receiver, scenario);
    }

    private void AddLog(ProcessLogLine line) => System.Windows.Application.Current.Dispatcher.Invoke(() => Logs.Add($"{line.Timestamp:HH:mm:ss} {(line.IsError ? "ERR" : "OUT")} {line.Text}"));
    private void OpenResults() { if (Directory.Exists(RunDirectory)) Process.Start(new ProcessStartInfo(RunDirectory) { UseShellExecute = true }); }
    private string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "settings.json");
    private async Task SaveSettingsAsync(ApplicationSettings value) { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })); }
    private void LoadSettings()
    {
        if (!File.Exists(SettingsPath)) return;
        try
        {
            var s = JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(SettingsPath)); if (s is null) return;
            Role=s.Role; RemoteAddress=s.RemoteAddress; Port=s.Port; GeneratorMode=s.Generator.Mode; GeneratorPath=s.Generator.ExecutablePath; GeneratorWorking=s.Generator.WorkingDirectory;
            ReceiverMode=s.Receiver.Mode; ReceiverPath=s.Receiver.ExecutablePath; ReceiverWorking=s.Receiver.WorkingDirectory; WslDistribution=s.Generator.WslDistribution; WslRunRoot=s.Generator.WslRunRoot;
            ScenarioName=s.Scenario.Name; Latitude=s.Scenario.ReferencePosition.LatitudeDegrees; Longitude=s.Scenario.ReferencePosition.LongitudeDegrees; Height=s.Scenario.ReferencePosition.HeightMeters;
            PositionTolerance=s.Scenario.PositionToleranceMeters; TimeTolerance=s.Scenario.TimeToleranceSeconds; Duration=s.Scenario.DurationSeconds; SampleRate=s.Scenario.SampleRateMHz; AlmanacPath=s.Scenario.AlmanacPath; Prns=s.Scenario.Prns; MinimumSatellites=s.Scenario.MinimumSatellites;
        }
        catch (JsonException) { }
    }
    private void RefreshCommands() { (StartCommand as AsyncCommand)?.Raise(); (CancelCommand as RelayCommand)?.Raise(); (OpenResultsCommand as RelayCommand)?.Raise(); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
}

public sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p) => canExecute(); public void Execute(object? p) => execute(); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
public sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p) => canExecute(); public async void Execute(object? p) => await execute(); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
