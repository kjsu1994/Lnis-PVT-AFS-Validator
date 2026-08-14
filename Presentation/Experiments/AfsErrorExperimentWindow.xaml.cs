using System.Windows;

namespace LnisAfsValidator.App;

/// <summary>AFS 코덱 오류정정·재동기 실험 전용 창이다.</summary>
public partial class AfsErrorExperimentWindow : Window
{
    public AfsErrorExperimentWindow(AfsErrorExperimentViewModel? viewModel = null) { InitializeComponent(); DataContext = viewModel ?? new AfsErrorExperimentViewModel(); }
}
