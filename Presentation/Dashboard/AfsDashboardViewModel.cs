using System.Windows;
using System.Windows.Input;

namespace LnisAfsValidator.App;

/// <summary>송신부·수신부·코덱 실험 창만 여는 시작 화면 모델이다.</summary>
public sealed class AfsDashboardViewModel
{
    public ICommand OpenSenderCommand { get; }
    public ICommand OpenReceiverCommand { get; }
    public ICommand OpenExperimentCommand { get; }

    public AfsDashboardViewModel()
    {
        OpenSenderCommand = new RelayCommand(() => Show(new AfsSenderWindow()));
        OpenReceiverCommand = new RelayCommand(() => Show(new AfsReceiverWindow()));
        OpenExperimentCommand = new RelayCommand(() => Show(new AfsErrorExperimentWindow()));
    }

    private static void Show(Window window)
    {
        window.Owner = Application.Current.MainWindow;
        window.Show();
    }
}
