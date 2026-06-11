using FluentAssertions;
using WpfDevTools.Tests.TestApp;
using Xunit;

namespace WpfDevTools.Tests.Unit.TestApp;

public sealed class ModernThemeStateTests
{
    [Fact]
    public void Constructor_ShouldExposeDeterministicDiagnostics()
    {
        var capabilities = new ModernBackdropCapability(true, "Supported", "Mica");
        var state = new ModernThemeState(capabilities);

        state.ThemeModeText.Should().Be("System");
        state.AccentText.Should().Be("Blue");
        state.AccentHex.Should().Be("#FF2563EB");
        state.CornerRadiusText.Should().Be("18");
        state.BackdropSupportedText.Should().Be("Supported");
    }

    [Fact]
    public void ApplySelections_ShouldUpdatePaletteAndDiagnostics()
    {
        var capabilities = new ModernBackdropCapability(false, "Not Supported", "Fallback");
        var state = new ModernThemeState(capabilities);

        state.ApplyThemeMode(ModernThemeMode.Dark);
        state.ApplyAccent(ModernAccentPreset.Emerald);

        state.ThemeModeText.Should().Be("Dark");
        state.AccentText.Should().Be("Emerald");
        state.AccentHex.Should().Be("#FF059669");
        state.SurfaceHex.Should().Be("#FF0F172A");
        state.CardHex.Should().Be("#FF111827");
        state.BackdropModeText.Should().Be("Fallback");
    }
}
