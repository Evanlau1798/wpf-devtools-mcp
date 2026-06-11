using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ProcessEnvironment")]
public sealed class SignaturePolicyEnvironmentTests
{
    [Fact]
    public void IsTrustedLocalDevelopmentSignatureSkipOptInEnabled_WithLegacyEnvVar_ShouldReturnFalse()
    {
        var previousValue = Environment.GetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK");
        var previousOverride = DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting;

        try
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK", "1");
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = true;

            var enabled = DllPathValidator.IsTrustedLocalDevelopmentSignatureSkipOptInEnabled(AppContext.BaseDirectory);

            enabled.Should().BeFalse(
                "signature skip opt-in must be scoped through explicit validators instead of process-wide environment state");
        }
        finally
        {
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = previousOverride;
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_SKIP_SIGNATURE_CHECK", previousValue);
        }
    }
}
