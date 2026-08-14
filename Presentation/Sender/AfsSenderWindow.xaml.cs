using System.Windows;
namespace LnisAfsValidator.App;
/// <summary>Test A 정상 송신과 Test E Frame Drop을 실행하는 송신부 전용 창이다.</summary>
public partial class AfsSenderWindow : Window
{
    public AfsSenderWindow(AfsSenderViewModel? viewModel = null) { InitializeComponent(); DataContext = viewModel ?? new AfsSenderViewModel(); }
}
