using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewProjectFilesTests
{
    [Theory]
    [InlineData("  <Window><Grid /></Window>", 1)]
    [InlineData("<?xml version=\"1.0\"?><Window><Grid /></Window>", 1)]
    [InlineData("<!-- preview --><Window><Grid /></Window>", 1)]
    [InlineData("<!-- <placeholder /> --><Window><Grid /></Window>", 1)]
    [InlineData("<!DOCTYPE Window [<!ENTITY sample \"<placeholder />\">]><Window><Grid /></Window>", 1)]
    [InlineData("<Window.Foo><Grid /></Window.Foo>", 2)]
    public void Write_ShouldRecognizeOnlyNativeWindowDocumentRoots(string generatedXaml, int expectedWindowStarts)
    {
        var xaml = WritePreviewXaml(generatedXaml);

        xaml.Split("<Window", StringSplitOptions.None).Should().HaveCount(expectedWindowStarts + 1);
        xaml.Split("x:Class=", StringSplitOptions.None).Should().HaveCount(2);
    }

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

    private static string WritePreviewXaml(string generatedXaml)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-preview-project-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            UiPreviewProjectFiles.Write(
                tempRoot,
                generatedXaml,
                includeRuntimeDiagnostics: false,
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
            return File.ReadAllText(Path.Combine(tempRoot, "MainWindow.xaml"));
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
