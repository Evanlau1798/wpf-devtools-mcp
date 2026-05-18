using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using System.Reflection;
using Xunit;
using FluentAssertions;
using Xunit.Sdk;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Tests.Unit.Execution;
using WpfDevTools.Tests.Unit.Release;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ProcessEnvironment")]
public class SignaturePolicyTests
{
    private const string TestReleaseSignerThumbprint = "1111111111111111111111111111111111111111";
    private const string TestReleaseSignerSubject = "CN=WpfDevTools Test Release Signer";

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
            act.Should().Throw<System.Security.Cryptography.CryptographicException>()
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
    public void ValidateDllPath_WithTrustedRootJunctionToOutsidePath_ShouldThrow()
    {
        RequireWindowsJunctions();

        var root = Path.Combine(Path.GetTempPath(), "WpfDevTools_DllPathValidator_" + Guid.NewGuid().ToString("N"));
        var trustedRoot = Path.Combine(root, "trusted");
        var outsideRoot = Path.Combine(root, "outside");
        var junctionPath = Path.Combine(trustedRoot, "linked");
        var outsideDllPath = Path.Combine(outsideRoot, "WpfDevTools.Bootstrapper.x64.dll");
        var candidatePath = Path.Combine(junctionPath, "WpfDevTools.Bootstrapper.x64.dll");
        var previousTrustedLocalDevelopmentBuild = DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting;

        Directory.CreateDirectory(trustedRoot);
        Directory.CreateDirectory(outsideRoot);
        File.WriteAllText(outsideDllPath, string.Empty);

        try
        {
            CreateDirectoryJunctionOrSkip(junctionPath, outsideRoot);
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = true;

            var act = () => DllPathValidator.ValidateDllPath(candidatePath, trustedRoot);

            act.Should().Throw<ArgumentException>()
                .WithMessage("*reparse*",
                    "a trusted-root string prefix must not allow a junction or symlink to redirect DLL loading outside the trusted root");
        }
        finally
        {
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = previousTrustedLocalDevelopmentBuild;
            TryDeleteDirectoryJunction(junctionPath);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
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
                act.Should().Throw<System.Security.Cryptography.CryptographicException>()
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

    [Fact]
    public void IsTrustedLocalDevelopmentSignatureSkipOptInEnabled_WithoutEnvVar_ShouldReturnFalse()
    {
        var previousValue = Environment.GetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK");
        var baseDirectory = CreateWorkspaceBuildBaseDirectory();

        try
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK", null);

            var enabled = DllPathValidator.IsTrustedLocalDevelopmentSignatureSkipOptInEnabled(baseDirectory);

            enabled.Should().BeFalse(
                "trusted non-Release workspace builds must not relax signature verification unless the local developer opts in explicitly");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK", previousValue);
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(baseDirectory)!)!)!)!, recursive: true);
        }
    }

    [Fact]
    public void ValidateDllPath_ReleaseBuild_WhenWinVerifyTrustFails_ShouldKeepStableInvalidSignatureContract()
    {
        var previousVerifier = DllPathValidator.WinVerifyTrustOverrideForTesting;
        var previousTrustedLocalDevelopmentBuild = DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting;
        var previousSignerThumbprint = Environment.GetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
        var previousSignerSubject = Environment.GetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT");
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var trustedDllPath = Path.Combine(tempDirectory, "WpfDevTools.Inspector.dll");
        var verifierInvoked = false;
        const string expectedMessage =
            "Inspector DLL is not digitally signed or has an invalid signature. " +
            "In development, use a DEBUG build which auto-skips signature verification for local DLLs. " +
            "In production, sign the DLL with Authenticode.";
        var verifyMethod = typeof(DllPathValidator).GetMethod(
            "VerifyAuthenticodeSignature",
            BindingFlags.NonPublic | BindingFlags.Static);
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(trustedDllPath, "unsigned");

        try
        {
            verifyMethod.Should().NotBeNull("the signature verifier should remain a distinct implementation surface that tests can exercise deterministically");
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = false;
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT", "TESTSIGNER00000000000000000000000000000000");
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT", null);
            DllPathValidator.WinVerifyTrustOverrideForTesting = _ =>
            {
                verifierInvoked = true;
                return unchecked((int)0x800B0100);
            };

            var act = () => verifyMethod!.Invoke(null, new object[] { trustedDllPath, tempDirectory });

            var exception = act.Should().Throw<TargetInvocationException>().Which;
            var signatureException = exception.InnerException.Should().BeOfType<System.Security.Cryptography.CryptographicException>().Which;
            signatureException.Message.Should().Be(expectedMessage,
                "WinVerifyTrust failures should be normalized back to the established invalid-signature guidance contract");
            signatureException.InnerException.Should().BeOfType<System.Security.Cryptography.CryptographicException>(
                "the normalized CryptographicException should preserve the underlying native verification failure details");
            verifierInvoked.Should().BeTrue(
                "the Authenticode verifier should execute the WinVerifyTrust-based file check before certificate parsing");
        }
        finally
        {
            DllPathValidator.WinVerifyTrustOverrideForTesting = previousVerifier;
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = previousTrustedLocalDevelopmentBuild;
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT", previousSignerThumbprint);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT", previousSignerSubject);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void VerifyAuthenticodeSignature_PackagedReleaseBuild_WithPinnedCurrentServerSigner_ShouldAcceptSignedPayload()
    {
        var previousVerifier = DllPathValidator.WinVerifyTrustOverrideForTesting;
        var previousValidatedSigner = DllPathValidator.ValidatedSignerOverrideForTesting;
        var previousCurrentProcessSigner = DllPathValidator.CurrentProcessReleaseSignerOverrideForTesting;
        var previousTrustedLocalDevelopmentBuild = DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting;
        var previousSignerThumbprint = Environment.GetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
        var previousSignerSubject = Environment.GetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT");
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        var verifyMethod = typeof(DllPathValidator).GetMethod(
            "VerifyAuthenticodeSignature",
            BindingFlags.NonPublic | BindingFlags.Static);

        try
        {
            var packageRoot = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot, useSignedPayload: false);
            var baseDirectory = Path.Combine(packageRoot, "bin");
            var dllPath = Path.Combine(packageRoot, "bin", "inspectors", "net8.0-windows", "WpfDevTools.Inspector.dll");
            verifyMethod.Should().NotBeNull();
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = false;
            DllPathValidator.CurrentProcessReleaseSignerOverrideForTesting = CreateTestReleaseSigner();
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT", null);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT", null);
            DllPathValidator.WinVerifyTrustOverrideForTesting = _ => 0;
            DllPathValidator.ValidatedSignerOverrideForTesting = _ => CreateTestReleaseSigner();

            var act = () => verifyMethod!.Invoke(null, new object[] { dllPath, baseDirectory });

            act.Should().NotThrow<TargetInvocationException>(
                "release package payloads should validate when the signed DLL matches the currently running MCP server executable signer and no explicit env pin overrides it");
        }
        finally
        {
            DllPathValidator.WinVerifyTrustOverrideForTesting = previousVerifier;
            DllPathValidator.ValidatedSignerOverrideForTesting = previousValidatedSigner;
            DllPathValidator.CurrentProcessReleaseSignerOverrideForTesting = previousCurrentProcessSigner;
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = previousTrustedLocalDevelopmentBuild;
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT", previousSignerThumbprint);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT", previousSignerSubject);
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void VerifyAuthenticodeSignature_PackagedReleaseBuild_WhenPinnedEnvironmentSignerDoesNotMatch_ShouldThrow()
    {
        var previousVerifier = DllPathValidator.WinVerifyTrustOverrideForTesting;
        var previousValidatedSigner = DllPathValidator.ValidatedSignerOverrideForTesting;
        var previousCurrentProcessSigner = DllPathValidator.CurrentProcessReleaseSignerOverrideForTesting;
        var previousTrustedLocalDevelopmentBuild = DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting;
        var previousSignerThumbprint = Environment.GetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
        var previousSignerSubject = Environment.GetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT");
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        var verifyMethod = typeof(DllPathValidator).GetMethod(
            "VerifyAuthenticodeSignature",
            BindingFlags.NonPublic | BindingFlags.Static);

        try
        {
            var packageRoot = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot, useSignedPayload: false);
            var baseDirectory = Path.Combine(packageRoot, "bin");
            var dllPath = Path.Combine(packageRoot, "bin", "inspectors", "net8.0-windows", "WpfDevTools.Inspector.dll");
            verifyMethod.Should().NotBeNull();
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = false;
            DllPathValidator.CurrentProcessReleaseSignerOverrideForTesting = null;
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT", "0000000000000000000000000000000000000000");
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT", null);
            DllPathValidator.WinVerifyTrustOverrideForTesting = _ => 0;
            DllPathValidator.ValidatedSignerOverrideForTesting = _ => CreateTestReleaseSigner();

            var act = () => verifyMethod!.Invoke(null, new object[] { dllPath, baseDirectory });

            var exception = act.Should().Throw<TargetInvocationException>().Which;
            var signatureException = exception.InnerException.Should().BeOfType<System.Security.Cryptography.CryptographicException>().Which;
            signatureException.Message.Should().Contain("invalid signature");
            signatureException.InnerException.Should().BeOfType<System.Security.Cryptography.CryptographicException>();
            signatureException.InnerException!.Message.Should().Contain("pinned release signer");
        }
        finally
        {
            DllPathValidator.WinVerifyTrustOverrideForTesting = previousVerifier;
            DllPathValidator.ValidatedSignerOverrideForTesting = previousValidatedSigner;
            DllPathValidator.CurrentProcessReleaseSignerOverrideForTesting = previousCurrentProcessSigner;
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = previousTrustedLocalDevelopmentBuild;
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT", previousSignerThumbprint);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT", previousSignerSubject);
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void VerifyAuthenticodeSignature_WhenSubjectOnlyEnvironmentPinIsProvided_ShouldRejectWeakPinConfiguration()
    {
        var previousVerifier = DllPathValidator.WinVerifyTrustOverrideForTesting;
        var previousValidatedSigner = DllPathValidator.ValidatedSignerOverrideForTesting;
        var previousCurrentProcessSigner = DllPathValidator.CurrentProcessReleaseSignerOverrideForTesting;
        var previousTrustedLocalDevelopmentBuild = DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting;
        var previousSignerThumbprint = Environment.GetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
        var previousSignerSubject = Environment.GetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT");
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        var verifyMethod = typeof(DllPathValidator).GetMethod(
            "VerifyAuthenticodeSignature",
            BindingFlags.NonPublic | BindingFlags.Static);

        try
        {
            var packageRoot = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot, useSignedPayload: false);
            var baseDirectory = Path.Combine(packageRoot, "bin");
            var dllPath = Path.Combine(packageRoot, "bin", "inspectors", "net8.0-windows", "WpfDevTools.Inspector.dll");
            verifyMethod.Should().NotBeNull();
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = false;
            DllPathValidator.CurrentProcessReleaseSignerOverrideForTesting = null;
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT", null);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT", TestReleaseSignerSubject);
            DllPathValidator.WinVerifyTrustOverrideForTesting = _ => 0;
            DllPathValidator.ValidatedSignerOverrideForTesting = _ => CreateTestReleaseSigner();

            var act = () => verifyMethod!.Invoke(null, new object[] { dllPath, baseDirectory });

            var exception = act.Should().Throw<TargetInvocationException>().Which;
            var signatureException = exception.InnerException.Should().BeOfType<System.Security.Cryptography.CryptographicException>().Which;
            signatureException.Message.Should().Contain("invalid signature");
            signatureException.InnerException.Should().BeOfType<System.Security.Cryptography.CryptographicException>();
            signatureException.InnerException!.Message.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT requires WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
        }
        finally
        {
            DllPathValidator.WinVerifyTrustOverrideForTesting = previousVerifier;
            DllPathValidator.ValidatedSignerOverrideForTesting = previousValidatedSigner;
            DllPathValidator.CurrentProcessReleaseSignerOverrideForTesting = previousCurrentProcessSigner;
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = previousTrustedLocalDevelopmentBuild;
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT", previousSignerThumbprint);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT", previousSignerSubject);
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string CreateWorkspaceBuildBaseDirectory(string configuration = "Checked")
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

    private static DllPathValidator.ValidatedAuthenticodeSigner CreateTestReleaseSigner()
        => new(
            TestReleaseSignerThumbprint,
            TestReleaseSignerSubject,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

    private static void RequireWindowsJunctions()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("Directory junction contract is Windows-specific.");
        }
    }

    private static void CreateDirectoryJunctionOrSkip(string junctionPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c mklink /J \"" + junctionPath + "\" \"" + targetPath + "\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        process.Should().NotBeNull();
        process!.WaitForExit(5000).Should().BeTrue("mklink should complete promptly");
        if (process.ExitCode != 0)
        {
            throw SkipException.ForSkip(
                "Directory junction creation failed: " + process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd());
        }
    }

    private static void TryDeleteDirectoryJunction(string junctionPath)
    {
        try
        {
            if (Directory.Exists(junctionPath))
            {
                Directory.Delete(junctionPath);
            }
        }
        catch
        {
            // Best-effort cleanup for a failed junction test.
        }
    }
}

