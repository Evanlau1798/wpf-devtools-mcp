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
}
