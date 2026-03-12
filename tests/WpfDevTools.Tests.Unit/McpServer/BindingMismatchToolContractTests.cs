using System.Reflection;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class BindingMismatchToolContractTests
{
    [Fact]
    public void GetBindingMismatches_ShouldExposeOptionalScopeParameters()
    {
        AssertOptionalParameter(nameof(BindingMcpTools.GetBindingMismatches), "processId", typeof(int?), null);
        AssertOptionalParameter(nameof(BindingMcpTools.GetBindingMismatches), "elementId", typeof(string), null);
        AssertOptionalParameter(nameof(BindingMcpTools.GetBindingMismatches), "recursive", typeof(bool), false);
        AssertOptionalParameter(nameof(BindingMcpTools.GetBindingMismatches), "includeFramework", typeof(bool), false);
    }

    private static void AssertOptionalParameter(
        string methodName,
        string parameterName,
        Type parameterType,
        object? defaultValue)
    {
        var method = typeof(BindingMcpTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();

        var parameter = method!.GetParameters().SingleOrDefault(item => item.Name == parameterName);
        parameter.Should().NotBeNull();
        parameter!.ParameterType.Should().Be(parameterType);
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().Be(defaultValue);
    }
}
