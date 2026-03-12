using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class BindingMismatchDiagnosticsE2eTests
{
    private readonly McpE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BindingMismatchDiagnosticsE2eTests(McpE2eFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task GetBindingMismatches_ShouldReportIntentionalTypeMismatchInTestApp()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_binding_mismatches",
            new { processId = _fixture.TestAppProcessId, recursive = true });

        _output.WriteLine(E2eTestHelpers.Truncate(result.GetRawText(), 800));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("mismatches").EnumerateArray()
            .Should()
            .Contain(item =>
                item.GetProperty("elementName").GetString() == "BrushMismatchButton" &&
                item.GetProperty("diagnosis").GetString() == "TypeMismatch");
    }

    [Fact]
    public async Task GetBindingMismatches_ShouldExcludeFrameworkTemplateNoiseByDefault()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var defaultResult = await _fixture.Client.CallToolAsync(
            "get_binding_mismatches",
            new { processId = _fixture.TestAppProcessId, recursive = true });

        var includeFrameworkResult = await _fixture.Client.CallToolAsync(
            "get_binding_mismatches",
            new { processId = _fixture.TestAppProcessId, recursive = true, includeFramework = true });

        _output.WriteLine(E2eTestHelpers.Truncate(defaultResult.GetRawText(), 800));
        _output.WriteLine(E2eTestHelpers.Truncate(includeFrameworkResult.GetRawText(), 800));

        var defaultCount = defaultResult.GetProperty("mismatchCount").GetInt32();
        var includeFrameworkCount = includeFrameworkResult.GetProperty("mismatchCount").GetInt32();
        var defaultOrigins = defaultResult.GetProperty("mismatches").EnumerateArray()
            .Select(item => item.GetProperty("origin").GetString())
            .ToArray();

        includeFrameworkCount.Should().BeGreaterThanOrEqualTo(defaultCount);
        defaultOrigins.Should().NotContain("FrameworkTemplate");
    }
}
