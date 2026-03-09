using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// E2E tests for MCP interaction tools (click_element, simulate_keyboard, element_screenshot).
/// Validates user interaction simulation through the full MCP protocol pipeline.
/// </summary>
[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class InteractionE2eTests
{
    private readonly McpE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public InteractionE2eTests(McpE2eFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task ElementScreenshot_OnRoot_ShouldReturnBase64Image()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "element_screenshot",
            new { processId = _fixture.TestAppProcessId });

        _output.WriteLine($"Screenshot result keys: {string.Join(", ", E2eTestHelpers.EnumeratePropertyNames(result))}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("base64Image", out var image).Should().BeTrue(
            "screenshot should include base64-encoded PNG data");
        image.GetString().Should().NotBeNullOrEmpty();

        if (result.TryGetProperty("width", out var width) &&
            result.TryGetProperty("height", out var height))
        {
            width.GetInt32().Should().BeGreaterThan(0);
            height.GetInt32().Should().BeGreaterThan(0);
            _output.WriteLine($"Screenshot size: {width.GetInt32()}x{height.GetInt32()}");
        }
    }

    [Fact]
    public async Task ClickElement_OnButton_ShouldSucceed()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var buttonElementId = await E2eTestHelpers.FindElementByTypeAsync(
            _fixture.Client, _fixture.TestAppProcessId, "Button");

        buttonElementId.Should().NotBeNull(
            "TestApp should contain at least one Button element in its visual tree");

        var result = await _fixture.Client.CallToolAsync(
            "click_element",
            new { processId = _fixture.TestAppProcessId, elementId = buttonElementId });

        _output.WriteLine($"click_element result: {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue(
            "clicking a button should succeed");
    }

    [Fact]
    public async Task SimulateKeyboard_OnTextBox_ShouldSucceed()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var textBoxId = await E2eTestHelpers.FindElementByTypeAsync(
            _fixture.Client, _fixture.TestAppProcessId, "TextBox");

        textBoxId.Should().NotBeNull(
            "TestApp should contain at least one TextBox element in its visual tree");

        var result = await _fixture.Client.CallToolAsync(
            "simulate_keyboard",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxId,
                key = "A",
                eventType = "KeyDown"
            });

        _output.WriteLine($"simulate_keyboard result: {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue(
            "keyboard simulation should succeed on a TextBox");
    }

    [Fact]
    public async Task GetLayoutInfo_ShouldReturnElementDimensions()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_layout_info",
            new { processId = _fixture.TestAppProcessId });

        _output.WriteLine($"Layout info: {E2eTestHelpers.Truncate(result.GetRawText(), 500)}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
