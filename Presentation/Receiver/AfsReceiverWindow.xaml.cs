using System.ComponentModel;
using System.Windows;
namespace LnisAfsValidator.App;
/// <summary>UDP 대기와 AFS 복호·RAW 복원을 표시하는 수신부 전용 창이다.</summary>
public partial class AfsReceiverWindow : Window
{
    public AfsReceiverWindow(AfsReceiverViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += StartReceiving;
        Closing += StopReceiving;
    }

    /// <summary>수신부 창이 표시되면 저장된 포트 설정으로 즉시 UDP 수신을 시작한다.</summary>
    private void StartReceiving(object sender, RoutedEventArgs e)
    {
        if (DataContext is AfsReceiverViewModel viewModel && viewModel.StartCommand.CanExecute(null))
            viewModel.StartCommand.Execute(null);
    }

    /// <summary>창을 닫을 때 대기 중인 UDP 작업도 취소하여 포트를 즉시 반환한다.</summary>
    private void StopReceiving(object? sender, CancelEventArgs e)
    {
        if (DataContext is AfsReceiverViewModel viewModel && viewModel.CancelCommand.CanExecute(null))
            viewModel.CancelCommand.Execute(null);
    }
}
