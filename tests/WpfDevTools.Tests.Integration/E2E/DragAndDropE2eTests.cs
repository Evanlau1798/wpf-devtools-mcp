using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class DragAndDropE2eTests
{
    private readonly McpE2eFixture _fixture;

    public DragAndDropE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DragAndDrop_ShouldReportTargetHandlerHints()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var tabSearch = await _fixture.Client.CallToolAsync(
            "find_elements",
            new
            {
                processId = _fixture.TestAppProcessId,
                typeName = "TabItem",
                propertyName = "Header",
                propertyValue = "Drag & Drop"
            });
        var tabId = tabSearch.GetProperty("results")[0].GetProperty("elementId").GetString();
        tabId.Should().NotBeNull();

        var activateTab = await _fixture.Client.CallToolAsync(
            "click_element",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = tabId
            });
        activateTab.GetProperty("success").GetBoolean().Should().BeTrue();

        var sourceId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client, _fixture.TestAppProcessId, "DragSourceTextBox");
        var targetId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client, _fixture.TestAppProcessId, "DropTargetTextBox");
        sourceId.Should().NotBeNull();
        targetId.Should().NotBeNull();

        var result = await _fixture.Client.CallToolAsync(
            "drag_and_drop",
            new
            {
                processId = _fixture.TestAppProcessId,
                sourceElementId = sourceId,
                targetElementId = targetId,
                dataFormat = "Text"
            });
        var hints = result.GetProperty("targetHandlerHints");

        result.GetProperty("success").GetBoolean().Should().BeTrue(result.GetRawText());
        hints.GetProperty("targetAllowsDrop").GetBoolean().Should().BeTrue();
        hints.GetProperty("hasDropHandler").GetBoolean().Should().BeTrue();
        hints.GetProperty("hasDragOverHandler").GetBoolean().Should().BeTrue();
        hints.GetProperty("hasAnyDropOrDragOverHandler").GetBoolean().Should().BeTrue();
    }
}
