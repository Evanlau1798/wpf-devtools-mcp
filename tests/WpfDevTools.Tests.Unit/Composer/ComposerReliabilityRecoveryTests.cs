using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerReliabilityRecoveryTests
{
    [Fact]
    public void PackRegistry_ShouldSkipCorruptPackAndReturnDiagnostic()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            var packRoot = Path.Combine(ComposerPackPaths.ProjectLocalRoot(projectRoot), "broken", "1.0.0");
            Directory.CreateDirectory(packRoot);
            File.WriteAllText(
                Path.Combine(packRoot, "install.manifest.json"),
                """
                {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"broken","version":"1.0.0","scope":"project","path":".","enabled":true}
                """);
            File.WriteAllText(Path.Combine(packRoot, "pack.json"), "{");

            var registry = new PackRegistry(Path.Combine(tempRoot, "builtin"), ComposerPackPaths.ProjectLocalRoot(projectRoot));

            var result = registry.ListPacks();

            result.Packs.Should().BeEmpty();
            result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("could not be loaded", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ValidateBlueprint_ShouldWarnForMissingUnusedOptionalPack()
    {
        var validator = new BlueprintValidationService(PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath(".")));

        var result = validator.Validate("""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "OptionalMissing",
              "packs": [
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                { "id": "missing.optional", "version": "1.0.0", "required": false, "role": "optional" }
              ],
              "primaryPack": "wpfui",
              "layout": { "kind": "wpfui.button", "properties": { "text": "OK" } }
            }
            """);

        result.Success.Should().BeTrue();
        result.Warnings.Should().ContainSingle(issue => issue.Code == "OptionalPackMissing")
            .Which.JsonPath.Should().Be("$.packs[1]");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
