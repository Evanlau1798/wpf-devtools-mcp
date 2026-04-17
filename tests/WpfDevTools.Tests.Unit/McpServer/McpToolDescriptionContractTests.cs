using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Pins AI-agent-facing description contracts for sensitive tools so future
/// refactors cannot silently regress tool titles, prominence of transport
/// limitations, or rollback guidance that agents rely on.
/// </summary>
public sealed class McpToolDescriptionContractTests
{
    private static readonly Assembly McpServerAssembly = typeof(ServerInstructions).Assembly;

    [Theory]
    [InlineData("click_element", "Click WPF Element")]
    public void ToolTitle_ShouldBeSpecificAndActionable(string toolName, string expectedTitle)
    {
        var attr = FindToolAttribute(toolName);
        attr.Title.Should().Be(expectedTitle,
            $"AI agents pick tools by Title; '{toolName}' must advertise a specific verb+noun phrase");
    }

    [Fact]
    public void WatchDpChanges_Description_ShouldAnnounceStdioLimitationBeforeOtherProse()
    {
        var description = GetDescriptionText("watch_dp_changes");
        var limitationIndex = description.IndexOf("STDIO", StringComparison.OrdinalIgnoreCase);
        var useIndex = description.IndexOf("Use this tool", StringComparison.OrdinalIgnoreCase);

        limitationIndex.Should().BeGreaterThan(-1, "STDIO limitation must be mentioned");
        limitationIndex.Should().BeLessThan(useIndex,
            "STDIO limitation must appear before 'Use this tool' so AI agents see it first");
    }

    [Fact]
    public void BatchMutate_Description_ShouldDocumentRestoreStateSnapshotRecoveryPath()
    {
        var description = GetDescriptionText("batch_mutate");
        description.Should().Contain("restore_state_snapshot",
            "AI agents must know which tool reverses a partial failure");
        description.Should().Contain("FAILURE RECOVERY",
            "Recovery guidance must be clearly labelled so agents can index it");
    }

    private static McpServerToolAttribute FindToolAttribute(string toolName)
    {
        foreach (var type in McpServerAssembly.GetTypes()
                     .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr is not null && string.Equals(attr.Name, toolName, StringComparison.Ordinal))
                {
                    return attr;
                }
            }
        }

        throw new InvalidOperationException($"Tool '{toolName}' not found among registered [McpServerTool] methods.");
    }

    private static string GetDescriptionText(string toolName)
    {
        foreach (var type in McpServerAssembly.GetTypes()
                     .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr is null || !string.Equals(attr.Name, toolName, StringComparison.Ordinal))
                {
                    continue;
                }

                var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
                return description ?? string.Empty;
            }
        }

        throw new InvalidOperationException($"Tool '{toolName}' not found among registered [McpServerTool] methods.");
    }
}
