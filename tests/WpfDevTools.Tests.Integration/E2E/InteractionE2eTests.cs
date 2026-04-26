using System.Linq;
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
public sealed partial class InteractionE2eTests : SharedStateMcpE2eTestBase
{
    private readonly McpE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public InteractionE2eTests(McpE2eFixture fixture, ITestOutputHelper output)
        : base(fixture)
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
        await ActivateBasicControlsTabAsync();

        var buttonElementId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client, _fixture.TestAppProcessId, "SaveButton");

        buttonElementId.Should().NotBeNull(
            "TestApp should expose SaveButton through the root namescope");

        var result = await _fixture.Client.CallToolAsync(
            "click_element",
            new { processId = _fixture.TestAppProcessId, elementId = buttonElementId });

        _output.WriteLine($"click_element result: {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue(
            "clicking a button should succeed");
    }

    [Fact]
    public async Task ClickElement_AfterSnapshotCapture_ShouldExposeMutationSessionContextRef()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);
        await ActivateBasicControlsTabAsync();

        var buttonElementId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client, _fixture.TestAppProcessId, "SaveButton");
        buttonElementId.Should().NotBeNull("TestApp should expose SaveButton through the root namescope");

        var capture = await _fixture.Client.CallToolAsync(
            "capture_state_snapshot",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = buttonElementId,
                propertyNames = new[] { "IsEnabled" }
            });
        capture.GetProperty("success").GetBoolean().Should().BeTrue();

        var click = await _fixture.Client.CallToolAsync(
            "click_element",
            new { processId = _fixture.TestAppProcessId, elementId = buttonElementId });

        click.GetProperty("success").GetBoolean().Should().BeTrue();
        var navigation = click.GetProperty("navigation");
        navigation.GetProperty("contextRefs")[0].GetProperty("type").GetString().Should().Be("mutation-session");
        navigation.GetProperty("prefetchTools").EnumerateArray().Select(item => item.GetString()).Should().Contain("restore_state_snapshot");
    }

    [Fact]
    public async Task SimulateKeyboard_OnTextBox_ShouldSucceed()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);
        await ActivateBasicControlsTabAsync();

        var textBoxId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client, _fixture.TestAppProcessId, "NameTextBox");

        textBoxId.Should().NotBeNull(
            "TestApp should expose NameTextBox through the root namescope");

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
    public async Task SimulateKeyboard_OnTextBoxEnter_ShouldInvokeDefaultButtonSemanticAction()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);
        await ActivateBasicControlsTabAsync();

        var textBoxId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "FocusNextTextBox");
        textBoxId.Should().NotBeNull("TestApp should expose FocusNextTextBox through the root namescope");

        var focusResult = await _fixture.Client.CallToolAsync(
            "focus_element",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxId
            });
        focusResult.GetProperty("success").GetBoolean().Should().BeTrue();

        var result = await _fixture.Client.CallToolAsync(
            "simulate_keyboard",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxId,
                key = "Enter",
                eventType = "KeyDown"
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("semanticEffectObserved").GetBoolean().Should().BeTrue();

        var viewModel = await _fixture.Client.CallToolAsync(
            "get_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyNames = new[] { "LastActionMessage" }
            });
        viewModel.GetProperty("success").GetBoolean().Should().BeTrue();
        viewModel.GetProperty("properties").EnumerateArray()
            .Single(property => property.GetProperty("name").GetString() == "LastActionMessage")
            .GetProperty("value").GetString().Should().Be("Focus action invoked");
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

    private async Task ActivateBasicControlsTabAsync()
    {
        var tabId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "BasicControlsTab");
        tabId.Should().NotBeNull("TestApp should expose BasicControlsTab through the root namescope");

        var result = await _fixture.Client.CallToolAsync(
            "click_element",
            new { processId = _fixture.TestAppProcessId, elementId = tabId, navigation = false });
        result.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
