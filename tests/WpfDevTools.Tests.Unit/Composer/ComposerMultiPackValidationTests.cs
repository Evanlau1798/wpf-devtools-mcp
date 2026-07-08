using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerMultiPackValidationTests
{
    [Fact]
    public void ValidateBlueprint_ShouldReportDeclaredPackVersionConflict()
    {
        var result = CreateValidator().Validate("""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "VersionConflict",
              "packs": [
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                { "id": "wpfui", "version": "9.9.9", "required": false, "role": "optional-control" }
              ],
              "primaryPack": "wpfui",
              "layout": { "kind": "wpfui.button" }
            }
            """);

        result.Errors.Should().Contain(issue => issue.JsonPath == "$.packs[1].version"
            && issue.Code == "PackVersionConflict"
            && issue.Message.Contains("0.1.0", StringComparison.Ordinal)
            && issue.Message.Contains("9.9.9", StringComparison.Ordinal));
    }

    private static BlueprintValidationService CreateValidator()
        => new(PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath(".")));
}
