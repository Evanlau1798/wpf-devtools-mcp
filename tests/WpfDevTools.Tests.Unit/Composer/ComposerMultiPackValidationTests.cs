using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerMultiPackValidationTests
{
    private const string WpfUiResourceDictionary = "pack://application:,,,/Wpf.Ui;component/Resources/Wpf.Ui.xaml";

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

    [Fact]
    public void ValidateBlueprint_ShouldWarnForPackResourceConflicts()
    {
        var projectRoot = CreateTempProjectWithResourcePack("conflict.resources");
        try
        {
            var result = CreateValidator(projectRoot).Validate("""
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "ResourceConflict",
                  "packs": [
                    { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                    { "id": "conflict.resources", "version": "1.0.0", "required": false, "role": "optional-control" }
                  ],
                  "primaryPack": "wpfui",
                  "layout": { "kind": "wpfui.button" }
                }
                """);

            result.Errors.Should().BeEmpty();
            result.Warnings.Should().Contain(issue => issue.JsonPath == "$.packs"
                && issue.Code == "PackResourceConflict"
                && issue.Message.Contains(WpfUiResourceDictionary, StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    private static BlueprintValidationService CreateValidator(string? projectRoot = null)
    {
        var repoRoot = TestRepositoryPaths.GetRepoFilePath(".");
        var projectPackRoot = projectRoot is null ? null : ComposerPackPaths.ProjectLocalRoot(projectRoot);
        return new(new PackRegistry(ComposerPackPaths.BuiltinRoot(repoRoot), projectPackRoot));
    }

    private static string CreateTempProjectWithResourcePack(string id)
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-pack-conflict-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", id, "1.0.0");
        Directory.CreateDirectory(packRoot);
        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"), $$"""
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"{{id}}","version":"1.0.0","scope":"project-local","path":"{{id}}/1.0.0","enabled":true}
            """);
        File.WriteAllText(Path.Combine(packRoot, "pack.json"), $$"""
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"{{id}}","version":"1.0.0","displayName":"Conflict Resources","resourceSetup":{"applicationMergedDictionaries":["{{WpfUiResourceDictionary}}"]},"blocks":[],"recipes":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"), """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[]}
            """);
        return projectRoot;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
