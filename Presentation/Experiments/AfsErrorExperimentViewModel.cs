using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;

namespace LnisAfsValidator.App;

/// <summary>네트워크와 분리된 Test B/C 오류정정 및 Test D 재동기 실험을 담당한다.</summary>
public sealed class AfsErrorExperimentViewModel : ObservableViewModel
{
    private CancellationTokenSource? cancellation;
    private string state = "Idle", verdict = "-", error = "", resultDirectory = "";
    private double progress;
    public IReadOnlyList<AfsErrorInjectionMode> ErrorModes { get; } = [AfsErrorInjectionMode.Random, AfsErrorInjectionMode.Burst, AfsErrorInjectionMode.SyncLoss];
    public AfsErrorInjectionMode ErrorMode { get; set; } = AfsErrorInjectionMode.Random;
    public string ErrorCountsText { get; set; } = "1, 2, 5, 10, 20, 50, 100";
    public int TrialsPerCondition { get; set; } = 100;
    public int ExperimentSeed { get; set; } = 1;
    public string State { get => state; private set => Set(ref state, value); }
    public string Verdict { get => verdict; private set => Set(ref verdict, value); }
    public string Error { get => error; private set => Set(ref error, value); }
    public string ResultDirectory { get => resultDirectory; private set => Set(ref resultDirectory, value); }
    public double Progress { get => progress; private set => Set(ref progress, value); }
    public ObservableCollection<AfsErrorCorrectionSummary> FecSummaries { get; } = [];
    public ObservableCollection<AfsSyncRecoverySummary> SyncSummaries { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenResultsCommand { get; }

    public AfsErrorExperimentViewModel()
    {
        RunCommand = new AsyncCommand(RunAsync, () => cancellation is null);
        CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => cancellation is not null);
        OpenResultsCommand = new RelayCommand(() => ResultFolderLauncher.Open(ResultDirectory), () => Directory.Exists(ResultDirectory));
        LoadSettings();
    }

    private async Task RunAsync()
    {
        Begin();
        try
        {
            var counts = AfsExperimentInputParser.ParseErrorCounts(ErrorCountsText);
            await SaveSettingsAsync();
            if (ErrorMode == AfsErrorInjectionMode.SyncLoss)
            {
                var result = await new AfsSyncRecoveryExperimentService().RunAsync(new(counts, TrialsPerCondition, ExperimentSeed), RunRoot(), Reporter(), cancellation!.Token);
                foreach (var row in result.Summaries) SyncSummaries.Add(row);
                ResultDirectory = result.ResultDirectory;
            }
            else
            {
                var result = await new AfsErrorCorrectionExperimentService().RunAsync(new(ErrorMode, counts, TrialsPerCondition, ExperimentSeed), RunRoot(), Reporter(), cancellation!.Token);
                foreach (var row in result.Summaries) FecSummaries.Add(row);
                ResultDirectory = result.ResultDirectory;
            }
            State = "Completed"; Verdict = "Measured"; Progress = 100;
            Logs.Add("정상·오류 AFS 프레임과 CSV/JSON 결과를 저장했습니다.");
        }
        catch (OperationCanceledException) { State = "Cancelled"; Verdict = Core.Verdict.Inconclusive.ToString(); }
        catch (Exception ex) { State = "Failed"; Verdict = Core.Verdict.Inconclusive.ToString(); Error = ex.Message; Logs.Add(ex.ToString()); }
        finally { End(); }
    }

    private IProgress<AfsSessionProgress> Reporter() => new Progress<AfsSessionProgress>(p => { State = $"{p.Stage}: {p.Message}"; Progress = p.Percent; Logs.Add($"{DateTime.Now:HH:mm:ss} {p.Message}"); });
    private void Begin() { cancellation = new(); Error = ""; Verdict = "-"; Progress = 0; FecSummaries.Clear(); SyncSummaries.Clear(); Logs.Clear(); RaiseCommands(); }
    private void End() { cancellation?.Dispose(); cancellation = null; RaiseCommands(); }
    private void RaiseCommands() { (RunCommand as AsyncCommand)?.Raise(); (CancelCommand as RelayCommand)?.Raise(); (OpenResultsCommand as RelayCommand)?.Raise(); }
    private static string RunRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "Runs");
    private static string SettingsPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LnisAfsValidator", "experiment-settings.json");
    private async Task SaveSettingsAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath())!);
            await File.WriteAllTextAsync(SettingsPath(), JsonSerializer.Serialize(new Saved(ErrorMode, ErrorCountsText, TrialsPerCondition, ExperimentSeed), JsonOptions));
        }
        catch (IOException)
        {
            // 여러 실험 창이 동시에 설정을 저장해도 실제 코덱 시험은 중단하지 않는다.
        }
    }
    private void LoadSettings() { try { if (File.Exists(SettingsPath())) { var x=JsonSerializer.Deserialize<Saved>(File.ReadAllText(SettingsPath())); if (x is null) return; ErrorMode=x.ErrorMode; ErrorCountsText=x.ErrorCountsText; TrialsPerCondition=x.TrialsPerCondition; ExperimentSeed=x.ExperimentSeed; return; } var legacy=LegacyAfsSettings.Load(); if (legacy is not { } old) return; ErrorCountsText=LegacyAfsSettings.Text(old,"ErrorCountsText",ErrorCountsText); TrialsPerCondition=LegacyAfsSettings.Integer(old,"TrialsPerCondition",TrialsPerCondition); ExperimentSeed=LegacyAfsSettings.Integer(old,"ExperimentSeed",ExperimentSeed); } catch (JsonException) { } }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Saved(AfsErrorInjectionMode ErrorMode, string ErrorCountsText, int TrialsPerCondition, int ExperimentSeed);
}
