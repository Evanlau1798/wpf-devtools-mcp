using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using Xunit;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerOptionalPackArchitectureTests
{
    [Theory]
    [InlineData("style-pack", ComposerPackRoles.Primary, true)]
    [InlineData("skill-generated-style-pack", ComposerPackRoles.Primary, true)]
    [InlineData("control-pack", ComposerPackRoles.ControlPack, false)]
    [InlineData("layout-pack", ComposerPackRoles.LayoutPack, false)]
    [InlineData("icon-pack", ComposerPackRoles.IconPack, false)]
    [InlineData("recipe-pack", ComposerPackRoles.RecipePack, false)]
    [InlineData("extension-pack", ComposerPackRoles.Extension, false)]
    [InlineData("project-local-pack", ComposerPackRoles.ProjectLocalPack, false)]
    [InlineData("unknown", ComposerPackRoles.Other, false)]
    public void PackKindRoleResolver_ShouldMapGenericPackMetadata(
        string packKind,
        string expectedRole,
        bool expectedRequired)
    {
        var result = ComposerPackKindRoleResolver.Resolve(packKind);

        result.Role.Should().Be(expectedRole);
        result.Required.Should().Be(expectedRequired);
    }
}
