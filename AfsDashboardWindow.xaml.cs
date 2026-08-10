using System.Windows;
namespace LnisAfsValidator.App;
public partial class AfsDashboardWindow : Window
{
    public AfsDashboardWindow() { InitializeComponent(); DataContext = new AfsMainViewModel(); }
}
