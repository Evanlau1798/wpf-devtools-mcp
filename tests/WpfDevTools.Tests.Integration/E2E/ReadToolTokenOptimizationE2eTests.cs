using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public class ReadToolTokenOptimizationE2eTests
{
    private readonly McpE2eFixture _fixture;

    public ReadToolTokenOptimizationE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
        E2eTestHelpers.AssertFixtureReady(_fixture);
    }

    [Fact]
    public async Task GetViewModel_WithPropertyNames_ShouldReturnRequestedSubsetOnly()
    {
        var result = await _fixture.Client.CallToolAsync(
            "get_viewmodel",
            new
            {
                propertyNames = new[] { "Name", "CanSave" }
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var properties = result.GetProperty("properties").EnumerateArray().ToList();
        properties.Should().HaveCount(2);
        properties.Select(property => property.GetProperty("name").GetString())
            .Should().Equal("Name", "CanSave");
    }

    [Fact]
    public async Task GetDpValueSource_WithCompactTrue_ShouldOmitVerboseFields()
    {
        var result = await _fixture.Client.CallToolAsync(
            "get_dp_value_source",
            new
            {
                propertyName = "Width",
                compact = true
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("propertyName").GetString().Should().Be("Width");
        result.TryGetProperty("effectiveValue", out _).Should().BeTrue();
        result.TryGetProperty("rawBaseValueSource", out _).Should().BeFalse();
        result.TryGetProperty("localValue", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetBindingErrors_WithMaxErrors_ShouldTruncateResponse()
    {
        var result = await _fixture.Client.CallToolAsync(
            "get_binding_errors",
            new
            {
                maxErrors = 1
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetBindingErrors_WithCompactTrue_ShouldReducePayloadSize()
    {
        var verbose = await _fixture.Client.CallToolAsync(
            "get_binding_errors",
            new
            {
                maxErrors = 1
            });
        var compact = await _fixture.Client.CallToolAsync(
            "get_binding_errors",
            new
            {
                maxErrors = 1,
                compact = true
            });

        verbose.GetProperty("success").GetBoolean().Should().BeTrue();
        compact.GetProperty("success").GetBoolean().Should().BeTrue();
        compact.GetRawText().Length.Should().BeLessThan(verbose.GetRawText().Length);
    }

    [Fact]
    public async Task GetBindings_WithStatusFilterActive_ShouldReturnOnlyActiveStatuses()
    {
        var result = await _fixture.Client.CallToolAsync(
            "get_bindings",
            new
            {
                recursive = true,
                statusFilter = "Active"
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        foreach (var binding in result.GetProperty("bindings").EnumerateArray())
        {
            binding.GetProperty("status").GetString().Should().Be("Active");
        }
    }

    [Fact]
    public async Task GetAppliedStyles_WithCompactTrue_ShouldReturnStyleSummary()
    {
        var result = await _fixture.Client.CallToolAsync(
            "get_applied_styles",
            new
            {
                compact = true
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("styleCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }
}
