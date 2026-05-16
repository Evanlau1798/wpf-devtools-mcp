using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.McpServer;

public partial class ToolCallHelperTests
{
    // === BuildJsonArgs Tests ===

    [Fact]
    public void BuildJsonArgs_WithNoParameters_ShouldReturnNull()
    {
        var result = ToolCallHelper.BuildJsonArgs();

        result.Should().BeNull();
    }

    [Fact]
    public void BuildJsonArgs_WithAllNullValues_ShouldReturnNull()
    {
        var result = ToolCallHelper.BuildJsonArgs(
            ("processId", null),
            ("elementId", null));

        result.Should().BeNull();
    }

    [Fact]
    public void BuildJsonArgs_WithSingleParameter_ShouldReturnJsonElement()
    {
        var result = ToolCallHelper.BuildJsonArgs(("processId", 12345));

        result.Should().NotBeNull();
        result!.Value.TryGetProperty("processId", out var pid).Should().BeTrue();
        pid.GetInt32().Should().Be(12345);
    }

    [Fact]
    public void BuildJsonArgs_WithMultipleParameters_ShouldIncludeAll()
    {
        var result = ToolCallHelper.BuildJsonArgs(
            ("processId", 12345),
            ("elementId", "Button_1"),
            ("depth", 5));

        result.Should().NotBeNull();
        var json = result!.Value;
        json.TryGetProperty("processId", out var pid).Should().BeTrue();
        pid.GetInt32().Should().Be(12345);
        json.TryGetProperty("elementId", out var eid).Should().BeTrue();
        eid.GetString().Should().Be("Button_1");
        json.TryGetProperty("depth", out var depth).Should().BeTrue();
        depth.GetInt32().Should().Be(5);
    }

    [Fact]
    public void CachedTool_WithoutTestScope_ShouldReuseProcessWideInstance()
    {
        var cacheKey = $"global-cache-{Guid.NewGuid():N}";

        var first = ToolCallHelper.CachedTool<object>(cacheKey, static () => new object());
        var second = ToolCallHelper.CachedTool<object>(cacheKey, static () => new object());

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void CachedTool_WhenNestedTestScopeDisposes_ShouldRestoreOuterScopedCache()
    {
        var cacheKey = $"scoped-cache-{Guid.NewGuid():N}";
        using var outerScope = ToolCallHelper.BeginTestScope();
        var outerInstance = ToolCallHelper.CachedTool<object>(cacheKey, static () => new object());

        using (ToolCallHelper.BeginTestScope())
        {
            var innerInstance = ToolCallHelper.CachedTool<object>(cacheKey, static () => new object());
            innerInstance.Should().NotBeSameAs(outerInstance);
        }

        var restoredOuterInstance = ToolCallHelper.CachedTool<object>(cacheKey, static () => new object());
        restoredOuterInstance.Should().BeSameAs(outerInstance);
    }

    [Fact]
    public void CachedTool_WhenIndependentTestScopesUseSameKey_ShouldNotShareInstances()
    {
        var cacheKey = $"independent-scope-{Guid.NewGuid():N}";
        object firstScopeInstance;
        object secondScopeInstance;

        using (ToolCallHelper.BeginTestScope())
        {
            firstScopeInstance = ToolCallHelper.CachedTool<object>(cacheKey, static () => new object());
            ToolCallHelper.CachedTool<object>(cacheKey, static () => new object()).Should().BeSameAs(firstScopeInstance);
        }

        using (ToolCallHelper.BeginTestScope())
        {
            secondScopeInstance = ToolCallHelper.CachedTool<object>(cacheKey, static () => new object());
            ToolCallHelper.CachedTool<object>(cacheKey, static () => new object()).Should().BeSameAs(secondScopeInstance);
        }

        secondScopeInstance.Should().NotBeSameAs(firstScopeInstance);
    }

    [Fact]
    public void CachedTool_WithDifferentSessionManagers_ShouldNotShareHostScopedInstances()
    {
        var cacheKey = $"host-scoped-cache-{Guid.NewGuid():N}";
        using var firstSessionManager = new SessionManager();
        using var secondSessionManager = new SessionManager();

        var firstInstance = ToolCallHelper.CachedTool<SessionBoundProbeTool>(
            firstSessionManager,
            cacheKey,
            () => new SessionBoundProbeTool(firstSessionManager));
        var secondInstance = ToolCallHelper.CachedTool<SessionBoundProbeTool>(
            secondSessionManager,
            cacheKey,
            () => new SessionBoundProbeTool(secondSessionManager));
        var secondAgain = ToolCallHelper.CachedTool<SessionBoundProbeTool>(
            secondSessionManager,
            cacheKey,
            () => new SessionBoundProbeTool(secondSessionManager));

        secondInstance.Should().NotBeSameAs(firstInstance);
        secondInstance.SessionManager.Should().BeSameAs(secondSessionManager);
        secondAgain.Should().BeSameAs(secondInstance);
    }

    [Fact]
    public void McpWrappers_WithSessionManagerBoundTools_ShouldUseHostScopedCachedToolOverload()
    {
        var mcpToolsDirectory = Path.GetDirectoryName(TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Mcp.Server/McpTools/ProcessMcpTools.cs"))!;
        var violations = Directory.EnumerateFiles(mcpToolsDirectory, "*.cs")
            .Where(path => !Path.GetFileName(path).StartsWith("ToolCallHelper", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { path, line, lineNumber = index + 1 }))
            .Where(entry => entry.line.Contains("ToolCallHelper.CachedTool<", StringComparison.Ordinal))
            .Where(entry => !entry.line.Contains("(sessionManager,", StringComparison.Ordinal))
            .Where(entry => !entry.line.Contains("ToolCallHelper.CachedTool<GetProcessesTool>(", StringComparison.Ordinal))
            .Select(entry => $"{Path.GetFileName(entry.path)}:{entry.lineNumber}: {entry.line.Trim()}")
            .ToArray();

        violations.Should().BeEmpty(
            "wrappers that cache SessionManager-bound tools must use the host-scoped overload; GetProcessesTool is the only process-wide exception");
    }

    [Fact]
    public void BuildJsonArgs_WithMixedNullAndNonNull_ShouldExcludeNulls()
    {
        var result = ToolCallHelper.BuildJsonArgs(
            ("processId", 12345),
            ("elementId", null),
            ("depth", 3));

        result.Should().NotBeNull();
        var json = result!.Value;
        json.TryGetProperty("processId", out _).Should().BeTrue();
        json.TryGetProperty("elementId", out _).Should().BeFalse();
        json.TryGetProperty("depth", out _).Should().BeTrue();
    }

    [Fact]
    public void BuildJsonArgs_WithStringParameter_ShouldSerializeCorrectly()
    {
        var result = ToolCallHelper.BuildJsonArgs(("nameFilter", "TestApp"));

        result.Should().NotBeNull();
        result!.Value.TryGetProperty("nameFilter", out var nf).Should().BeTrue();
        nf.GetString().Should().Be("TestApp");
    }

    [Fact]
    public void BuildJsonArgs_WithBooleanFalse_ShouldIncludeIt()
    {
        var result = ToolCallHelper.BuildJsonArgs(("recursive", false));

        result.Should().NotBeNull();
        result!.Value.TryGetProperty("recursive", out var val).Should().BeTrue();
        val.GetBoolean().Should().BeFalse();
    }

    private sealed class SessionBoundProbeTool(SessionManager sessionManager)
    {
        public SessionManager SessionManager { get; } = sessionManager;
    }
}
