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

        description.Should().Contain("message?: string",
            "the response format should show that compact mode can omit the verbose message field");
        description.Should().Contain("Compact mode omits the message field",
            "the description should explain when the message field is absent from binding errors");
        description.Should().Contain("compact=false",
            "the description should explain how to opt back into the verbose trace text");
    }
}