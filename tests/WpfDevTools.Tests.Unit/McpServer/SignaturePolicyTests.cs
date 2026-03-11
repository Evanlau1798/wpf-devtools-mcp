using System.Security.Cryptography.X509Certificates;
using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.McpServer;

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
    public void GetCurrentBuildRevocationMode_ShouldMatchBuildConfiguration()
    {
        var mode = DllPathValidator.GetCurrentBuildRevocationMode();

#if DEBUG
        mode.Should().Be(X509RevocationMode.Offline,
            "the DEBUG build must wire Offline revocation into Authenticode chain validation to avoid network-blocking verification");
#else
        mode.Should().Be(X509RevocationMode.Online,
            "the RELEASE build must wire Online revocation into Authenticode chain validation for maximum security");
#endif
    }
    // === Integration: ConnectTool path validation ===

    [Fact]
    public void ValidateDllPath_DebugBuild_TrustedRoot_ShouldNotThrow()
    {
        var trustedDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");

#if DEBUG
        var act = () => DllPathValidator.ValidateDllPath(trustedDllPath);
        act.Should().NotThrow(
            "DEBUG builds auto-skip signature verification for trusted root DLLs");
#else
        var act = () => DllPathValidator.ValidateDllPath(trustedDllPath);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*signature*");
#endif
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

#if DEBUG
            act.Should().NotThrow();
#else
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*signature*");
#endif
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

