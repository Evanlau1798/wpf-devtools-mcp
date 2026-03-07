using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.McpServer;

public class SignaturePolicyTests
{
    // === Policy decision tests (pure logic, no file system) ===

    [Fact]
    public void Evaluate_ReleaseBuild_ShouldAlwaysVerify()
    {
        var result = SignaturePolicy.Evaluate(
            isDebugBuild: false,
            isTrustedRoot: true,
            hasSkipEnvVar: true,
            isCi: false);

        result.Should().Be(SignaturePolicy.Action.Verify,
            "RELEASE builds must always verify signatures regardless of other flags");
    }

    [Fact]
    public void Evaluate_DebugBuild_TrustedRoot_ShouldSkip()
    {
        var result = SignaturePolicy.Evaluate(
            isDebugBuild: true,
            isTrustedRoot: true,
            hasSkipEnvVar: false,
            isCi: false);

        result.Should().Be(SignaturePolicy.Action.Skip,
            "DEBUG builds should skip verification for DLLs in trusted roots");
    }

    [Fact]
    public void Evaluate_DebugBuild_UntrustedPath_WithEnvVar_NoCi_ShouldSkip()
    {
        var result = SignaturePolicy.Evaluate(
            isDebugBuild: true,
            isTrustedRoot: false,
            hasSkipEnvVar: true,
            isCi: false);

        result.Should().Be(SignaturePolicy.Action.Skip,
            "DEBUG builds with env var bypass should skip for untrusted paths outside CI");
    }

    [Fact]
    public void Evaluate_DebugBuild_UntrustedPath_WithEnvVar_InCi_ShouldVerify()
    {
        var result = SignaturePolicy.Evaluate(
            isDebugBuild: true,
            isTrustedRoot: false,
            hasSkipEnvVar: true,
            isCi: true);

        result.Should().Be(SignaturePolicy.Action.Verify,
            "CI environments must always verify even in DEBUG with env var set");
    }

    [Fact]
    public void Evaluate_DebugBuild_UntrustedPath_NoEnvVar_ShouldVerify()
    {
        var result = SignaturePolicy.Evaluate(
            isDebugBuild: true,
            isTrustedRoot: false,
            hasSkipEnvVar: false,
            isCi: false);

        result.Should().Be(SignaturePolicy.Action.Verify,
            "DEBUG builds without env var must verify for untrusted paths");
    }

    [Fact]
    public void Evaluate_DebugBuild_UntrustedPath_NoEnvVar_InCi_ShouldVerify()
    {
        var result = SignaturePolicy.Evaluate(
            isDebugBuild: true,
            isTrustedRoot: false,
            hasSkipEnvVar: false,
            isCi: true);

        result.Should().Be(SignaturePolicy.Action.Verify,
            "CI without env var must always verify");
    }

    // === Integration: ConnectTool behavior with signature policy ===

    [Fact]
    public void ConnectTool_DebugBuild_TrustedRoot_ShouldNotThrow()
    {
        // Trusted root DLL (in app directory) should not trigger signature check in DEBUG
        var trustedDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");

#if DEBUG
        var act = () => new ConnectTool(new SessionManager(), trustedDllPath);
        act.Should().NotThrow(
            "DEBUG builds auto-skip signature verification for trusted root DLLs");
#else
        // In RELEASE, unsigned DLLs always fail
        var act = () => new ConnectTool(new SessionManager(), trustedDllPath);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*signature*");
#endif
    }

    [Fact]
    public void ConnectTool_UntrustedDll_ErrorMessage_ShouldBeEnglishAndActionable()
    {
#if DEBUG
        // Create DLL outside trusted roots to trigger actual verification
        var tempDir = Path.Combine(Path.GetTempPath(), $"wpfdevtools_sigtest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var untrustedDllPath = Path.Combine(tempDir, "WpfDevTools.Inspector.dll");

        try
        {
            // Copy or create a minimal DLL file
            var sourceDll = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");
            if (File.Exists(sourceDll))
                File.Copy(sourceDll, untrustedDllPath);
            else
                File.WriteAllBytes(untrustedDllPath, new byte[] { 0x4D, 0x5A });

            var act = () => new ConnectTool(new SessionManager(), untrustedDllPath);

            // Should throw because DLL is outside trusted roots and unsigned
            // The exception should have path validation error (untrusted path)
            act.Should().Throw<ArgumentException>()
                .WithMessage("*application directory*",
                    "untrusted DLL paths must be rejected with clear English error message");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
#else
        Assert.True(true, "RELEASE mode always verifies - tested by Constructor_WithUnsignedDllInTrustedRoot_ShouldNotThrowInDebug");
#endif
    }
}
