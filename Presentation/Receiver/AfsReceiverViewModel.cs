using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;

namespace LnisAfsValidator.App;

/// <summary>Test A/E의 UDP 수신, AFS 복호, RAW 복원과 판정만 담당한다.</summary>
public sealed class AfsReceiverViewModel : ObservableViewModel
{
    private readonly IAfsSessionService sessionService;
    private CancellationTokenSource? cancellation;
    private string state = "Idle", verdict = "-", error = "", resultDirectory = "";
    private double progress;
    public int DataPort { get; set; } = 45821;
    public int ResultPort { get; set; } = 45822;
    public int RepeatCount { get; set; } = 3;
    public bool ApplyDelivery { get; set; }
    public double MinimumDelivery { get; set; } = 99;
    public bool ApplyLoss { get; set; }
    public double MaximumLoss { get; set; } = 1;
    public string State { get => state; private set => Set(ref state, value); }
    public string Verdict { get => verdict; private set => Set(ref verdict, value); }
    public string Error { get => error; private set => Set(ref error, value); }
    public string ResultDirectory { get => resultDirectory; private set => Set(ref resultDirectory, value); }
    public double Progress { get => progress; private set => Set(ref progress, value); }
    public ObservableCollection<PerformanceMetric> Metrics { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public ICommand StartCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenResultsCommand { get; }

    public AfsReceiverViewModel(IAfsSessionService? sessionService = null)
    {
        this.sessionService = sessionService ?? new AfsUdpSessionService();
        StartCommand = new AsyncCommand(StartAsync, () => cancellation is null);
        CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => cancellation is not null);
        OpenResultsCommand = new RelayCommand(() => ResultFolderLauncher.Open(ResultDirectory), () => Directory.Exists(ResultDirectory));
        LoadSettings();
    }

    private async Task StartAsync()
    {
        Begin();
        try
        {
            Validate();
            var thresholds = new Dictionary<string, MetricThreshold>
            {
                ["PacketDeliveryRate"] = new(ApplyDelivery, MinimumDelivery, true),
                ["PacketLossRate"] = new(ApplyLoss, MaximumLoss, false)
            };
            var receiver = new AfsReceiverSettings(RunRoot(), Thresholds: thresholds);
            var transport = new AfsTransportSettings(DataPort: DataPort, ResultPort: ResultPort, RepeatCount: RepeatCount);
            await SaveSettingsAsync();
            var result = await sessionService.ReceiveAsync(receiver, transport, Reporter(), cancellation!.Token);
            foreach (var metric in result.Metrics) Metrics.Add(metric);
            ResultDirectory = result.ResultDirectory;
            Verdict = result.Verdict.ToString(); State = "Completed"; Progress = 100;
        }
        catch (OperationCanceledException) { State = "Cancelled"; Verdict = Core.Verdict.Inconclusive.ToString(); }
        catch (Exception ex) { State = "Failed"; Verdict = Core.Verdict.Inconclusive.ToString(); Error = ex.Message; Logs.Add(ex.ToString()); }
        finally { End(); }
    }

    private void Validate() { if (DataPort is < 1 or > 65535 || ResultPort is < 1 or > 65535 || DataPort == ResultPort) throw new ArgumentException("데이터 포트와 결과 포트는 서로 다른 1~65535 값이어야 합니다."); if (RepeatCount is < 1 or > 20) throw new ArgumentException("중복 송신 횟수는 1~20 범위여야 합니다."); }
    private IProgress<AfsSessionProgress> Reporter() => new Progress<AfsSessionProgress>(p => { State = $"{p.Stage}: {p.Message}"; Progress = p.Percent; Logs.Add($"{DateTime.Now:HH:mm:ss} {p.Message}"); });
    private void Begin() { cancellation = new(); Error = ""; Verdict = "-"; Progress = 0; Metrics.Clear(); Logs.Clear(); RaiseCommands(); }
    private void End() { cancellation?.Dispose(); cancellation = null; RaiseCommands(); }
    private void RaiseCommands() { (StartCommand as AsyncCommand)?.Raise(); (CancelCommand as RelayCommand)?.Raise(); (OpenResultsCommand as RelayCommand)?.Raise(); }
    private static string RunRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "Runs");
    private static string SettingsPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "receiver-settings.json");
    private async Task SaveSettingsAsync() { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath())!); await File.WriteAllTextAsync(SettingsPath(), JsonSerializer.Serialize(new Saved(DataPort, ResultPort, RepeatCount, ApplyDelivery, MinimumDelivery, ApplyLoss, MaximumLoss), JsonOptions)); }
    private void LoadSettings() { try { if (File.Exists(SettingsPath())) { var x=JsonSerializer.Deserialize<Saved>(File.ReadAllText(SettingsPath())); if (x is null) return; DataPort=x.DataPort; ResultPort=x.ResultPort; RepeatCount=x.RepeatCount; ApplyDelivery=x.ApplyDelivery; MinimumDelivery=x.MinimumDelivery; ApplyLoss=x.ApplyLoss; MaximumLoss=x.MaximumLoss; return; } var legacy=LegacyAfsSettings.Load(); if (legacy is not { } old) return; DataPort=LegacyAfsSettings.Integer(old,"DataPort",DataPort); ResultPort=LegacyAfsSettings.Integer(old,"ResultPort",ResultPort); } catch (JsonException) { } }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Saved(int DataPort, int ResultPort, int RepeatCount, bool ApplyDelivery, double MinimumDelivery, bool ApplyLoss, double MaximumLoss);
}
