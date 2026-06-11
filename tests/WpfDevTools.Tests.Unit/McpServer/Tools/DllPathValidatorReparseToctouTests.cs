using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class DllPathValidatorReparseToctouTests
{
    [Fact]
    public void ValidateDllPath_WhenPathBecomesReparsePointAfterInitialValidation_ShouldFailClosed()
    {
        var dllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");
        var previousDetector = DllPathValidator.ReparsePointChainDetectorOverrideForTesting;
        var previousTrustedLocalBuild = DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting;
        var calls = 0;

        try
        {
            DllPathValidator.ReparsePointChainDetectorOverrideForTesting = _ => ++calls >= 2;
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = true;

            var act = () => DllPathValidator.ValidateDllPath(
                dllPath,
                AppContext.BaseDirectory,
                trustedLocalDevelopmentSkipOptIn: true);

            act.Should().Throw<ArgumentException>()
                .WithMessage("*reparse point*");
            calls.Should().BeGreaterThanOrEqualTo(2);
        }
        finally
        {
            DllPathValidator.ReparsePointChainDetectorOverrideForTesting = previousDetector;
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = previousTrustedLocalBuild;
        }
    }
}
