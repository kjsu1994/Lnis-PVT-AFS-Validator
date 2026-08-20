using System.Windows;
using System.Windows.Input;

namespace LnisAfsValidator.App;

/// <summary>각 PC에서 사용할 송신부 또는 수신부 창을 독립적으로 여는 시작 화면 모델이다.</summary>
public sealed class AfsDashboardViewModel
{
    public ICommand OpenSenderCommand { get; }
    public ICommand OpenReceiverCommand { get; }

    public AfsDashboardViewModel(
        Func<AfsSenderWindow> senderWindowFactory,
        Func<AfsReceiverWindow> receiverWindowFactory)
    {
        OpenSenderCommand = new RelayCommand(() => Show(senderWindowFactory()));
        OpenReceiverCommand = new RelayCommand(() => Show(receiverWindowFactory()));
    }

    private static void Show(Window window)
    {
        window.Owner = Application.Current.MainWindow;
        window.Show();
    }
}
