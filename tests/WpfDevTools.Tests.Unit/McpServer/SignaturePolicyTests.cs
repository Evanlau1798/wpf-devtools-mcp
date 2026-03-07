using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
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

    // === Integration: ConnectTool path validation ===

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
    public void ConnectTool_UntrustedPath_ShouldAlwaysBeRejected()
    {
        // Untrusted path must be rejected regardless of build configuration
        var untrustedPath = Path.Combine(Path.GetTempPath(), "WpfDevTools.Inspector.dll");

        var act = () => new ConnectTool(new SessionManager(), untrustedPath);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*application directory*",
                "untrusted DLL paths must always be rejected before reaching signature check");
    }

    [Fact]
    public void ConnectTool_UntrustedPath_WithEnvVar_ShouldStillBeRejected()
    {
        // WPFDEVTOOLS_SKIP_SIGNATURE_CHECK must NOT bypass path validation
        var untrustedPath = Path.Combine(Path.GetTempPath(), "WpfDevTools.Inspector.dll");
        var previousValue = Environment.GetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK");

        try
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK", "1");

            var act = () => new ConnectTool(new SessionManager(), untrustedPath);
            act.Should().Throw<ArgumentException>()
                .WithMessage("*application directory*",
                    "env var must NOT bypass trusted-root path validation");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK", previousValue);
        }
    }
}
