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

public sealed class AfsMainViewModel : INotifyPropertyChanged
{
    private CancellationTokenSource? cancellation;
    private string state = "Idle", verdict = "-", error = "", runDirectory = "", capturePath = "", almanacPath = "", broadcastAddress = "255.255.255.255";
    private double progress;
    public Array Roles => Enum.GetValues<RunRole>();
    public RunRole Role { get; set; } = RunRole.Local;
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
    public ICommand StartCommand { get; } public ICommand CancelCommand { get; } public ICommand OpenResultsCommand { get; }
    public ICommand BrowseCaptureCommand { get; } public ICommand BrowseAlmanacCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public AfsMainViewModel()
    {
        StartCommand = new AsyncCommand(StartAsync, () => cancellation is null); CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => cancellation is not null);
        OpenResultsCommand = new RelayCommand(OpenResults, () => Directory.Exists(RunDirectory)); BrowseCaptureCommand = new RelayCommand(BrowseCapture, () => cancellation is null); BrowseAlmanacCommand = new RelayCommand(BrowseAlmanac, () => cancellation is null); Load();
    }

    private async Task StartAsync()
    {
        cancellation = new(); Refresh(); Error = ""; Verdict = "-"; Metrics.Clear(); Logs.Clear();
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "Runs");
            var thresholds = new Dictionary<string, MetricThreshold> { ["LinkAvailability"] = new(ApplyAvailability, MinimumAvailability, true), ["AverageLatency"] = new(ApplyAverageLatency, MaximumAverageLatency, false), ["MaximumLatency"] = new(ApplyMaximumLatency, MaximumLatency, false), ["Throughput"] = new(ApplyThroughput, MinimumThroughput, true), ["PacketLossRate"] = new(ApplyLoss, MaximumLoss, false), ["PacketDeliveryRate"] = new(ApplyDelivery, MinimumDelivery, true) };
            var test = new AfsTestSettings(CapturePath, AlmanacPath, root, Thresholds: thresholds); var network = new AfsTransportSettings(BroadcastAddress, DataPort, ResultPort, ResultTimeoutSeconds: ResultTimeoutSeconds);
            await SaveAsync(); var reporter = new Progress<AfsSessionProgress>(p => { State = $"{p.Stage}: {p.Message}"; Progress = p.Percent; Logs.Add($"{DateTime.Now:HH:mm:ss} {p.Message}"); });
            AfsSessionResult result;
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
    private void BrowseAlmanac() { var d = new OpenFileDialog { Filter = "Almanac (*.txt)|*.txt|All files (*.*)|*.*" }; if (d.ShowDialog() == true) AlmanacPath = d.FileName; }
    private void OpenResults() { if (Directory.Exists(RunDirectory)) Process.Start(new ProcessStartInfo(RunDirectory) { UseShellExecute = true }); }
    private string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "afs-settings.json");
    private async Task SaveAsync() { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(new Saved(Role, CapturePath, AlmanacPath, BroadcastAddress, DataPort, ResultPort, ResultTimeoutSeconds), new JsonSerializerOptions { WriteIndented = true })); }
    private void Load() { try { if (!File.Exists(SettingsPath)) return; var x = JsonSerializer.Deserialize<Saved>(File.ReadAllText(SettingsPath)); if (x is null) return; Role = x.Role; CapturePath = x.CapturePath; AlmanacPath = x.AlmanacPath; BroadcastAddress = x.BroadcastAddress; DataPort = x.DataPort; ResultPort = x.ResultPort; ResultTimeoutSeconds = x.ResultTimeoutSeconds; } catch (JsonException) { } }
    private void Refresh() { (StartCommand as AsyncCommand)?.Raise(); (CancelCommand as RelayCommand)?.Raise(); (OpenResultsCommand as RelayCommand)?.Raise(); (BrowseCaptureCommand as RelayCommand)?.Raise(); (BrowseAlmanacCommand as RelayCommand)?.Raise(); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
    private sealed record Saved(RunRole Role, string CapturePath, string AlmanacPath, string BroadcastAddress, int DataPort, int ResultPort, int ResultTimeoutSeconds);
}

