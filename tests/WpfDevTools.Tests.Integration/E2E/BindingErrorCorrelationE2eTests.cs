using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class BindingErrorCorrelationE2eTests
{
    private readonly McpE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BindingErrorCorrelationE2eTests(McpE2eFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task GetBindingErrors_ShouldProvideActionableElementCorrelation()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_binding_errors",
            new { processId = _fixture.TestAppProcessId });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.ValueKind.Should().Be(JsonValueKind.Array);

        string? actionableElementId = null;
        JsonElement? actionableError = null;
        foreach (var error in errors.EnumerateArray())
        {
            if (error.TryGetProperty("elementId", out var elementIdProperty) &&
                elementIdProperty.ValueKind == JsonValueKind.String)
            {
                actionableElementId = elementIdProperty.GetString();
                actionableError = error;
                break;
            }

            if (error.TryGetProperty("suggestedElementId", out var suggestedElementIdProperty) &&
                suggestedElementIdProperty.ValueKind == JsonValueKind.String)
            {
                actionableElementId = suggestedElementIdProperty.GetString();
                actionableError = error;
                break;
            }
        }

        actionableElementId.Should().NotBeNullOrWhiteSpace("binding errors should now point to a specific or suggested element target");
        var bindings = await _fixture.Client.CallToolAsync(
            "get_bindings",
            new { processId = _fixture.TestAppProcessId, elementId = actionableElementId });

        _output.WriteLine($"Actionable error: {E2eTestHelpers.Truncate(actionableError?.GetRawText() ?? string.Empty, 500)}");
        _output.WriteLine($"Bindings for actionable element: {E2eTestHelpers.Truncate(bindings.GetRawText(), 500)}");

        bindings.GetProperty("success").GetBoolean().Should().BeTrue();
        bindings.GetProperty("bindings").GetArrayLength().Should().BeGreaterThan(0);
    }
}
