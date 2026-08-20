using System.Windows;
namespace LnisAfsValidator.App;
/// <summary>프로그램 시작 시 표시되는 AFS 검증 대시보드 창이다.</summary>
public partial class AfsDashboardWindow : Window
{
    public AfsDashboardWindow(AfsDashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
