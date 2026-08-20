using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Input;
using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;

namespace LnisAfsValidator.App;

/// <summary>
/// WPF 응용 프로그램의 수명과 App.xaml 리소스를 연결하는 진입 클래스다.
/// </summary>
public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        // App이 유일한 Composition Root가 되어 Presentation에는 Core 인터페이스만 주입한다.
        var sessionService = CreateSessionService();
        AfsSenderViewModel CreateSenderViewModel() =>
            new(sessionService, CreateGnssCaptureViewModel());
        AfsReceiverViewModel CreateReceiverViewModel() =>
            new(sessionService);

        // 일반 실행은 대시보드를 열고, 역할 인수는 반복 가능한 자동 인수시험 창을 직접 연다.
        var arguments = e.Args.ToDictionary(
            x => x.Split('=', 2)[0],
            x => x.Contains('=') ? x.Split('=', 2)[1] : "true",
            StringComparer.OrdinalIgnoreCase);

        Window window;
        ICommand? start = null;
        // 역할별 ViewModel에 명령행 시험값을 주입하되 실제 실행 경로는 UI 명령과 동일하게 유지한다.
        if (arguments.ContainsKey("--sender"))
        {
            var vm = CreateSenderViewModel();
            if (arguments.TryGetValue("--capture", out var capture)) vm.CapturePath = capture;
            if (arguments.TryGetValue("--broadcast", out var address)) vm.BroadcastAddress = address;
            if (Value(arguments, "--data-port") is { } data) vm.DataPort = data;
            if (Value(arguments, "--result-port") is { } result) vm.ResultPort = result;
            if (Value(arguments, "--repeat") is { } repeat) vm.RepeatCount = repeat;
            if (Value(arguments, "--timeout") is { } timeout) vm.ResultTimeoutSeconds = timeout;
            if (arguments.TryGetValue("--test", out var test) && Enum.TryParse<AfsEndToEndTestType>(test, true, out var parsedTest)) vm.SelectedTest = parsedTest;
            if (Value(arguments, "--errors") is { } errors) vm.ErrorCount = errors;
            if (Value(arguments, "--seed") is { } seed) vm.ErrorSeed = seed;
            if (Value(arguments, "--sync-interval") is { } interval) vm.SyncDamageInterval = interval;
            if (DoubleValue(arguments, "--drop-rate") is { } dropRate) vm.DropRatePercent = dropRate;
            if (Value(arguments, "--drop-seed") is { } dropSeed) vm.DropSeed = dropSeed;
            window = new AfsSenderWindow(vm); start = vm.StartCommand;
        }
        else if (arguments.ContainsKey("--receiver"))
        {
            var vm = CreateReceiverViewModel();
            if (Value(arguments, "--data-port") is { } data) vm.DataPort = data;
            if (Value(arguments, "--result-port") is { } result) vm.ResultPort = result;
            if (Value(arguments, "--repeat") is { } repeat) vm.RepeatCount = repeat;
            window = new AfsReceiverWindow(vm); start = vm.StartCommand;
        }
        else
        {
            var dashboard = new AfsDashboardViewModel(
                () => new AfsSenderWindow(CreateSenderViewModel()),
                () => new AfsReceiverWindow(CreateReceiverViewModel()));
            window = new AfsDashboardWindow(dashboard);
        }

        // 창이 표시된 다음 자동 시작해야 WPF 바인딩과 화면 수명까지 실제로 검증할 수 있다.
        MainWindow = window;
        window.Show();
        if (arguments.ContainsKey("--auto-start") && start is not null)
            Dispatcher.BeginInvoke(() => start.Execute(null));
    }

    private static IAfsSessionService CreateSessionService() =>
        new AfsSessionOrchestrator(
            new AfsFrameService(static () => new AfsNativeCodec()),
            new AfsTimeSynchronizer(),
            new AfsTestEvaluator(),
            new AfsResultWriter());

    private static GnssCaptureViewModel CreateGnssCaptureViewModel()
    {
        var protocols = new GnssProtocolAdapterCatalog();
        var capture = new GnssComCaptureService(
            new SerialPortGnssByteSourceFactory(),
            protocols);
        return new(
            capture,
            new SystemGnssSerialPortCatalog(),
            protocols);
    }

    private static int? Value(IReadOnlyDictionary<string, string> arguments, string name) =>
        arguments.TryGetValue(name, out var text) && int.TryParse(text, out var value) ? value : null;
    private static double? DoubleValue(IReadOnlyDictionary<string, string> arguments, string name) =>
        arguments.TryGetValue(name, out var text) && double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
}

