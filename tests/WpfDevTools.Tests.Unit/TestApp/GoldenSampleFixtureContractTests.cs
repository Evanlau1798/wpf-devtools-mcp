using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.TestApp;

public sealed class GoldenSampleFixtureContractTests
{
    [Fact]
    public void MainWindowXaml_ShouldExposeStableNamedAnchorsForE2eWorkflows()
    {
        var content = File.ReadAllText(GetRepoFilePath("tests/WpfDevTools.Tests.TestApp/MainWindow.xaml"));

        var requiredAnchors = new[]
        {
            "MainTabControl",
            "BasicControlsTab",
            "StylesTemplatesTab",
            "PerformanceTab",
            "BaseStyleButton",
            "PrimaryStyleButton",
            "PrimaryDisabledButton",
            "RoundTemplateButton",
            "ClippingBorderSample",
            "ClippingTextSample",
            "RotatedButtonSample",
            "ScaledButtonSample",
            "LayoutRotatedButtonSample",
            "PerformanceBottomSentinel",
            "FocusStartTextBox",
            "FocusNextTextBox",
            "FocusActionButton",
            "FocusStatusTextBlock"
        };

        foreach (var anchor in requiredAnchors)
        {
            content.Should().Contain(anchor, $"Golden sample should expose stable E2E anchor '{anchor}'");
        }
    }

    [Fact]
    public void ModernShellWindowXaml_ShouldExposeFocusableNamedAnchor()
    {
        var content = File.ReadAllText(GetRepoFilePath("tests/WpfDevTools.Tests.TestApp/ModernShellWindow.xaml"));

        content.Should().Contain("ModernShellInputTextBox");
        content.Should().Contain("ModernPrimaryActionButton");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
