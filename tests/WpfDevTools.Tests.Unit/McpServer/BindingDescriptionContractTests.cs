using System.ComponentModel;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class BindingDescriptionContractTests
{
    [Fact]
    public void GetBindingErrors_Description_ShouldDocumentCompactMessageOmission()
    {
        var description = typeof(BindingMcpTools)
            .GetMethod(nameof(BindingMcpTools.GetBindingErrors))!
            .GetCustomAttributes(typeof(DescriptionAttribute), false)
            .Cast<DescriptionAttribute>()
            .Single()
            .Description;

        description.Should().Contain("compact=false",
            "the description should explain how to opt back into the verbose trace text");
        description.Should().Contain("structuredContent",
            "binding error descriptions should point clients at the canonical machine-readable payload instead of embedding a full inline schema block");
        description.Should().Contain("wpf://contracts/response",
            "field-level binding error contracts should be discoverable from the shared response contract resource");
        description.Should().Contain("compact=false",
            "the description should explain how to opt back into the verbose trace text");
    }
}