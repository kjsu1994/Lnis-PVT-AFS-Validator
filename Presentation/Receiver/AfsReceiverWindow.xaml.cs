using System.Windows;
namespace LnisAfsValidator.App;
/// <summary>UDP 대기와 AFS 복호·RAW 복원을 표시하는 수신부 전용 창이다.</summary>
public partial class AfsReceiverWindow : Window
{
    public AfsReceiverWindow(AfsReceiverViewModel? viewModel = null) { InitializeComponent(); DataContext = viewModel ?? new AfsReceiverViewModel(); }
}
