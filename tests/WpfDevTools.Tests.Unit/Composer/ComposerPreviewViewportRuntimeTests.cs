using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Preview;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed partial class ComposerPreviewCompileTests
{
    [Fact]
    [Trait("Category", "ComposerRuntime")]
    public async Task PreviewBlueprintAsync_WithTargetViewport_ShouldReportWindowClientOverflow()
    {
        using var sensitiveReads = new EnvironmentVariableScope(
            McpServerConfiguration.AllowSensitiveReadsEnvVar,
            "true");
        using var session = SecurePreviewSession.Create();
        using var timeout = CreateTimeout();

        var result = await new UiBlueprintPreviewService(CreateRegistry(), session.SessionManager)
            .PreviewAsync(
                new PreviewBlueprintRequest(
                    CoreViewportOverflowBlueprint(),
                    RestoreEnabled: true,
                    StartHost: true,
                    IncludeRuntimeDiagnostics: true,
                    ViewportWidth: 320,
                    ViewportHeight: 200),
                timeout.Token);

        result.BuildSucceeded.Should().BeTrue(
            "build output: {0}; diagnostics: {1}",
            result.BuildOutput,
            string.Join(" | ", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        result.PreviewHost.Status.Should().Be("loaded", result.BuildOutput);
        result.LayoutRiskSummary.ClippedElementCount.Should().BeGreaterThan(0);
        result.LayoutRiskSummary.Warnings.Should().Contain(warning =>
            warning.SuggestedFix != null
            && warning.SuggestedFix.Contains("Window client viewport", StringComparison.Ordinal));
    }

    private static string CoreViewportOverflowBlueprint()
        => """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "ViewportRegression",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "primary" }
              ],
              "primaryPack": "core",
              "layout": {
                "kind": "core.stack",
                "slots": {
                  "children": [
                    {
                      "kind": "core.border",
                      "elementName": "ViewportSectionOne",
                      "properties": { "minHeight": 160 },
                      "slots": { "content": [{ "kind": "core.text", "properties": { "text": "One" } }] }
                    },
                    {
                      "kind": "core.border",
                      "elementName": "ViewportSectionTwo",
                      "properties": { "minHeight": 160 },
                      "slots": { "content": [{ "kind": "core.text", "properties": { "text": "Two" } }] }
                    },
                    {
                      "kind": "core.border",
                      "elementName": "ViewportSectionThree",
                      "properties": { "minHeight": 160 },
                      "slots": { "content": [{ "kind": "core.text", "properties": { "text": "Three" } }] }
                    }
                  ]
                }
              }
            }
            """;
}
