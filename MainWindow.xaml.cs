using System.Windows;
namespace LnisAfsValidator.App;
public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); DataContext = new MainViewModel(); }
    private void OpenGnssCapture_Click(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) new GnssCaptureWindow(vm.GnssCapture) { Owner = this }.Show(); }
}
