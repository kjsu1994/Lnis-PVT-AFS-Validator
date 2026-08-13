using System.Windows;

namespace LnisAfsValidator.App;

public partial class AfsErrorExperimentWindow : Window
{
    public AfsErrorExperimentWindow(string? capturePath = null, string? almanacPath = null)
    {
        InitializeComponent();
        DataContext = new AfsErrorExperimentViewModel(capturePath, almanacPath);
    }
}
