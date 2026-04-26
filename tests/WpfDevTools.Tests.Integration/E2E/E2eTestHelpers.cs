using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Integration.TestSupport;

namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// Shared helper methods for E2E tests.
/// Avoids code duplication across test classes (DRY principle).
/// </summary>
public static class E2eTestHelpers
{
    internal delegate Task<JsonElement> ToolCallAsync(string toolName, object? arguments);

    private const string BasicControlsTabName = "BasicControlsTab";
    private const string ResetCommandTargetName = "NameTextBox";
    private const string ResetStateCommandName = "ResetStateCommand";
    private const int ResetEventDrainMaxEvents = 200;
    private const int ResetEventDrainMaxPasses = 10;

    /// <summary>
    /// Assert the E2E fixture is ready and connected.
    /// Fails with a clear message if prerequisites are missing.
    /// </summary>
    public static void AssertFixtureReady(McpE2eFixture fixture)
    {
        fixture.SkipReason.Should().BeNull(
            $"E2E fixture must be available. Skip reason: {fixture.SkipReason}");
        fixture.QuarantineReason.Should().BeNull(
            $"E2E fixture must not be quarantined. Quarantine reason: {fixture.QuarantineReason}");
    }

    /// <summary>
    /// Enumerate property names from a JSON object element.
    /// </summary>
    public static IEnumerable<string> EnumeratePropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
                yield return prop.Name;
        }
    }

    /// <summary>
    /// Search a visual/logical tree for an element matching a type name.
    /// Tree nodes use "type" (not "typeName") and "elementId" properties.
    /// </summary>
    public static string? SearchTreeForType(JsonElement node, string typeName)
    {
        if (node.TryGetProperty("type", out var name) &&
            name.GetString()?.Contains(typeName, StringComparison.OrdinalIgnoreCase) == true)
        {
            return node.TryGetProperty("elementId", out var id) ? id.GetString() : null;
        }

        if (node.TryGetProperty("children", out var children) &&
            children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                var found = SearchTreeForType(child, typeName);
                if (found != null) return found;
            }
        }

        return null;
    }

    public static string? SearchTreeForName(JsonElement node, string elementName)
    {
        if (node.TryGetProperty("name", out var name) &&
            string.Equals(name.GetString(), elementName, StringComparison.Ordinal))
        {
            return node.TryGetProperty("elementId", out var id) ? id.GetString() : null;
        }

        if (node.TryGetProperty("children", out var children) &&
            children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                var found = SearchTreeForName(child, elementName);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Find an element by type name in the visual tree via MCP protocol.
    /// Unwraps the "tree" wrapper from the response before searching.
    /// </summary>
    public static async Task<string?> FindElementByTypeAsync(
        McpStdioClient client, int processId, string typeName)
    {
        // WPF visual tree is deep: Window > Border > AdornerDecorator > ContentPresenter >
        // Grid > TabControl > TabItem > ContentPresenter > StackPanel > Button/TextBox
        // Need depth >= 10 to reach control content inside TabControl
        var response = await client.CallToolAsync(
            "get_visual_tree",
            new { processId, depth = 15 });

        if (!response.GetProperty("success").GetBoolean())
            return null;

        // Tree responses wrap the root node under "tree" property
        if (!response.TryGetProperty("tree", out var tree))
            return null;

        return SearchTreeForType(tree, typeName);
    }

    public static async Task<string?> FindElementByNameAsync(
        McpStdioClient client, int processId, string elementName)
    {
        var response = await client.CallToolAsync(
            "get_namescope",
            new { processId });

        if (!response.GetProperty("success").GetBoolean() ||
            !response.TryGetProperty("namedElements", out var namedElements))
        {
            return null;
        }

        var match = namedElements.EnumerateArray()
            .FirstOrDefault(item => string.Equals(item.GetProperty("name").GetString(), elementName, StringComparison.Ordinal));
        if (match.ValueKind != JsonValueKind.Undefined)
        {
            return match.GetProperty("elementId").GetString();
        }

        var treeResponse = await client.CallToolAsync(
            "get_visual_tree",
            new { processId, depth = 15 });
        if (!treeResponse.GetProperty("success").GetBoolean() ||
            !treeResponse.TryGetProperty("tree", out var tree))
        {
            return null;
        }

        return SearchTreeForName(tree, elementName);
    }

    public static async Task<string?> WaitForElementByNameAsync(
        McpStdioClient client,
        int processId,
        string elementName,
        int attempts = 10,
        int delayMs = 100)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var elementId = await FindElementByNameAsync(client, processId, elementName);
            if (!string.IsNullOrWhiteSpace(elementId))
            {
                return elementId;
            }

            await Task.Delay(delayMs);
        }

        return null;
    }

    public static async Task<JsonElement> WaitForTraceEventAsync(
        McpStdioClient client,
        int processId,
        TimeSpan timeout)
    {
        return await ConditionWaiter.WaitForAsync(
            () => client.CallToolAsync(
                "trace_routed_events",
                new
                {
                    processId,
                    mode = "get"
                }),
            result => result.GetProperty("eventCount").GetInt32() > 0,
            timeout,
            "Timed out waiting for trace_routed_events(mode='get') to observe a routed event.");
    }

    public static async Task ResetTestAppStateAsync(McpStdioClient client, int processId)
    {
        var basicControlsTabId = await FindElementByNameAsync(client, processId, BasicControlsTabName);
        if (string.IsNullOrWhiteSpace(basicControlsTabId))
        {
            throw new InvalidOperationException($"Could not find {BasicControlsTabName} while resetting shared E2E state.");
        }

        var resetTargetId = await FindElementByNameAsync(client, processId, ResetCommandTargetName);
        if (string.IsNullOrWhiteSpace(resetTargetId))
        {
            throw new InvalidOperationException($"Could not find {ResetCommandTargetName} while resetting shared E2E state.");
        }

        var activateResult = await client.CallToolAsync(
            "click_element",
            new
            {
                processId,
                elementId = basicControlsTabId,
                navigation = false
            });
        EnsureToolSucceeded(activateResult, "click_element", BasicControlsTabName);

        var resetResult = await client.CallToolAsync(
            "execute_command",
            new
            {
                processId,
                elementId = resetTargetId,
                commandName = ResetStateCommandName,
                navigation = false
            });
        EnsureToolSucceeded(resetResult, "execute_command", ResetStateCommandName);

        await DrainPendingEventsUntilEmptyAsync(() => client.CallToolAsync(
            "drain_events",
            new
            {
                processId,
                maxEvents = ResetEventDrainMaxEvents
            }));
    }

    public static async Task ResetSharedSessionStateAsync(McpE2eFixture fixture)
    {
        await fixture.ReconnectClientAsync();
        await ResetTestAppStateAsync(fixture.Client, fixture.TestAppProcessId);
    }

    internal static Task RunWithRestoredSnapshotAsync(
        McpStdioClient client,
        int processId,
        string snapshotId,
        Func<Task> bodyAsync)
    {
        return RunWithRestoredSnapshotAsync(
            (toolName, arguments) => client.CallToolAsync(toolName, arguments),
            processId,
            snapshotId,
            bodyAsync);
    }

    internal static async Task RunWithRestoredSnapshotAsync(
        ToolCallAsync callToolAsync,
        int processId,
        string snapshotId,
        Func<Task> bodyAsync)
    {
        try
        {
            await bodyAsync();
        }
        finally
        {
            var restore = await RestoreStateSnapshotAsync(callToolAsync, processId, snapshotId);
            EnsureToolSucceeded(restore, "restore_state_snapshot", snapshotId);
        }
    }

    internal static Task<JsonElement> RestoreStateSnapshotAsync(
        McpStdioClient client,
        int processId,
        string snapshotId,
        bool removeAfterRestore = true)
    {
        return RestoreStateSnapshotAsync(
            (toolName, arguments) => client.CallToolAsync(toolName, arguments),
            processId,
            snapshotId,
            removeAfterRestore);
    }

    internal static Task<JsonElement> RestoreStateSnapshotAsync(
        ToolCallAsync callToolAsync,
        int processId,
        string snapshotId,
        bool removeAfterRestore = true)
    {
        return callToolAsync(
            "restore_state_snapshot",
            new
            {
                processId,
                snapshotId,
                removeAfterRestore
            });
    }

    /// <summary>
    /// Truncate a string for safe inclusion in log/error messages.
    /// </summary>
    public static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value.Substring(0, maxLength) + "...";
    }

    internal static void EnsureToolSucceeded(JsonElement result, string toolName, string target)
    {
        if (!result.TryGetProperty("success", out var success) || !success.GetBoolean())
        {
            throw new InvalidOperationException(
                $"{toolName} failed while resetting {target}: {result.GetRawText()}");
        }

        if (result.TryGetProperty("cleanupIncomplete", out var cleanupIncomplete) &&
            cleanupIncomplete.ValueKind == JsonValueKind.True)
        {
            var cleanupFailureMessage = result.TryGetProperty("cleanupFailureMessage", out var failureMessage)
                && failureMessage.ValueKind == JsonValueKind.String
                    ? failureMessage.GetString()
                    : null;
            var cleanupFailureType = result.TryGetProperty("cleanupFailureType", out var failureType)
                && failureType.ValueKind == JsonValueKind.String
                    ? failureType.GetString()
                    : null;

            throw new InvalidOperationException(
                $"{toolName} reported cleanupIncomplete while resetting {target}: " +
                $"{cleanupFailureType ?? "UnknownCleanupFailure"}: {cleanupFailureMessage ?? result.GetRawText()}");
        }
    }

    internal static async Task DrainPendingEventsUntilEmptyAsync(Func<Task<JsonElement>> drainEventsAsync)
    {
        JsonElement lastResult = default;
        for (var attempt = 1; attempt <= ResetEventDrainMaxPasses; attempt++)
        {
            lastResult = await drainEventsAsync();
            EnsureToolSucceeded(lastResult, "drain_events", "pending event queue");

            if (!lastResult.TryGetProperty("pendingEventCount", out var pendingEventCount) ||
                pendingEventCount.ValueKind != JsonValueKind.Number ||
                pendingEventCount.GetInt32() == 0)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"drain_events did not empty the pending event queue after {ResetEventDrainMaxPasses} reset passes: {lastResult.GetRawText()}");
    }
}
