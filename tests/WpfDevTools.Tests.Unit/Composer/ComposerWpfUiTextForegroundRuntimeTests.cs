using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using WpfUiFluentWindow = Wpf.Ui.Controls.FluentWindow;
using WpfUiTextBlock = Wpf.Ui.Controls.TextBlock;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("WPF")]
public sealed class ComposerWpfUiTextForegroundRuntimeTests
{
    [Fact]
    public void ExplicitForeground_ShouldSurvivePreloadedWpfUiThemeResources()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                VerifyExplicitForeground();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure.Should().BeNull();
    }

    private static void VerifyExplicitForeground()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
        application.Resources.MergedDictionaries.Add(new ControlsDictionary());

        var registry = PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));
        var render = new UiBlueprintRenderer(registry).Render(new RenderBlueprintRequest("""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "ForegroundPrecedence",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
              ],
              "primaryPack": "wpfui",
              "resourceVariants": { "wpfui": "light" },
              "layout": {
                "kind": "wpfui.fluentWindow",
                "slots": { "content": [{
                  "kind": "wpfui.textBlock",
                  "elementName": "ExplicitText",
                  "properties": {
                    "text": "Readable",
                    "appearance": "Primary",
                    "foreground": "#FFFFFFFF"
                  }
                }] }
              }
            }
            """));

        render.Success.Should().BeTrue(string.Join(Environment.NewLine, render.Errors.Select(error => error.Message)));
        var window = (WpfUiFluentWindow)XamlReader.Parse(render.Xaml);
        try
        {
            window.Show();
            window.UpdateLayout();
            var text = window.FindName("ExplicitText").Should().BeOfType<WpfUiTextBlock>().Subject;

            text.Foreground.Should().BeOfType<SolidColorBrush>()
                .Which.Color.Should().Be(Colors.White);
        }
        finally
        {
            window.Close();
            application.Shutdown();
        }
    }
}
