using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewCompileTests
{
    [Theory]
    [MemberData(nameof(CompilableBlueprints))]
    public void PreviewBlueprint_ShouldCompileGoldenBlueprint(string blueprintJson, string expectedConclusion)
    {
        var service = new UiBlueprintPreviewService(CreateRegistry());

        var result = service.Preview(new PreviewBlueprintRequest(blueprintJson, RestoreEnabled: true));

        result.Success.Should().BeTrue();
        result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
        result.RestoreEnabled.Should().BeTrue();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == expectedConclusion);
        result.PreviewHost.Status.Should().Be("compiled");
    }

    [Fact]
    public void PreviewBlueprint_ShouldCaptureRestoreDisabledBuildFailure()
    {
        var service = new UiBlueprintPreviewService(CreateRegistry());

        var result = service.Preview(new PreviewBlueprintRequest(ButtonBlueprint(), RestoreEnabled: false));

        result.Success.Should().BeTrue();
        result.BuildSucceeded.Should().BeFalse();
        result.RestoreEnabled.Should().BeFalse();
        result.BuildOutput.Should().Contain("project.assets.json");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "XamlCompileFailed"
            && diagnostic.JsonPath == "$.layout"
            && diagnostic.RendererTemplatePath.EndsWith("button.xaml.sbn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreviewUiBlueprintTool_ShouldReturnStructuredCompileResult()
    {
        var result = await UiComposerMcpTools.PreviewUiBlueprint(
            ButtonBlueprint(),
            restoreEnabled: true,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("buildSucceeded").GetBoolean().Should().BeTrue();
        payload.GetProperty("previewHost").GetProperty("status").GetString().Should().Be("compiled");
    }

    public static TheoryData<string, string> CompilableBlueprints()
        => new()
        {
            { ButtonBlueprint(), "ButtonIconPropertyElementValid" },
            { NavigationShellBlueprint(), "PreviewXamlCompiled" },
            { DataGridBlueprint(), "DataGridColumnsPropertyElementValid" },
            { DashboardCardBlueprint(), "PreviewXamlCompiled" },
            { ContentDialogBlueprint(), "PreviewXamlCompiled" },
            { SnackbarBlueprint(), "PreviewXamlCompiled" },
            { TabbedSettingsBlueprint(), "PreviewXamlCompiled" }
        };

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static string ButtonBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.button",
              "properties": { "text": "Save" },
              "slots": { "icon": [{ "kind": "wpfui.symbolIcon", "properties": { "symbol": "Save24" } }] }
            }
            """);

    private static string DataGridBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.dataGrid",
              "properties": { "itemsSource": "{Binding Rows}" },
              "slots": { "columns": [{ "kind": "template" }] }
            }
            """);

    private static string NavigationShellBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.fluentWindow",
              "properties": { "title": "Composer" },
              "slots": {
                "titleBar": [{ "kind": "wpfui.titleBar", "properties": { "title": "Composer" } }],
                "content": [{
                  "kind": "wpfui.navigationView",
                  "slots": {
                    "items": [{
                      "kind": "wpfui.navigationViewItem",
                      "slots": {
                        "content": [{ "kind": "text", "properties": { "value": "Home" } }],
                        "icon": [{ "kind": "wpfui.symbolIcon", "properties": { "symbol": "Home24" } }]
                      }
                    }],
                    "content": [{ "kind": "wpfui.card" }]
                  }
                }]
              }
            }
            """);

    private static string DashboardCardBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.card",
              "slots": {
                "header": [{ "kind": "text", "properties": { "value": "Dashboard" } }],
                "content": [{ "kind": "text", "properties": { "value": "Compiled preview" } }],
                "actions": [{ "kind": "wpfui.button", "properties": { "text": "Refresh" } }]
              }
            }
            """);

    private static string ContentDialogBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.contentDialog",
              "properties": { "title": "Confirm" },
              "slots": {
                "content": [{ "kind": "text", "properties": { "value": "Continue?" } }],
                "actions": [{ "kind": "wpfui.button", "properties": { "text": "Continue" } }]
              }
            }
            """);

    private static string SnackbarBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.snackbar",
              "properties": { "timeout": 4000 },
              "slots": {
                "content": [{ "kind": "text", "properties": { "value": "Saved" } }],
                "actions": [{ "kind": "wpfui.button", "properties": { "text": "Undo" } }]
              }
            }
            """);

    private static string TabbedSettingsBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.tabView",
              "slots": {
                "items": [
                  {
                    "kind": "wpfui.tabViewItem",
                    "slots": {
                      "header": [{ "kind": "text", "properties": { "value": "General" } }],
                      "content": [{ "kind": "wpfui.card" }]
                    }
                  },
                  {
                    "kind": "wpfui.tabViewItem",
                    "slots": {
                      "header": [{ "kind": "text", "properties": { "value": "Security" } }],
                      "content": [{ "kind": "wpfui.card" }]
                    }
                  }
                ]
              }
            }
            """);

    private static string Blueprint(string layoutJson)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "PreviewView",
              "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true }],
              "primaryPack": "wpfui",
              "layout": {{layoutJson}}
            }
            """;
}
