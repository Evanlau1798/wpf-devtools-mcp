using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewProjectFilesTests
{
    [Fact]
    public void Write_WhenThirdPartyWindowUsesPropertyElements_ShouldOnlyDecorateRootOpeningTag()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-preview-project-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            UiPreviewProjectFiles.Write(
                tempRoot,
                "<sample:Shell><sample:Shell.Content><Grid /></sample:Shell.Content></sample:Shell>",
                includeRuntimeDiagnostics: false,
                loadedSentinelFileName: "loaded.txt",
                sdkOptionsFileName: "sdk.txt",
                sdkReadyFileName: "ready.txt",
                previewContract: new PreviewContractGenerationResult(
                    true,
                    string.Empty,
                    new Dictionary<string, string> { ["sample"] = "Sample.Controls" },
                    "sample:Shell",
                    "Sample.Controls.Shell",
                    []));

            var xaml = File.ReadAllText(Path.Combine(tempRoot, "MainWindow.xaml"));
            xaml.Should().Contain("<sample:Shell.Content>");
            xaml.Split("x:Class=", StringSplitOptions.None).Should().HaveCount(2);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Write_WhenRuntimeDiagnosticsRequested_ShouldPreferPackagedSdkReferences()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-preview-project-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            UiPreviewProjectFiles.Write(
                tempRoot,
                "<Grid />",
                includeRuntimeDiagnostics: true,
                loadedSentinelFileName: "loaded.txt",
                sdkOptionsFileName: "sdk.txt",
                sdkReadyFileName: "ready.txt",
                previewContract: new PreviewContractGenerationResult(
                    true,
                    string.Empty,
                    new Dictionary<string, string>(),
                    null,
                    null,
                    []));

            var project = File.ReadAllText(Path.Combine(tempRoot, "PreviewHost.csproj"));
            project.Should().Contain("<Reference Include=\"WpfDevTools.Inspector.Sdk\">");
            project.Should().NotContain("<ProjectReference");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
