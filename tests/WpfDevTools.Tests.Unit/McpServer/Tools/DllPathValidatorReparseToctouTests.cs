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
        var calls = 0;

        try
        {
            DllPathValidator.ReparsePointChainDetectorOverrideForTesting = _ => ++calls >= 2;

            var act = () => DllPathValidator.ValidateDllPath(dllPath);

            act.Should().Throw<ArgumentException>()
                .WithMessage("*reparse point*");
            calls.Should().BeGreaterThanOrEqualTo(2);
        }
        finally
        {
            DllPathValidator.ReparsePointChainDetectorOverrideForTesting = previousDetector;
        }
    }
}
