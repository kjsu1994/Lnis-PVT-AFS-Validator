using System.Windows;
namespace LnisAfsValidator.App;
/// <summary>Test A~E 조건을 선택하여 AFS 프레임을 전송하는 송신부 전용 창이다.</summary>
public partial class AfsSenderWindow : Window
{
    public AfsSenderWindow(AfsSenderViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
