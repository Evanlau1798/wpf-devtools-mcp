using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// Shared helper methods for E2E tests.
/// Avoids code duplication across test classes (DRY principle).
/// </summary>
public static class E2eTestHelpers
{
    /// <summary>
    /// Assert the E2E fixture is ready and connected.
    /// Fails with a clear message if prerequisites are missing.
    /// </summary>
    public static void AssertFixtureReady(McpE2eFixture fixture)
    {
        fixture.SkipReason.Should().BeNull(
            $"E2E fixture must be available. Skip reason: {fixture.SkipReason}");
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

    /// <summary>
    /// Truncate a string for safe inclusion in log/error messages.
    /// </summary>
    public static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value.Substring(0, maxLength) + "...";
    }
}
