using System.Windows;

namespace LnisAfsValidator.App;

/// <summary>메인 화면의 입력 파일 경로를 이어받아 오류 실험 ViewModel을 표시한다.</summary>
public partial class AfsErrorExperimentWindow : Window
{
    public AfsErrorExperimentWindow(string? capturePath = null, string? almanacPath = null)
    {
        InitializeComponent();
        DataContext = new AfsErrorExperimentViewModel(capturePath, almanacPath);
    }
}
