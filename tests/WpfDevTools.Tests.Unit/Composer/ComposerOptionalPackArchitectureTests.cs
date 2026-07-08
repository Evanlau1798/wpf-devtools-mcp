using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using Xunit;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerOptionalPackArchitectureTests
{
    [Fact]
    public void WpfUiPackPlan_ShouldSeparateCoreDefaultAndOptionalPacks()
    {
        ComposerPackRoleCatalog.DefaultWpfUiPackId.Should().Be("wpfui");
        ComposerPackRoleCatalog.WpfUiPacks.Should().BeEquivalentTo(
        [
            new { Id = "wpfui", Role = ComposerPackRoles.Primary, Required = true },
            new { Id = "wpfui.gallery", Role = ComposerPackRoles.RecipeExample, Required = false },
            new { Id = "wpfui.syntaxhighlight", Role = ComposerPackRoles.OptionalControl, Required = false },
            new { Id = "wpfui.tray", Role = ComposerPackRoles.OptionalControl, Required = false },
            new { Id = "wpfui.templates", Role = ComposerPackRoles.ShellTemplate, Required = false }
        ]);
    }
}
