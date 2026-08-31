using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Drafts;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintInputRecoveryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WhenBlueprintInputIsMissing_ShouldReturnStructuredIssue(string? input)
    {
        var result = BlueprintInputResolver.Resolve(input);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("MissingBlueprintInput");
        result.Error.RequestJsonPath.Should().Be("$.blueprintJson");
    }

    [Fact]
    public async Task ValidateUiBlueprint_WhenBlueprintInputIsNull_ShouldReturnStructuredIssue()
    {
        var result = await UiComposerMcpTools.ValidateUiBlueprint(
            null!,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        var error = result.StructuredContent!.Value.GetProperty("errors")[0];
        error.GetProperty("code").GetString().Should().Be("MissingBlueprintInput");
        error.GetProperty("jsonPath").GetString().Should().Be("$.blueprintJson");
    }
}
