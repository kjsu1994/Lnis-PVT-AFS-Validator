using LnisAfsValidator.App;

namespace LnisAfsValidator.Tests;

public sealed class AppWorkspacePathsTests
{
    [Fact]
    public void DefaultArtifactRootsUseDocumentsWorkspace()
    {
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LNIS AFS Validator");

        Assert.Equal(Path.Combine(expectedRoot, "Runs"), AppWorkspacePaths.DefaultRunsRoot);
        Assert.Equal(Path.Combine(expectedRoot, "Captures"), AppWorkspacePaths.DefaultCapturesRoot);
    }

    [Fact]
    public void LegacyCaptureDefaultMigratesButCustomPathIsPreserved()
    {
        var legacyDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LnisAfsValidator",
            "Captures");
        var customPath = Path.Combine(Path.GetTempPath(), "LNIS-Custom-Captures");

        Assert.Equal(AppWorkspacePaths.DefaultCapturesRoot, AppWorkspacePaths.ResolveCapturesRoot(legacyDefault));
        Assert.Equal(customPath, AppWorkspacePaths.ResolveCapturesRoot(customPath));
    }
}
