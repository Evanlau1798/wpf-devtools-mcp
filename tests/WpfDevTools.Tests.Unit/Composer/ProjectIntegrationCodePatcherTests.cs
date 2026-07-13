using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ProjectIntegrationCodePatcherTests
{
    [Fact]
    public void Patch_ShouldReplaceOnlyBaseTypeAndPreserveInterfaces()
    {
        var result = Patch("public partial class MainWindow : LegacyShell<string, int>, IDisposable, INotifyPropertyChanged { }");

        result.Success.Should().BeTrue(result.Error?.Message);
        result.Content.Should().Contain(
            "partial class MainWindow : Nebula.Controls.Shell, IDisposable, INotifyPropertyChanged");
    }

    [Fact]
    public void Patch_ShouldAddBaseTypeWhenInheritanceClauseIsAbsent()
    {
        var result = Patch("public partial class MainWindow { }");

        result.Success.Should().BeTrue(result.Error?.Message);
        result.Content.Should().Contain("partial class MainWindow : Nebula.Controls.Shell {");
    }

    [Fact]
    public void Patch_ShouldRejectAmbiguousPartialClassDeclarations()
    {
        var result = Patch(
            "public partial class MainWindow : System.Windows.Window { }\n"
            + "public partial class MainWindow : System.Windows.Window { }");

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("CodeBehindClassAmbiguous");
    }

    [Fact]
    public void Patch_ShouldRejectMalformedInheritanceClause()
    {
        var result = Patch("public partial class MainWindow : System.Windows.Window, { }");

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("CodeBehindInheritanceInvalid");
    }

    private static ProjectContentPatchResult Patch(string source)
    {
        var root = TestDirectory.Create();
        try
        {
            var path = Path.Combine(root, "MainWindow.xaml.cs");
            File.WriteAllText(path, source);
            return ProjectIntegrationCodePatcher.Patch(
                path,
                "NebulaApp",
                "MainWindow",
                "Nebula.Controls.Shell");
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }
}
