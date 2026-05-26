using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ProcessEnvironment")]
public sealed class SignaturePolicyLocalReleaseTests
{
    private const string LegacySkipSignatureCheckEnvironmentVariable = "WPFDEVTOOLS_SKIP_SIGNATURE_CHECK";
    private const string ReleaseOnlySignaturePolicyReason =
        "Packaged Release signature enforcement is verified only in Release builds because Debug builds intentionally skip DLL signatures.";

    [Fact]
    public void GetSignatureAction_ReleaseWorkspaceWithLegacyEnvironmentVariable_ShouldVerify()
    {
        var previousValue = Environment.GetEnvironmentVariable(LegacySkipSignatureCheckEnvironmentVariable);
        var baseDirectory = CreateWorkspaceBuildBaseDirectory(configuration: "Release");
        using var releasePolicyScope = UseReleaseBuildPolicy();

        try
        {
            Environment.SetEnvironmentVariable(LegacySkipSignatureCheckEnvironmentVariable, "1");

            var action = DllPathValidator.GetSignatureAction(baseDirectory);

            action.Should().Be(SignaturePolicy.Action.Verify,
                "the legacy process-wide skip environment variable must not affect the default production validator path");
        }
        finally
        {
            Environment.SetEnvironmentVariable(LegacySkipSignatureCheckEnvironmentVariable, previousValue);
            DeleteWorkspace(baseDirectory);
        }
    }

    [Fact]
    public void GetSignatureAction_ReleaseWorkspaceWithExplicitOverride_ShouldSkipWithoutMutatingEnvironment()
    {
        var previousValue = Environment.GetEnvironmentVariable(LegacySkipSignatureCheckEnvironmentVariable);
        var baseDirectory = CreateWorkspaceBuildBaseDirectory(configuration: "Release");
        using var releasePolicyScope = UseReleaseBuildPolicy();

        try
        {
            Environment.SetEnvironmentVariable(LegacySkipSignatureCheckEnvironmentVariable, null);

            var action = DllPathValidator.GetSignatureAction(
                baseDirectory,
                trustedLocalDevelopmentSkipOptIn: true);

            action.Should().Be(SignaturePolicy.Action.Skip);
            Environment.GetEnvironmentVariable(LegacySkipSignatureCheckEnvironmentVariable)
                .Should().BeNull("explicit policy override should not use process-wide environment state");
        }
        finally
        {
            Environment.SetEnvironmentVariable(LegacySkipSignatureCheckEnvironmentVariable, previousValue);
            DeleteWorkspace(baseDirectory);
        }
    }

    [Fact]
    public void GetSignatureAction_PackagedReleaseDirectoryWithExplicitOverride_ShouldStillVerify()
    {
        var previousValue = Environment.GetEnvironmentVariable(LegacySkipSignatureCheckEnvironmentVariable);
        var baseDirectory = CreatePackagedReleaseBaseDirectory();
        using var releasePolicyScope = UseReleaseBuildPolicy();

        try
        {
            Environment.SetEnvironmentVariable(LegacySkipSignatureCheckEnvironmentVariable, "1");

            var action = DllPathValidator.GetSignatureAction(
                baseDirectory,
                trustedLocalDevelopmentSkipOptIn: true);

            action.Should().Be(SignaturePolicy.Action.Verify,
                "the local skip opt-in must not relax signature verification outside a trusted repository build tree");
        }
        finally
        {
            Environment.SetEnvironmentVariable(LegacySkipSignatureCheckEnvironmentVariable, previousValue);
            Directory.Delete(GetTempRoot(baseDirectory), recursive: true);
        }
    }

    private static string CreateWorkspaceBuildBaseDirectory(string configuration)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var mainRoot = Path.Combine(root, "repo");
        var worktreeRoot = Path.Combine(mainRoot, ".worktrees", "feature-branch");
        var baseDirectory = Path.Combine(
            worktreeRoot,
            "src",
            "WpfDevTools.Mcp.Server",
            "bin",
            configuration,
            "net8.0-windows");

        Directory.CreateDirectory(baseDirectory);
        File.WriteAllText(Path.Combine(mainRoot, "WpfDevTools.sln"), string.Empty);
        File.WriteAllText(Path.Combine(worktreeRoot, "WpfDevTools.sln"), string.Empty);

        return baseDirectory;
    }

    private static string CreatePackagedReleaseBaseDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var baseDirectory = Path.Combine(root, "package", "bin");
        Directory.CreateDirectory(baseDirectory);
        return baseDirectory;
    }

    private static void DeleteWorkspace(string baseDirectory)
        => Directory.Delete(GetTempRoot(baseDirectory), recursive: true);

    private static IDisposable UseReleaseBuildPolicy()
    {
        var previousValue = DllPathValidator.DebugBuildOverrideForTesting;
        DllPathValidator.DebugBuildOverrideForTesting = false;
        return new RestoreAction(() => DllPathValidator.DebugBuildOverrideForTesting = previousValue);
    }

    private static string GetTempRoot(string baseDirectory)
    {
        var directory = new DirectoryInfo(baseDirectory);
        while (directory.Parent is { } parent
            && !string.Equals(parent.FullName, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            directory = parent;
        }

        return directory.FullName;
    }

    private sealed class RestoreAction(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
