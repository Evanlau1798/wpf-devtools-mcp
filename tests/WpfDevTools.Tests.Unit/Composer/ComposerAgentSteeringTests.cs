using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerAgentSteeringTests
{
    [Fact]
    public async Task BlockCatalog_ShouldReturnBriefFirstPackNeutralAuthoringGuidance()
    {
        var result = await UiComposerMcpTools.GetUiBlockCatalog(
            includeRecipes: false,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse();
        var payload = result.StructuredContent!.Value;
        var guidance = payload.GetProperty("authoringGuidance");
        guidance.GetProperty("strategy").GetString().Should().Be("brief-first");
        guidance.GetProperty("recipesRequested").GetBoolean().Should().BeFalse();
        guidance.GetProperty("creativeBriefRequired").GetBoolean().Should().BeTrue();
        guidance.GetProperty("principles").GetArrayLength().Should().BeGreaterThan(1);
        guidance.GetRawText().ToLowerInvariant().Should().NotContain("wpfui");
    }

    [Fact]
    public void ComposerToolDiscoveryText_ShouldUsePackNeutralExamplesAndOptionalRecipes()
    {
        var text = typeof(UiComposerMcpTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .SelectMany(method => method.GetParameters()
                .Select(parameter => parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty)
                .Prepend(method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty))
            .Aggregate(string.Empty, (current, value) => current + "\n" + value);

        text.Should().Contain("brief-first");
        text.Should().Contain("optional accelerator");
        text.ToLowerInvariant().Should().NotContain("wpfui");
        text.ToLowerInvariant().Should().NotContain("fluentwindow");
    }
}
