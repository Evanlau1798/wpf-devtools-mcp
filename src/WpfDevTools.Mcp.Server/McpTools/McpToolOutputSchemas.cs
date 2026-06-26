using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace WpfDevTools.Mcp.Server.McpTools;

internal static partial class McpToolOutputSchemas
{
    private static readonly JsonElement StructuredPayloadSchema = CreateSchema(
        additionalProperties: true,
        toolSpecificProperties: []);

    private static readonly Dictionary<string, JsonElement> HighValueSchemas = CreateHighValueSchemas();

    public static void Apply(IEnumerable<Tool> tools)
    {
        foreach (var tool in tools)
        {
            Apply(tool);
        }
    }

    public static void Apply(Tool tool)
    {
        tool.OutputSchema = HighValueSchemas.TryGetValue(tool.Name, out var schema)
            ? schema.Clone()
            : StructuredPayloadSchema.Clone();
    }

    internal static string GetSchemaStatus(string toolName)
        => HighValueSchemas.ContainsKey(toolName)
            ? "exact-tool-output-schema"
            : "generic-structured-payload-intentional";

    internal static string GetSchemaHashSource(string toolName)
    {
        var status = GetSchemaStatus(toolName);
        var schema = HighValueSchemas.TryGetValue(toolName, out var highValueSchema)
            ? highValueSchema
            : StructuredPayloadSchema;

        return status + ":" + schema.GetRawText();
    }

    private static Dictionary<string, JsonElement> CreateHighValueSchemas()
        => new(StringComparer.Ordinal)
        {
            ["connect"] = CreateSchema(false, new()
            {
                ["processId"] = Integer("Selected or target WPF process identifier."),
                ["processName"] = String("Selected process name."),
                ["windowTitle"] = String("Selected WPF window title."),
                ["reusedExistingHost"] = Boolean("Whether connect reused an existing Inspector host instead of injecting a new one."),
                ["connectionSource"] = String("Connection source: active-session, sdk-hosted-inspector, or raw-injection."),
                ["targetIsElevated"] = Boolean("Whether the requested target process is elevated."),
                ["requiresExplicitTargetOptIn"] = Boolean("Whether target policy requires an explicit allowlist entry before retrying."),
                ["autoDiscovered"] = Boolean("Whether connect selected an allowlisted target automatically."),
                ["autoSelected"] = Boolean("Whether a selection strategy selected the target."),
                ["selectionReason"] = String("Reason for the automatic selection."),
                ["candidateCount"] = Integer("Number of allowlisted candidate targets."),
                ["redactedCandidateCount"] = Integer("Number of denied candidates hidden by policy."),
                ["policyEnvVar"] = String("Policy environment variable relevant to the result."),
                ["processes"] = ArrayOf("Allowed candidate processes when disambiguation is required.", ProcessSummary()),
                ["requiresElevationToConnect"] = Boolean("Whether the server must run elevated to connect."),
                ["canConnectFromCurrentServer"] = Boolean("Whether the current server can connect directly."),
                ["suggestedAction"] = String("Operator-oriented next step for failed connection attempts.")
            }),
            ["get_processes"] = CreateSchema(false, new()
            {
                ["processes"] = ArrayOf("Allowlisted WPF processes visible to this server.", ProcessSummary()),
                ["redactedTargetCount"] = Integer("Number of denied targets hidden by policy before filtering."),
                ["policyEnvVar"] = String("Policy environment variable controlling process visibility.")
            }),
            ["get_ui_summary"] = CreateSchema(false, new()
            {
                ["rootElementId"] = String("Runtime element id for the summarized root."),
                ["rootElementType"] = String("WPF type for the summarized root."),
                ["rootElementName"] = String("Name for the summarized root when available."),
                ["depth"] = Integer("Traversal depth used to build the summary."),
                ["depthMode"] = String("Traversal mode used to build the summary."),
                ["scopeVisibility"] = String("Visibility scope used for summary traversal."),
                ["isCurrentlyVisible"] = Boolean("Whether the summarized scope is currently visible."),
                ["semanticNodeCount"] = Integer("Number of semantic nodes returned."),
                ["traversalNodeCount"] = Integer("Number of WPF nodes visited while building the summary."),
                ["omittedNodeCount"] = Integer("Number of nodes omitted because a traversal budget was reached."),
                ["omittedSemanticNodeCount"] = Integer("Number of semantic nodes omitted because a semantic budget was reached."),
                ["truncated"] = Boolean("Whether any summary payload budget was reached."),
                ["truncationReasons"] = ArrayOfString("Budget reason codes that caused truncation."),
                ["payloadLimits"] = UiSummaryPayloadLimits(),
                ["summaryText"] = String("Compact scene summary for AI clients."),
                ["nodes"] = ArrayOf("Semantic scene nodes when summaryOnly is false.", SceneNode()),
                ["navigationNodes"] = ArrayOf("Navigation-oriented semantic nodes when summaryOnly is true.", SceneNode())
            }),
            ["get_form_summary"] = CreateSchema(false, new()
            {
                ["formScope"] = String("Runtime element id for the summarized form scope."),
                ["scopeVisibility"] = String("Visibility scope used for form traversal."),
                ["isCurrentlyVisible"] = Boolean("Whether the summarized form scope is currently visible."),
                ["inputs"] = ArrayOf("Input controls discovered under the form scope.", FormInput()),
                ["commands"] = ArrayOf("Command-capable controls discovered under the form scope.", FormCommand()),
                ["traversalNodeCount"] = Integer("Number of WPF nodes visited while building the form summary."),
                ["omittedNodeCount"] = Integer("Number of nodes omitted because a traversal budget was reached."),
                ["omittedInputCount"] = Integer("Number of input controls omitted because an input budget was reached."),
                ["omittedCommandCount"] = Integer("Number of command controls omitted because a command budget was reached."),
                ["truncated"] = Boolean("Whether any form summary payload budget was reached."),
                ["truncationReasons"] = ArrayOfString("Budget reason codes that caused truncation."),
                ["payloadLimits"] = FormSummaryPayloadLimits(),
                ["summary"] = FormSummary()
            }),
            ["get_element_snapshot"] = CreateSchema(false, new()
            {
                ["elementId"] = String("Runtime element id."),
                ["elementType"] = String("WPF element type."),
                ["elementName"] = String("Element name when available."),
                ["dataContextType"] = String("DataContext type name when available."),
                ["properties"] = MapOf("DependencyProperty snapshot keyed by property name.", PropertySnapshot()),
                ["bindings"] = ArrayOf("Binding entries for the element.", BindingEntry()),
                ["validationErrors"] = ArrayOf("Validation errors for the element.", ValidationError()),
                ["style"] = StyleSummary(),
                ["layout"] = LayoutSummary()
            }),
            ["get_bindings"] = CreateSchema(false, new()
            {
                ["bindings"] = ArrayOf("Binding entries for single-target inspection.", BindingEntry()),
                ["results"] = ArrayOf("Per-target batch binding results.", BindingBatchResult()),
                ["resultCount"] = Integer("Number of batch results."),
                ["successCount"] = Integer("Number of successful batch results."),
                ["failureCount"] = Integer("Number of failed batch results.")
            }),
            ["get_binding_errors"] = CreateSchema(false, new()
            {
                ["errorCount"] = Integer("Number of binding errors."),
                ["errors"] = ArrayOf("Binding error records.", BindingErrorEntry()),
                ["navigation"] = Navigation(),
                ["nextSteps"] = ArrayOf("Compatibility next-step list.", NextStep())
            }),
            ["drain_events"] = CreateSchema(false, new()
            {
                ["pendingEventCount"] = Integer("Number of runtime events returned by this drain."),
                ["droppedEventCount"] = Integer("Number of runtime events dropped before this drain."),
                ["cleanupIncomplete"] = Boolean("Whether event-drain cleanup was incomplete."),
                ["cleanupFailureMessage"] = String("Cleanup failure details when cleanupIncomplete is true."),
                ["cleanupFailureType"] = String("Cleanup failure type when cleanupIncomplete is true."),
                ["pendingEvents"] = ArrayOf("Runtime events drained from the connected process.", PendingEvent())
            }),
            ["capture_state_snapshot"] = CreateSchema(false, new()
            {
                ["snapshotId"] = String("Captured mutation-safety snapshot id."),
                ["snapshotName"] = String("Caller-supplied snapshot name when provided."),
                ["snapshotSummary"] = StateSnapshotSummary(),
                ["snapshotCompleteness"] = SnapshotCompleteness(),
                ["skippedDependencyProperties"] = ArrayOf("DependencyProperty capture requests skipped before snapshot creation.", SkippedCaptureEntry()),
                ["warnings"] = ArrayOfString("Snapshot capture warnings.")
            }),
            ["get_state_diff"] = CreateSchema(false, new()
            {
                ["snapshotId"] = String("Snapshot id used for the diff."),
                ["trigger"] = String("Human-readable trigger label for the observed change."),
                ["durationMs"] = Integer("Elapsed time between snapshot capture and diff calculation."),
                ["propertyChanges"] = ArrayOf("DependencyProperty changes since the snapshot.", PropertyChange()),
                ["viewModelChanges"] = ArrayOf("ViewModel property changes since the snapshot.", ViewModelChange()),
                ["newBindingErrors"] = ArrayOf("Binding errors that appeared after the snapshot.", BindingErrorDelta()),
                ["resolvedBindingErrors"] = ArrayOf("Binding errors that disappeared after the snapshot.", BindingErrorDelta()),
                ["validationChanges"] = ArrayOf("Validation error changes since the snapshot.", ValidationChange()),
                ["focusChange"] = FocusChange()
            }),
            ["restore_state_snapshot"] = CreateSchema(false, new()
            {
                ["snapshotId"] = String("Snapshot id restored."),
                ["restoreIncomplete"] = Boolean("Whether restore was interrupted before all state could be verified."),
                ["stateAfterTimeoutUnknown"] = Boolean("Whether runtime state may be unknown after timeout/interruption."),
                ["requiresReconnect"] = Boolean("Whether reconnect is recommended before more mutations."),
                ["processId"] = Integer("Target process id when recovery guidance is process-scoped."),
                ["timeoutSeconds"] = Number("Timeout duration associated with an interrupted restore."),
                ["retryAfterSeconds"] = Number("Recommended retry delay in seconds."),
                ["retryAfter"] = String("Human-readable retry guidance."),
                ["availableTokens"] = Integer("Available rate-limit token count when reported."),
                ["availableEvents"] = ArrayOfString("Available event names when reported."),
                ["restoredDependencyPropertyCount"] = Integer("Number of DependencyProperty entries restored."),
                ["restoredDependencyProperties"] = ArrayOf("DependencyProperty entries restored.", RestoredStateEntry()),
                ["skippedDependencyPropertyCount"] = Integer("Number of DependencyProperty entries skipped."),
                ["restoredViewModelProperties"] = ArrayOf("ViewModel properties restored.", RestoredStateEntry()),
                ["restoredViewModelPropertyCount"] = Integer("Number of ViewModel properties restored."),
                ["skippedDependencyProperties"] = ArrayOf("DependencyProperty entries skipped with reasons.", SkippedStateEntry()),
                ["skippedViewModelPropertyCount"] = Integer("Number of ViewModel properties skipped."),
                ["skippedViewModelProperties"] = ArrayOf("ViewModel properties skipped with reasons.", SkippedStateEntry()),
                ["restoredFocus"] = Boolean("Whether focus was restored."),
                ["warnings"] = ArrayOfString("Restore warnings.")
            }),
            ["batch_mutate"] = CreateSchema(false, new()
            {
                ["executionMode"] = String("Batch execution strategy."),
                ["executionPolicy"] = String("Human-readable batch execution policy."),
                ["stopOnError"] = Boolean("Whether execution stops after the first failed mutation."),
                ["mutations"] = ArrayOf("Per-mutation results in execution order.", MutationResult()),
                ["mutationCount"] = Integer("Total mutation count."),
                ["executedMutationCount"] = Integer("Number of mutations that started execution."),
                ["successfulMutationCount"] = Integer("Number of successful mutations."),
                ["failedMutationCount"] = Integer("Number of failed mutations."),
                ["skippedMutationCount"] = Integer("Number of mutations skipped after an earlier failure."),
                ["stateAfterTimeoutUnknown"] = Boolean("Whether runtime state may be unknown after timeout/interruption."),
                ["requiresReconnect"] = Boolean("Whether reconnect is recommended before more mutations."),
                ["processId"] = Integer("Target process id when recovery guidance is process-scoped."),
                ["timeoutSeconds"] = Number("Timeout duration associated with a batch failure."),
                ["retryAfterSeconds"] = Number("Recommended retry delay in seconds."),
                ["retryAfter"] = String("Human-readable retry guidance."),
                ["availableTokens"] = Integer("Available rate-limit token count when reported."),
                ["availableEvents"] = ArrayOfString("Available event names when reported."),
                ["snapshotId"] = String("Snapshot captured for the batch when requested."),
                ["stateDiff"] = StateDiff(),
                ["rollback"] = RollbackInfo(),
                ["recovery"] = Recovery()
            }),
            ["element_screenshot"] = CreateSchema(false, new()
            {
                ["elementId"] = String("Runtime element id captured."),
                ["screenshotId"] = String("Registered screenshot identifier."),
                ["resourceUri"] = String("MCP resource URI for file-mode screenshot retrieval."),
                ["expiresAtUtc"] = String("UTC expiration timestamp for the retained screenshot resource."),
                ["outputMode"] = EnumString("Selected screenshot output mode.", ["metadata", "file", "base64"]),
                ["width"] = Integer("Screenshot width in pixels."),
                ["height"] = Integer("Screenshot height in pixels."),
                ["format"] = String("Screenshot image format label."),
                ["rendered"] = Boolean("Whether screenshot pixels were rendered for the selected output mode."),
                ["byteLength"] = Integer("Screenshot payload byte length."),
                ["fileName"] = String("Redacted screenshot file name for file output mode."),
                ["localPathRedacted"] = Boolean("Whether local filesystem paths were redacted."),
                ["sha256"] = String("SHA-256 hash of the screenshot bytes when available."),
                ["mimeType"] = String("Image MIME type."),
                ["base64Image"] = String("Inline base64 image data when explicitly requested and below the inline byte limit."),
                ["maxInlineByteLength"] = Integer("Inline base64 PNG byte limit reported on oversized inline requests."),
                ["nextSteps"] = ArrayOf("Compatibility next-step list.", NextStep())
            })
        };

    private static JsonElement CreateSchema(
        bool additionalProperties,
        Dictionary<string, object> toolSpecificProperties)
    {
        var properties = CommonProperties();
        foreach (var property in toolSpecificProperties)
        {
            properties[property.Key] = property.Value;
        }

        return JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "success" },
            additionalProperties,
            properties
        });
    }

    private static Dictionary<string, object> CommonProperties()
        => new(StringComparer.Ordinal)
        {
            ["success"] = Boolean("Whether the tool operation succeeded."),
            ["error"] = String("Human-readable error message when success is false."),
            ["errorCode"] = String("Machine-readable error code when available."),
            ["message"] = String("Human-readable status or summary message."),
            ["hint"] = String("Human-readable recovery hint when available."),
            ["suggestedAction"] = String("Operator-oriented next action when available."),
            ["toolName"] = String("MCP tool name when an execution wrapper reports an error."),
            ["requiresReconnect"] = Boolean("Whether the client should reconnect before retrying."),
            ["stateAfterTimeoutUnknown"] = Boolean("Whether target state may be unknown after a timeout."),
            ["processId"] = Integer("Process ID associated with the recovery or timeout condition."),
            ["timeoutSeconds"] = Integer("Timeout budget that was exceeded, in seconds."),
            ["retryAfterSeconds"] = Integer("Seconds to wait before retrying after throttling."),
            ["retryAfter"] = String("Human-readable retry-after guidance."),
            ["availableTokens"] = Integer("Remaining request tokens when rate-limit metadata is available."),
            ["availableEvents"] = ArrayOfString("Supported event names when event recovery metadata is available."),
            ["status"] = String("Tool-specific status string."),
            ["summaryText"] = String("Compact tool-specific summary text."),
            ["navigation"] = Navigation(),
            ["nextSteps"] = ArrayOf("Compatibility next-step list.", NextStep()),
            ["pendingEvents"] = ArrayOf("Piggybacked runtime events when present.", PendingEvent()),
            ["pendingEventCount"] = Integer("Number of piggybacked pending events."),
            ["droppedEventCount"] = Integer("Number of dropped pending events when bounded buffers overflow."),
            ["pendingEventsOrigin"] = String("Origin of the pending event payload when reported."),
            ["pendingEventsMayIncludePriorContext"] = Boolean("Whether pending events may include context from before this request."),
            ["pendingEventsSuggestedAction"] = String("Recommended drain_events workflow when a clean event window matters."),
            ["pendingEventsAreAdvisory"] = Boolean("Whether piggybacked pending events are advisory shared-buffer context rather than proof that the current request caused them."),
            ["pendingEventsSummary"] = String("Compact summary of piggybacked event count, event types, prior-context caveat, and clean drain workflow."),
            ["pendingEventsPiggybackFailed"] = Boolean("Whether automatic pending-event piggyback recovery failed after the primary tool result succeeded."),
            ["pendingEventsPiggybackFailureType"] = String("Failure type for the automatic pending-event piggyback recovery attempt."),
            ["pendingEventsMayRemainBuffered"] = Boolean("Whether pending events may remain buffered for an explicit drain_events call."),
            ["pendingEventsPiggybackRequiresReconnect"] = Boolean("Whether reconnect is recommended before recovering piggybacked pending events."),
            ["pendingEventsStateAfterTimeoutUnknown"] = Boolean("Whether pending-event state is unknown after a piggyback timeout or transport reset."),
            ["pendingEventsPiggybackSuggestedAction"] = String("Recommended reconnect and drain_events workflow after piggyback recovery fails."),
            ["cleanupIncomplete"] = Boolean("Whether cleanup after a tool operation was incomplete."),
            ["cleanupFailureMessage"] = String("Cleanup failure details when cleanupIncomplete is true."),
            ["cleanupFailureType"] = String("Cleanup failure type when cleanupIncomplete is true."),
            ["recovery"] = Recovery(),
            ["errorData"] = MapOf("Structured error context when available.", JsonValue())
        };

    private static object Navigation()
        => new
        {
            type = "object",
            description = "Recommended next-step envelope.",
            additionalProperties = false,
            properties = new Dictionary<string, object>
            {
                ["recommended"] = ArrayOf("Primary next-step recommendations.", NextStep()),
                ["alternatives"] = ArrayOf("Alternative follow-up actions.", NextStep()),
                ["prefetchTools"] = ArrayOfString("Useful tools to inspect before choosing a follow-up."),
                ["contextRefs"] = ArrayOf("Structured references for follow-up tool calls.", ContextRef())
            }
        };

}
