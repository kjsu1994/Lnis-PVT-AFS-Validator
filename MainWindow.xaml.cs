using System.Windows;
namespace LnisAfsValidator.App;
public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); DataContext = new MainViewModel(); }
}
