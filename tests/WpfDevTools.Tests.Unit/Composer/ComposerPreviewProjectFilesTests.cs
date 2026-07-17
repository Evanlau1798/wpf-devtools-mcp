using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewProjectFilesTests
{
    [Fact]
    public void Write_WithExplicitViewport_ShouldConstrainWrapperWindow()
    {
        var xaml = WritePreviewXaml("<Grid />", viewportWidth: 800, viewportHeight: 450);

        xaml.Should().Contain("Width=\"800\"");
        xaml.Should().Contain("Height=\"450\"");
    }

    [Fact]
    public void Write_WithExplicitViewport_ShouldOverrideWindowRootDimensions()
    {
        var xaml = WritePreviewXaml(
            "<Window Width=\"1024\" Height=\"768\"><Grid /></Window>",
            viewportWidth: 800,
            viewportHeight: 450);

        xaml.Should().Contain("Width=\"800\"");
        xaml.Should().Contain("Height=\"450\"");
        xaml.Should().NotContain("Width=\"1024\"");
        xaml.Should().NotContain("Height=\"768\"");
    }

    [Fact]
    public void Write_WithMarkupExtensionWidthArgument_ShouldPreserveQuotedValue()
    {
        var xaml = WritePreviewXaml(
            "<Window Tag=\"{Binding Width='5'}\"><Grid /></Window>",
            viewportWidth: 800);

        xaml.Should().Contain("Tag=\"{Binding Width='5'}\"");
        xaml.Should().Contain(" Width=\"800\"");
    }

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

    [Fact]
    public void Write_ShouldSignalLoadedOnlyAfterTwoCompositorFrames()
    {
        var codeBehind = WritePreviewCodeBehind("<Grid />");

        codeBehind.Should().Contain("ContentRendered += OnContentRendered;");
        codeBehind.Should().Contain("CompositionTarget.Rendering += OnRendering;");
        codeBehind.Should().Contain("RequiredRenderedFrames = 2");
        codeBehind.Should().Contain("DispatcherPriority.ContextIdle");
        codeBehind.IndexOf("CompositionTarget.Rendering += OnRendering;", StringComparison.Ordinal).Should()
            .BeLessThan(codeBehind.IndexOf("File.WriteAllText", StringComparison.Ordinal));
    }

    private static string WritePreviewXaml(
        string generatedXaml,
        int? viewportWidth = null,
        int? viewportHeight = null)
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
                    []),
                viewportWidth: viewportWidth,
                viewportHeight: viewportHeight);
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

    private static string WritePreviewCodeBehind(string generatedXaml)
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
            return File.ReadAllText(Path.Combine(tempRoot, "MainWindow.xaml.cs"));
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
