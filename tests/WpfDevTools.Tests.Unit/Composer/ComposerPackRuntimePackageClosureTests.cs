using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPackRuntimePackageClosureTests
{
    [Fact]
    public void Load_ShouldRejectExternalVariantResourcesWithoutRuntimePackages()
    {
        var root = Path.Combine(Path.GetTempPath(), "wpfdevtools-pack-closure-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(root, "sample", "1.0.0");
        try
        {
            Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
            Directory.CreateDirectory(Path.Combine(packRoot, "recipes"));
            Directory.CreateDirectory(Path.Combine(packRoot, "examples"));
            File.WriteAllText(
                Path.Combine(packRoot, "pack.json"),
                """
                {
                  "schemaVersion": "wpfdevtools.ui-pack.v1",
                  "id": "sample",
                  "displayName": "Sample",
                  "version": "1.0.0",
                  "nugetPackages": [],
                  "resourceSetup": {
                    "defaultVariant": "light",
                    "variants": {
                      "light": {
                        "appearance": "light",
                        "applicationMergedDictionaries": [
                          "pack://application:,,,/Example.Controls;component/Themes/Light.xaml"
                        ]
                      }
                    }
                  },
                  "blocks": [],
                  "recipes": []
                }
                """);
            File.WriteAllText(
                Path.Combine(packRoot, "source.lock.json"),
                """
                {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
                """);

            var action = () => ComposerPackLoader.LoadUncachedForValidation(packRoot);

            action.Should().Throw<InvalidDataException>()
                .WithMessage("*MissingRuntimePackageClosure*sample*");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
