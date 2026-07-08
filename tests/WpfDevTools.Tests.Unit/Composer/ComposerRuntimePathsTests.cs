using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using Xunit;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("ProcessEnvironment")]
public sealed class ComposerRuntimePathsTests
{
    private const string ComposerRootEnvVar = "WPFDEVTOOLS_COMPOSER_ROOT";

    [Fact]
    public void ResolveComposerRoot_WhenPackageManifestExists_ShouldNotClimbOutsidePackageRoot()
    {
        var previousCurrentDirectory = Directory.GetCurrentDirectory();
        var previousConfiguredRoot = Environment.GetEnvironmentVariable(ComposerRootEnvVar);
        var tempRoot = CreateTempDirectory();
        try
        {
            Environment.SetEnvironmentVariable(ComposerRootEnvVar, null);
            var packageRoot = Path.Combine(tempRoot, "install", "x64", "current");
            var binRoot = Path.Combine(packageRoot, "bin");
            Directory.CreateDirectory(binRoot);
            File.WriteAllText(Path.Combine(binRoot, "manifest.json"), "{}");
            Directory.SetCurrentDirectory(binRoot);

            var resolved = ComposerRuntimePaths.ResolveComposerRoot();

            resolved.Should().Be(Path.GetFullPath(packageRoot));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
            Environment.SetEnvironmentVariable(ComposerRootEnvVar, previousConfiguredRoot);
            DeleteDirectory(tempRoot);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-runtime-" + Guid.NewGuid().ToString("N"));
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
