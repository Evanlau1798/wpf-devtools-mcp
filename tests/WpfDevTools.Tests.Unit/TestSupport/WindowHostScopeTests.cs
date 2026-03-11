using System.Windows;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.TestSupport;

public sealed class WindowHostScopeTests
{
    [StaFact]
    public void Create_WhenApplicationHasNoMainWindow_ShouldInstallFreshMainWindow()
    {
        if (Application.Current != null)
        {
            Application.Current.MainWindow = null;
        }

        using var scope = WindowHostScope.Create();

        scope.Window.Should().NotBeNull();
        if (Application.Current != null)
        {
            Application.Current.MainWindow.Should().BeSameAs(scope.Window);
        }
    }

    [StaFact]
    public void Dispose_WhenScopeOwnsMainWindow_ShouldCloseWindowAndClearMainWindowReference()
    {
        if (Application.Current != null)
        {
            Application.Current.MainWindow = null;
        }

        Window? window;
        using (var scope = WindowHostScope.Create())
        {
            window = scope.Window;
            window.Show();
            window.IsVisible.Should().BeTrue();
        }

        window.Should().NotBeNull();
        window!.IsVisible.Should().BeFalse();
        if (Application.Current != null)
        {
            Application.Current.MainWindow.Should().BeNull();
        }
    }
}
