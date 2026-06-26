using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("VisibilityMcpE2E")]
[Trait("Category", "E2E")]
public sealed class VisibilityDiagnosisE2eTests
{
    private readonly McpE2eFixture _fixture;
    private string? _layoutTransformsTabId;

    public VisibilityDiagnosisE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DiagnoseVisibility_ShouldReportAncestorVisibilityBlocker()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        await SelectLayoutTransformsTabAsync();
        var result = await DiagnoseVisibilityAsync("HiddenByAncestorText");

        Assert.True(result.GetProperty("success").GetBoolean(), result.GetRawText());
        result.GetProperty("isUserVisible").GetBoolean().Should().BeFalse();
        result.GetProperty("rootCause").GetString().Should().Contain("HiddenByAncestorPanel");
    }

    [Fact]
    public async Task DiagnoseVisibility_ShouldReportPartialClippingAsVisibleWithMetadata()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        await SelectLayoutTransformsTabAsync();
        var result = await DiagnoseVisibilityAsync("ClippingTextSample");

        Assert.True(result.GetProperty("success").GetBoolean(), result.GetRawText());
        result.GetProperty("isUserVisible").GetBoolean().Should().BeTrue(result.GetRawText());
        result.GetProperty("rootCause").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        var clipping = result.GetProperty("clipping");
        clipping.GetProperty("severity").GetString().Should().Be("partial");
        clipping.GetProperty("isClipped").GetBoolean().Should().BeTrue();
        clipping.GetProperty("isFullyClipped").GetBoolean().Should().BeFalse();
        clipping.GetProperty("visibleRatio").GetDouble().Should().BeGreaterThan(0).And.BeLessThan(1);
    }

    [Fact]
    public async Task DiagnoseVisibility_ShouldReportOffscreenRenderTransformBlocker()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        await SelectLayoutTransformsTabAsync();
        var result = await DiagnoseVisibilityAsync("OffscreenTranslatedButtonSample");

        Assert.True(result.GetProperty("success").GetBoolean(), result.GetRawText());
        result.GetProperty("isUserVisible").GetBoolean().Should().BeFalse();
        result.GetProperty("rootCause").GetString().Should().Contain("RenderTransform");
        result.GetProperty("suggestedFix").GetString().Should().Contain("RenderTransform");
    }

    private async Task SelectLayoutTransformsTabAsync()
    {
        await SelectLayoutTransformsTabAsync(retryElementLookup: true);
    }

    private async Task<string?> FindElementIdAsync(string elementName, bool allowTabRefresh = true)
    {
        var renderedElementId = await FindRenderedElementIdInLayoutTabAsync(elementName);
        if (renderedElementId != null)
        {
            return renderedElementId;
        }

        if (!allowTabRefresh)
        {
            return await FindNamedElementIdAsync(elementName);
        }

        await SelectLayoutTransformsTabAsync(retryElementLookup: false);
        renderedElementId = await FindRenderedElementIdInLayoutTabAsync(elementName);
        if (renderedElementId != null)
        {
            return renderedElementId;
        }

        return await FindNamedElementIdAsync(elementName);
    }

    private async Task<string?> FindNamedElementIdAsync(string elementName)
    {
        var namescopeResult = await _fixture.Client.CallToolAsync(
            "get_namescope",
            new
            {
                processId = _fixture.TestAppProcessId
            });

        if (!namescopeResult.TryGetProperty("namedElements", out var namedElements))
        {
            return null;
        }

        var nameMatch = namedElements.EnumerateArray()
            .FirstOrDefault(item => string.Equals(item.GetProperty("name").GetString(), elementName, StringComparison.Ordinal));
        return nameMatch.ValueKind != System.Text.Json.JsonValueKind.Undefined
            ? nameMatch.GetProperty("elementId").GetString()
            : null;
    }

    private async Task<System.Text.Json.JsonElement> DiagnoseVisibilityAsync(string elementName)
    {
        var runtimeElementId = await FindRenderedElementIdInLayoutTabAsync(elementName)
            ?? await FindElementIdAsync(elementName);
        var result = await CallDiagnoseVisibilityAsync(runtimeElementId);
        if (result.GetProperty("success").GetBoolean())
        {
            return result;
        }

        await SelectLayoutTransformsTabAsync(retryElementLookup: false);
        runtimeElementId = await FindRenderedElementIdInLayoutTabAsync(elementName)
            ?? await FindElementIdAsync(elementName);
        return await CallDiagnoseVisibilityAsync(runtimeElementId);
    }

    private Task<System.Text.Json.JsonElement> CallDiagnoseVisibilityAsync(string? runtimeElementId)
    {
        return _fixture.Client.CallToolAsync(
            "diagnose_visibility",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = runtimeElementId
            });
    }

    private async Task SelectLayoutTransformsTabAsync(bool retryElementLookup)
    {
        var layoutTabId = await FindLayoutTransformsTabIdAsync(retryElementLookup);
        var clickResult = await _fixture.Client.CallToolAsync(
            "click_element",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = layoutTabId
            });

        Assert.True(clickResult.GetProperty("success").GetBoolean(), clickResult.GetRawText());
    }

    private async Task<string?> FindLayoutTransformsTabIdAsync(bool retryElementLookup)
    {
        _layoutTransformsTabId ??= await FindNamedElementIdAsync("LayoutTransformsTab");
        var tabId = _layoutTransformsTabId;
        if (tabId != null || !retryElementLookup)
        {
            return tabId;
        }

        _layoutTransformsTabId = await FindNamedElementIdAsync("LayoutTransformsTab");
        return _layoutTransformsTabId;
    }

    private async Task<string?> FindRenderedElementIdInLayoutTabAsync(string elementName)
    {
        var layoutTabId = await FindLayoutTransformsTabIdAsync(retryElementLookup: false);
        if (layoutTabId == null)
        {
            return null;
        }

        var treeResult = await _fixture.Client.CallToolAsync(
            "get_visual_tree",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = layoutTabId,
                depth = 12
            });

        if (!treeResult.TryGetProperty("tree", out var tree))
        {
            return null;
        }

        return SearchTreeForName(tree, elementName);
    }

    private static string? SearchTreeForName(System.Text.Json.JsonElement node, string targetName)
    {
        if (node.TryGetProperty("name", out var name) &&
            string.Equals(name.GetString(), targetName, StringComparison.Ordinal) &&
            node.TryGetProperty("elementId", out var elementId))
        {
            return elementId.GetString();
        }

        if (!node.TryGetProperty("children", out var children) ||
            children.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return null;
        }

        foreach (var child in children.EnumerateArray())
        {
            var found = SearchTreeForName(child, targetName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
