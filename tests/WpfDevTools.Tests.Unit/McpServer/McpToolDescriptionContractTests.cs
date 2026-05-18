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
    public void WatchDpChanges_Description_ShouldExplainTransientRegistrationLifecycle()
    {
        var description = GetDescriptionText("watch_dp_changes");

        description.Should().Contain("next successful drain_events readback or piggyback cycle",
            "AI agents must know exactly when STDIO watch registrations are cleared so they do not assume cross-session persistence");
        description.Should().Contain("Any successful drain_events readback ends that transient watch cycle",
            "AI agents must know that even non-DpChange drain reads expire the transient watch registration");
        description.Should().Contain("transient",
            "STDIO watch registrations should be described as transient to discourage relying on stale watcher state");
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

    [Fact]
    public void BatchMutate_Description_ShouldGateRollbackGuidanceOnRetainedSnapshots()
    {
        var description = GetDescriptionText("batch_mutate");

        description.Should().ContainAll(
            "rollback.available",
            "recovery.tool",
            "20 snapshots",
            "30 minutes",
            "manual reversal");
        description.Should().NotContain("stateAfterTimeoutUnknown=true means reconnect and restore before retrying");
    }

    [Fact]
    public void BatchMutate_Description_ShouldDocumentActualResponseEnvelope()
    {
        var description = GetDescriptionText("batch_mutate");

        description.Should().ContainAll(
            "mutationCount",
            "executedMutationCount",
            "successfulMutationCount",
            "failedMutationCount",
            "skippedMutationCount",
            "mutations",
            "stateDiff",
            "rollback",
            "error",
            "errorCode",
            "recovery",
            "stateAfterTimeoutUnknown",
            "requiresReconnect");
        description.Should().NotContain("totalSteps",
            "stale response fields cause AI agents to parse a payload shape that batch_mutate no longer returns");
        description.Should().NotContain("completedSteps",
            "stale response fields cause AI agents to parse a payload shape that batch_mutate no longer returns");
    }

    [Fact]
    public void GetNameScope_Description_ShouldDocumentNoNameScopeAsSuccessfulEmptyResult()
    {
        var description = GetDescriptionText("get_namescope");

        description.Should().ContainAll(
            "hasNameScope",
            "hasNameScope=false",
            "success=true",
            "traversalTruncated");
        description.Should().NotContain("ERROR: no namescope");
    }

    [Fact]
    public void DrainEvents_Description_ShouldAdvertiseCleanupDiagnosticsAndReplaySubsetSemantics()
    {
        var description = GetDescriptionText("drain_events");

        description.Should().Contain("cleanupIncomplete",
            "AI agents must know which field signals that buffered-event cleanup did not complete cleanly");
        description.Should().Contain("cleanupFailureMessage",
            "AI agents need the surfaced cleanup failure detail field to decide whether to quarantine or retry");
        description.Should().Contain("cleanupFailureType",
            "AI agents need the surfaced cleanup failure type field to classify follow-up risk");
        description.Should().Contain("uncapped live read internally",
            "AI agents must know replay-present drains internally bypass the inspector default live drain window before applying the caller-visible cap");
        description.Should().Contain("Any replay event that is not returned by the explicit read",
            "AI agents must know max-capped replay reads only consume the events actually returned to the caller");
        description.Should().Contain("matching live event that exceeds the caller-visible result cap",
            "AI agents must know that overflow live events are retained rather than silently lost");
        description.Should().Contain("remain buffered for the next explicit drain_events call",
            "AI agents must know that filtered or max-capped replay reads only consume the returned subset");
        description.Should().Contain("errorData.replayPreserved",
            "AI agents must know live drain failures do not discard already buffered replay");
        description.Should().Contain("errorData.bufferedReplayEventCount",
            "AI agents need the preserved replay count to decide whether a retry is still meaningful");
    }

    [Fact]
    public void DrainEvents_MaxEventsParameterDescription_ShouldExplainReplayBacklogDefault()
    {
        var method = FindToolMethod("drain_events");
        var maxEventsParameter = method.GetParameters().Single(parameter => parameter.Name == "maxEvents");
        var description = maxEventsParameter.GetCustomAttribute<DescriptionAttribute>()?.Description;

        description.Should().NotBeNull();
        description.Should().Contain("only when no replay is buffered",
            "schema-driven clients need to know omitting maxEvents is only bounded on the pure live-read path");
        description.Should().Contain("full merged replay plus matching live backlog",
            "schema-driven clients need the replay-backed omission case to avoid accidentally requesting an unbounded merged drain");
        description.Should().Contain("caller-visible result cap",
            "schema-driven clients need to know the cap is enforced after the internal uncapped replay-backed live read");
    }

    [Fact]
    public void TraceRoutedEvents_MaxEventsParameterDescription_ShouldAdvertiseTruncationMetadata()
    {
        var method = FindToolMethod("trace_routed_events");
        var maxEventsParameter = method.GetParameters().Single(parameter => parameter.Name == "maxEvents");
        var description = maxEventsParameter.GetCustomAttribute<DescriptionAttribute>()?.Description;

        description.Should().NotBeNull();
        description.Should().Contain("returnedEventCount",
            "schema-driven agents need the returned count when trace events are capped");
        description.Should().Contain("totalEventCount",
            "schema-driven agents need the original count when trace events are capped");
        description.Should().Contain("eventsTruncated",
            "schema-driven agents need a stable boolean that tells them to request a larger cap if needed");
    }

    private static McpServerToolAttribute FindToolAttribute(string toolName)
        => FindToolMethod(toolName).GetCustomAttribute<McpServerToolAttribute>()
            ?? throw new InvalidOperationException($"Tool '{toolName}' is missing [McpServerTool].");

    private static MethodInfo FindToolMethod(string toolName)
    {
        foreach (var type in McpServerAssembly.GetTypes()
                     .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr is not null && string.Equals(attr.Name, toolName, StringComparison.Ordinal))
                {
                    return method;
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
