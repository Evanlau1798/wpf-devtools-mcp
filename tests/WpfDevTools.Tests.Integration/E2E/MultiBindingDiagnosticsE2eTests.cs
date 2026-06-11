using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class MultiBindingDiagnosticsE2eTests
{
    private readonly McpE2eFixture _fixture;

    public MultiBindingDiagnosticsE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
        E2eTestHelpers.AssertFixtureReady(_fixture);
    }

    [Fact]
    public async Task GetBindings_OnMultiBindingTextBlock_ShouldReturnMultiBindingDetails()
    {
        var searchResult = await _fixture.Client.CallToolAsync(
            "find_elements",
            new
            {
                elementName = "MultiBindingTextBlock",
                maxResults = 1
            });

        searchResult.GetProperty("success").GetBoolean().Should().BeTrue();
        var elementId = searchResult.GetProperty("results")[0].GetProperty("elementId").GetString();
        elementId.Should().NotBeNullOrWhiteSpace();

        var bindingResult = await _fixture.Client.CallToolAsync(
            "get_bindings",
            new
            {
                elementId
            });

        bindingResult.GetProperty("success").GetBoolean().Should().BeTrue();
        var binding = bindingResult.GetProperty("bindings")[0];
        binding.GetProperty("bindingType").GetString().Should().Be("MultiBinding");
        binding.GetProperty("converter").GetString().Should().Be("ConcatMultiConverter");
        binding.GetProperty("currentValue").GetString().Should().Be("Ada Lovelace");
        binding.GetProperty("bindingPaths").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("FirstName", "LastName");
    }

    [Fact]
    public async Task GetBindingValueChain_OnMultiBindingTextBlock_ShouldReturnMultiBindingResolutionChain()
    {
        var searchResult = await _fixture.Client.CallToolAsync(
            "find_elements",
            new
            {
                elementName = "MultiBindingTextBlock",
                maxResults = 1
            });

        searchResult.GetProperty("success").GetBoolean().Should().BeTrue();
        var elementId = searchResult.GetProperty("results")[0].GetProperty("elementId").GetString();
        elementId.Should().NotBeNullOrWhiteSpace();

        var chainResult = await _fixture.Client.CallToolAsync(
            "get_binding_value_chain",
            new
            {
                elementId,
                propertyName = "Text"
            });

        chainResult.GetProperty("success").GetBoolean().Should().BeTrue(chainResult.GetRawText());
        chainResult.GetProperty("hasBinding").GetBoolean().Should().BeTrue(chainResult.GetRawText());
        chainResult.GetProperty("chain").EnumerateArray()
            .Any(step =>
                step.TryGetProperty("bindingType", out var bindingType)
                && bindingType.GetString() == "MultiBinding")
            .Should().BeTrue(chainResult.GetRawText());
        chainResult.GetProperty("chain").EnumerateArray()
            .SelectMany(step =>
                step.TryGetProperty("bindingPaths", out var bindingPaths)
                    ? bindingPaths.EnumerateArray().Select(item => item.GetString())
                    : Array.Empty<string?>())
            .Should().Contain(new[] { "FirstName", "LastName" });
        chainResult.GetProperty("chain").EnumerateArray()
            .Any(step =>
                step.TryGetProperty("step", out var stepName)
                && stepName.GetString() == "FinalValue"
                && step.TryGetProperty("value", out var value)
                && value.GetString() == "Ada Lovelace")
            .Should().BeTrue(chainResult.GetRawText());
    }
}
