using System.Security.Cryptography.X509Certificates;
using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Tests.Unit.Execution;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ProcessEnvironment")]
public class SignaturePolicyTests
{
    // === Policy decision tests (pure logic, no file system) ===
    // Policy contract: trusted-root-only model
    //   - Release builds ALWAYS verify signatures
    //   - Debug builds skip verification (path already validated as trusted root)

    [Fact]
    public void Evaluate_ReleaseBuild_ShouldAlwaysVerify()
    {
        var result = SignaturePolicy.Evaluate(isDebugBuild: false);

        result.Should().Be(SignaturePolicy.Action.Verify,
            "RELEASE builds must always verify signatures");
    }

    [Fact]
    public void Evaluate_DebugBuild_ShouldSkip()
    {
        var result = SignaturePolicy.Evaluate(isDebugBuild: true);

        result.Should().Be(SignaturePolicy.Action.Skip,
            "DEBUG builds skip verification (path already validated as trusted root)");
    }

    [Fact]
    public void Evaluate_TrustedLocalDevelopmentBuild_ShouldSkip()
    {
        var result = SignaturePolicy.Evaluate(
            isDebugBuild: false,
            isTrustedLocalDevelopmentBuild: true);

        result.Should().Be(SignaturePolicy.Action.Skip,
            "trusted non-Release workspace builds should remain usable for local development");
    }

    // === Revocation mode tests ===
    // Contract: Debug uses Offline (no network blocking), Release uses Online (max security)

    [Fact]
    public void GetRevocationMode_DebugBuild_ShouldReturnOffline()
    {
        var mode = SignaturePolicy.GetRevocationMode(isDebugBuild: true);

        mode.Should().Be(X509RevocationMode.Offline,
            "DEBUG builds must use Offline revocation to prevent network blocking during development");
    }

    [Fact]
    public void GetRevocationMode_ReleaseBuild_ShouldReturnOnline()
    {
        var mode = SignaturePolicy.GetRevocationMode(isDebugBuild: false);

        mode.Should().Be(X509RevocationMode.Online,
            "RELEASE builds must use Online revocation for maximum security");
    }

    [Fact]
    public void GetRevocationMode_TrustedLocalDevelopmentBuild_ShouldReturnOffline()
    {
        var mode = SignaturePolicy.GetRevocationMode(
            isDebugBuild: false,
            isTrustedLocalDevelopmentBuild: true);

        mode.Should().Be(X509RevocationMode.Offline,
            "trusted local workspace builds should avoid production-style signature network checks");
    }


    [Fact]
    public void GetCurrentBuildRevocationMode_ShouldMatchBuildConfiguration()
    {
        var mode = DllPathValidator.GetCurrentBuildRevocationMode();
        var action = DllPathValidator.GetCurrentBuildSignatureAction();

        var expected = action == SignaturePolicy.Action.Skip
            ? X509RevocationMode.Offline
            : X509RevocationMode.Online;

        mode.Should().Be(expected,
            "the current build's revocation policy must stay aligned with the effective signature verification action");
    }
    // === Integration: ConnectTool path validation ===

    [Fact]
    public void ValidateDllPath_DebugBuild_TrustedRoot_ShouldNotThrow()
    {
        var trustedDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");
        var act = () => DllPathValidator.ValidateDllPath(trustedDllPath);
        var signatureAction = DllPathValidator.GetCurrentBuildSignatureAction();

        if (signatureAction == SignaturePolicy.Action.Skip)
        {
            act.Should().NotThrow(
                "development builds should skip signature verification for trusted root DLLs");
        }
        else
        {
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*signature*");
        }
    }

    [Fact]
    public void ValidateDllPath_UntrustedPath_ShouldAlwaysBeRejected()
    {
        var untrustedPath = Path.Combine(Path.GetTempPath(), "WpfDevTools.Inspector.dll");

        var act = () => DllPathValidator.ValidateDllPath(untrustedPath);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*application directory*",
                "untrusted DLL paths must always be rejected before reaching signature check");
    }

    [Fact]
    public void ValidateDllPath_UntrustedPath_WithEnvVar_ShouldStillBeRejected()
    {
        var untrustedPath = Path.Combine(Path.GetTempPath(), "WpfDevTools.Inspector.dll");
        var previousValue = Environment.GetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK");

        try
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK", "1");

            var act = () => DllPathValidator.ValidateDllPath(untrustedPath);
            act.Should().Throw<ArgumentException>()
                .WithMessage("*application directory*",
                    "env var must NOT bypass trusted-root path validation");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK", previousValue);
        }
    }

    [Fact]
    public void EnumerateTrustedRoots_ShouldIncludePrimaryRepositoryRoot_ForWorktreeBaseDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var mainRoot = Path.Combine(root, "repo");
        var worktreeRoot = Path.Combine(mainRoot, ".worktrees", "feature-branch");
        var baseDirectory = Path.Combine(worktreeRoot, "src", "WpfDevTools.Mcp.Server", "bin", "Debug");
        Directory.CreateDirectory(baseDirectory);
        File.WriteAllText(Path.Combine(mainRoot, "WpfDevTools.sln"), string.Empty);
        File.WriteAllText(Path.Combine(worktreeRoot, "WpfDevTools.sln"), string.Empty);

        try
        {
            var trustedRoots = DllPathValidator.EnumerateTrustedRoots(baseDirectory);

            trustedRoots.Should().Contain(Path.GetFullPath(baseDirectory));
            trustedRoots.Should().Contain(Path.GetFullPath(mainRoot));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidateDllPath_WorktreeBuild_ShouldTrustPrimaryRepositoryArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var mainRoot = Path.Combine(root, "repo");
        var worktreeRoot = Path.Combine(mainRoot, ".worktrees", "feature-branch");
        var baseDirectory = Path.Combine(worktreeRoot, "src", "WpfDevTools.Mcp.Server", "bin", "Debug");
        var trustedDllPath = Path.Combine(
            mainRoot,
            "artifacts",
            "bootstrapper",
            "Debug",
            "x64",
            "WpfDevTools.Bootstrapper.x64.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(trustedDllPath)!);
        Directory.CreateDirectory(baseDirectory);
        File.WriteAllText(Path.Combine(mainRoot, "WpfDevTools.sln"), string.Empty);
        File.WriteAllText(Path.Combine(worktreeRoot, "WpfDevTools.sln"), string.Empty);
        File.WriteAllText(trustedDllPath, string.Empty);

        try
        {
            var act = () => DllPathValidator.ValidateDllPath(trustedDllPath, baseDirectory);
            var signatureAction = DllPathValidator.GetSignatureAction(baseDirectory);

            if (signatureAction == SignaturePolicy.Action.Skip)
            {
                act.Should().NotThrow();
            }
            else
            {
                act.Should().Throw<InvalidOperationException>()
                    .WithMessage("*signature*");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryGetBuildConfiguration_ReleasePublishDirectory_ShouldReturnRelease()
    {
        var baseDirectory = Path.Combine(
            "G:\\wpf-devtools-mcp",
            "src",
            "WpfDevTools.Mcp.Server",
            "bin",
            "Release",
            "net8.0-windows",
            "publish");

        var configuration = DllPathValidator.TryGetBuildConfiguration(baseDirectory);

        configuration.Should().Be("Release");
    }
}

