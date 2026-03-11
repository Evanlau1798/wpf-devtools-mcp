using System.Reflection;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ElementSearchContractTests
{
    [Fact]
    public void FindElements_ShouldExposeOptionalMatchModeAndTypeNames()
    {
        AssertOptionalParameter(nameof(TreeMcpTools.FindElements), "matchMode", typeof(string), null);
        AssertOptionalParameter(nameof(TreeMcpTools.FindElements), "typeNames", typeof(string[]), null);
    }

    private static void AssertOptionalParameter(string methodName, string parameterName, Type parameterType, object? defaultValue)
    {
        var method = typeof(TreeMcpTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();

        var parameter = method!.GetParameters().SingleOrDefault(item => item.Name == parameterName);
        parameter.Should().NotBeNull();
        parameter!.ParameterType.Should().Be(parameterType);
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().Be(defaultValue);
    }
}
