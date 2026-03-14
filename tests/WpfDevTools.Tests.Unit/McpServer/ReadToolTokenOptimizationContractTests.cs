using System.Reflection;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public class ReadToolTokenOptimizationContractTests
{
    [Fact]
    public void GetViewModel_ShouldExposeOptionalPropertyNamesFilter()
    {
        AssertOptionalParameter(
            typeof(MvvmMcpTools),
            nameof(MvvmMcpTools.GetViewModel),
            "propertyNames",
            typeof(string[]),
            null);
    }

    [Fact]
    public void GetDpValueSource_ShouldExposeOptionalCompactMode()
    {
        AssertOptionalParameter(
            typeof(DependencyPropertyMcpTools),
            nameof(DependencyPropertyMcpTools.GetDpValueSource),
            "compact",
            typeof(bool),
            false);
    }

    [Fact]
    public void GetBindingErrors_ShouldExposeOptionalWindowingParameters()
    {
        AssertOptionalParameter(
            typeof(BindingMcpTools),
            nameof(BindingMcpTools.GetBindingErrors),
            "maxErrors",
            typeof(int?),
            null);
        AssertOptionalParameter(
            typeof(BindingMcpTools),
            nameof(BindingMcpTools.GetBindingErrors),
            "sinceTimestamp",
            typeof(string),
            null);
        AssertOptionalParameter(
            typeof(BindingMcpTools),
            nameof(BindingMcpTools.GetBindingErrors),
            "compact",
            typeof(bool),
            true);
    }

    [Fact]
    public void GetBindings_ShouldExposeOptionalStatusFilter()
    {
        AssertOptionalParameter(
            typeof(BindingMcpTools),
            nameof(BindingMcpTools.GetBindings),
            "statusFilter",
            typeof(string),
            null);
    }

    [Fact]
    public void GetAppliedStyles_ShouldExposeOptionalCompactMode()
    {
        AssertOptionalParameter(
            typeof(StyleMcpTools),
            nameof(StyleMcpTools.GetAppliedStyles),
            "compact",
            typeof(bool),
            false);
    }

    [Fact]
    public void GetElementSnapshot_ShouldExposeOptionalIncludePropertiesFilter()
    {
        AssertOptionalParameter(
            typeof(SceneDiagnosticsMcpTools),
            nameof(SceneDiagnosticsMcpTools.GetElementSnapshot),
            "includeProperties",
            typeof(string[]),
            null);
    }

    private static void AssertOptionalParameter(
        Type declaringType,
        string methodName,
        string parameterName,
        Type parameterType,
        object? defaultValue)
    {
        var method = declaringType.GetMethod(methodName);
        method.Should().NotBeNull();

        var parameter = method!.GetParameters().SingleOrDefault(item => item.Name == parameterName);
        parameter.Should().NotBeNull($"{declaringType.Name}.{methodName} should expose '{parameterName}'");
        parameter!.ParameterType.Should().Be(parameterType);
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().Be(defaultValue);
    }
}
