using FluentAssertions;
using WpfDevTools.Tests.TestApp;
using Xunit;

namespace WpfDevTools.Tests.Unit.TestApp;

public sealed class ModernBackdropCapabilitiesTests
{
    [Fact]
    public void Evaluate_Windows11Build_ShouldReportMicaSupport()
    {
        var result = ModernBackdropCapabilities.Evaluate(new Version(10, 0, 22621, 0));

        result.SupportsMica.Should().BeTrue();
        result.BackdropSupportedText.Should().Be("Supported");
        result.DefaultBackdropMode.Should().Be("Mica");
    }

    [Fact]
    public void Evaluate_PreWindows11Build_ShouldReportFallback()
    {
        var result = ModernBackdropCapabilities.Evaluate(new Version(10, 0, 19045, 0));

        result.SupportsMica.Should().BeFalse();
        result.BackdropSupportedText.Should().Be("Not Supported");
        result.DefaultBackdropMode.Should().Be("Fallback");
    }
}
