using System.Windows;

namespace LnisAfsValidator.App;

/// <summary>전달받은 GNSS 캡처 ViewModel을 표시하는 전용 창이다.</summary>
public partial class GnssCaptureWindow : Window
{
    public GnssCaptureWindow(GnssCaptureViewModel viewModel) { InitializeComponent(); DataContext = viewModel; }
}
