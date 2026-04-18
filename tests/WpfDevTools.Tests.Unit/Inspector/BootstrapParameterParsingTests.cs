using System.Reflection;
using FluentAssertions;
using WpfDevTools.Inspector;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector;

public class BootstrapParameterParsingTests
{
    [Fact]
    public void ParseParameters_WithSecureBootstrapParameters_ShouldPreserveBase64Padding()
    {
        var parameters = string.Join(";",
            "inspectorDllPath=C:\\app\\Inspector.dll",
            "pipeName=WpfDevTools_1234",
            "auth=enabled",
            "authSecretBase64=YWJjZA==",
            "encryption=enabled",
            "certDirectory=C:\\secure certs");

        var result = ParseParameters(parameters);

        result.Should().ContainKey("inspectorDllPath").WhoseValue.Should().Be("C:\\app\\Inspector.dll");
        result.Should().ContainKey("pipeName").WhoseValue.Should().Be("WpfDevTools_1234");
        result.Should().ContainKey("auth").WhoseValue.Should().Be("enabled");
        result.Should().ContainKey("authSecretBase64").WhoseValue.Should().Be("YWJjZA==");
        result.Should().ContainKey("encryption").WhoseValue.Should().Be("enabled");
        result.Should().ContainKey("certDirectory").WhoseValue.Should().Be("C:\\secure certs");
    }

    [Fact]
    public void ParseParameters_WithLegacyPayloadContainingEqualsInPath_ShouldTreatInputAsLegacyFormat()
    {
        var result = ParseParameters(@"C:\temp\a=b\Inspector.dll;WpfDevTools_1234");

        result.Should().ContainKey("inspectorDllPath").WhoseValue.Should().Be(@"C:\temp\a=b\Inspector.dll");
        result.Should().ContainKey("pipeName").WhoseValue.Should().Be("WpfDevTools_1234");
    }

    private static IReadOnlyDictionary<string, string> ParseParameters(string parameters)
    {
        var method = typeof(Bootstrap).GetMethod(
            "ParseParameters",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        var result = method!.Invoke(null, [parameters]);
        result.Should().BeAssignableTo<IReadOnlyDictionary<string, string>>();
        return (IReadOnlyDictionary<string, string>)result!;
    }
}