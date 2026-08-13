using System.Windows;
namespace LnisAfsValidator.App;
/// <summary>기존 IQ 종단 간 검증 화면과 GNSS 캡처 창 진입을 제공한다.</summary>
public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); DataContext = new MainViewModel(); }
    // 메인 ViewModel의 캡처 상태를 별도 창과 공유하여 창을 다시 열어도 설정을 유지한다.
    private void OpenGnssCapture_Click(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) new GnssCaptureWindow(vm.GnssCapture) { Owner = this }.Show(); }
}
