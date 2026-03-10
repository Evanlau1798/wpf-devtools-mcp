using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Integration;

 [Collection("WpfIntegration")]
public sealed class WpfApplicationFixtureResilienceTests
{
    private readonly WpfApplicationFixture _fixture;

    public WpfApplicationFixtureResilienceTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void RunOnUiThread_AfterCurrentMainWindowClosed_ShouldRecreateUsableRootWindow()
    {
        _fixture.RunOnUIThread(() =>
        {
            var transientWindow = new Window
            {
                Width = 320,
                Height = 200,
                Content = new TextBlock { Text = "Transient" }
            };

            Application.Current.MainWindow = transientWindow;
            transientWindow.Show();
            transientWindow.Close();
        });

        var result = _fixture.RunOnUIThread(() =>
        {
            var rootWindow = Application.Current.MainWindow;
            rootWindow.Should().NotBeNull();
            rootWindow!.Content = new StackPanel();

            return new
            {
                rootWindow.IsLoaded,
                HasContent = rootWindow.Content is StackPanel
            };
        });

        result.HasContent.Should().BeTrue();
    }
}
