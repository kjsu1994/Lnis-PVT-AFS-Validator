using System.Windows;

namespace LnisAfsValidator.App;

public partial class GnssCaptureWindow : Window
{
    public GnssCaptureWindow(GnssCaptureViewModel viewModel) { InitializeComponent(); DataContext = viewModel; }
}
