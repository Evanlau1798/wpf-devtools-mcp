namespace WpfDevTools.Mcp.Server.McpTools;

internal static partial class McpToolOutputSchemas
{
    private static object ProcessSummary()
        => ObjectSchema("Allowlisted WPF process summary.", new()
        {
            ["processId"] = Integer("Operating system process id."),
            ["processName"] = String("Process name."),
            ["windowTitle"] = String("Main WPF window title."),
            ["secondaryWindowTitle"] = String("Secondary WPF window title when detected."),
            ["architecture"] = String("Process architecture."),
            ["dotNetVersion"] = String("Detected .NET runtime version when available."),
            ["runtime"] = String("Detected CLR/runtime family."),
            ["isElevated"] = Boolean("Whether the target process is elevated."),
            ["requiresElevationToConnect"] = Boolean("Whether elevation is required from the current server."),
            ["canConnectFromCurrentServer"] = Boolean("Whether the current server can connect directly."),
            ["connectionWarning"] = String("Connection warning when the current server cannot attach directly.")
        });

    private static object SceneNode()
        => ObjectSchema("Semantic UI scene node.", new()
        {
            ["elementId"] = String("Runtime element id."),
            ["elementType"] = String("WPF element type."),
            ["elementName"] = String("Element name when available."),
            ["name"] = String("Element name when available."),
            ["kind"] = String("Semantic node kind."),
            ["depth"] = Integer("Depth from the summarized root."),
            ["text"] = String("Visible text or accessible label."),
            ["currentValue"] = JsonValue(),
            ["annotations"] = ArrayOfString("Compact annotations for AI clients."),
            ["role"] = String("Semantic role inferred for the node."),
            ["state"] = String("Concise state summary."),
            ["bounds"] = Bounds(),
            ["children"] = ArrayOf("Immediate child node references.", NodeReference())
        });

    private static object PropertySnapshot()
        => ObjectSchema("DependencyProperty value snapshot.", new()
        {
            ["propertyName"] = String("DependencyProperty name."),
            ["ownerType"] = String("Owner type name."),
            ["value"] = JsonValue(),
            ["valuePreview"] = String("Truncated display value."),
            ["valueType"] = String("Runtime value type."),
            ["source"] = String("DependencyProperty value source."),
            ["isLocalValue"] = Boolean("Whether the value is locally set."),
            ["isAnimated"] = Boolean("Whether the value is animation-driven."),
            ["bindingStatus"] = String("Binding status when bound.")
        });

    private static object ValidationError()
        => ObjectSchema("Validation error record.", new()
        {
            ["propertyName"] = String("Validated property name."),
            ["message"] = String("Validation message."),
            ["rule"] = String("Validation rule name."),
            ["value"] = JsonValue()
        });

    private static object StyleSummary()
        => ObjectSchema("Style and template summary.", new()
        {
            ["styleType"] = String("Applied style type."),
            ["basedOn"] = String("Base style key or type."),
            ["templateType"] = String("Applied template type."),
            ["setters"] = ArrayOf("Applied setters.", StyleSetter()),
            ["triggers"] = ArrayOf("Active trigger summaries.", TriggerSummary()),
            ["resources"] = MapOf("Resolved resources keyed by resource key.", JsonValue())
        });

    private static object LayoutSummary()
        => ObjectSchema("Layout summary.", new()
        {
            ["visibility"] = String("Visibility value."),
            ["isVisible"] = Boolean("Computed IsVisible value."),
            ["isEnabled"] = Boolean("Computed IsEnabled value."),
            ["actualWidth"] = Number("ActualWidth."),
            ["actualHeight"] = Number("ActualHeight."),
            ["desiredWidth"] = Number("DesiredSize width."),
            ["desiredHeight"] = Number("DesiredSize height."),
            ["layoutState"] = String("High-level layout state."),
            ["notRenderedReason"] = String("Reason the element is not rendered when known."),
            ["bounds"] = Bounds()
        });

    private static object StateSnapshotSummary()
        => ObjectSchema("Summary of captured DependencyProperty, ViewModel, and focus state.", new()
        {
            ["dependencyPropertyCount"] = Integer("Captured DependencyProperty count."),
            ["restorableDependencyPropertyCount"] = Integer("Restorable DependencyProperty count."),
            ["skippedDependencyPropertyCount"] = Integer("Skipped DependencyProperty count."),
            ["viewModelPropertyCount"] = Integer("Captured ViewModel property count."),
            ["capturedFocus"] = Boolean("Whether focus was captured.")
        });

    private static object SnapshotCompleteness()
        => ObjectSchema("Snapshot baseline completeness flags.", new()
        {
            ["bindingErrorBaselineCaptured"] = Boolean("Whether binding error baseline capture succeeded."),
            ["validationBaselineCaptured"] = Boolean("Whether validation error baseline capture succeeded.")
        });

    private static object RestoredStateEntry()
        => ObjectSchema("Restored state entry.", new()
        {
            ["elementId"] = String("Element id."),
            ["propertyName"] = String("Property name."),
            ["oldValue"] = JsonValue(),
            ["restoredValue"] = JsonValue(),
            ["source"] = String("State source."),
            ["restoreDisposition"] = String("Restore classification."),
            ["verified"] = Boolean("Whether post-restore verification matched expected state."),
            ["expectedValue"] = JsonValue(),
            ["currentValue"] = JsonValue(),
            ["verificationSkippedReason"] = String("Reason verification could not be completed.")
        });

    private static object SkippedStateEntry()
        => ObjectSchema("Skipped state entry.", new()
        {
            ["elementId"] = String("Element id."),
            ["propertyName"] = String("Property name."),
            ["reason"] = String("Skip reason."),
            ["source"] = String("State source."),
            ["restoreDisposition"] = String("Restore classification."),
            ["verified"] = Boolean("Whether post-restore verification matched expected state."),
            ["expectedValue"] = JsonValue(),
            ["currentValue"] = JsonValue(),
            ["verificationSkippedReason"] = String("Reason verification could not be completed.")
        });

    private static object SkippedCaptureEntry()
        => ObjectSchema("Skipped capture request.", new()
        {
            ["source"] = String("State source."),
            ["elementId"] = String("Element id."),
            ["propertyName"] = String("Property name."),
            ["reason"] = String("Skip reason."),
            ["errorCode"] = String("Machine-readable error code.")
        });

    private static object MutationResult()
        => ObjectSchema("Per-mutation result.", new()
        {
            ["index"] = Integer("Mutation index."),
            ["tool"] = String("Mutation tool name."),
            ["label"] = String("Optional mutation label supplied by the caller."),
            ["success"] = Boolean("Whether this mutation succeeded."),
            ["skipped"] = Boolean("Whether this mutation was skipped after a previous failure."),
            ["error"] = String("Mutation error message."),
            ["errorCode"] = String("Machine-readable error code."),
            ["message"] = String("Mutation status message."),
            ["elementId"] = String("Element id affected by the mutation."),
            ["stateAfterTimeoutUnknown"] = Boolean("Whether this mutation may have left unknown runtime state."),
            ["result"] = MapOf("Tool-specific mutation result payload.", JsonValue())
        });

    private static object RollbackInfo()
        => ObjectSchema("Rollback availability and restore parameters.", new()
        {
            ["available"] = Boolean("Whether rollback is available."),
            ["snapshotId"] = String("Snapshot id to restore."),
            ["tool"] = String("Recommended rollback tool."),
            ["args"] = MapOf("Rollback tool arguments.", JsonValue()),
            ["params"] = MapOf("Rollback tool arguments using MCP-compatible naming.", JsonValue()),
            ["reason"] = String("Reason rollback is unavailable when applicable."),
            ["rollbackOnFailure"] = Boolean("Whether automatic rollback was requested."),
            ["attempted"] = Boolean("Whether automatic rollback was attempted."),
            ["succeeded"] = Boolean("Whether automatic rollback succeeded."),
            ["result"] = MapOf("restore_state_snapshot result when automatic rollback was attempted.", JsonValue())
        });

    private static object Recovery()
        => ObjectSchema("Machine-readable recovery guidance.", new()
        {
            ["retryable"] = Boolean("Whether retrying can reasonably succeed."),
            ["hint"] = String("Human-readable recovery hint."),
            ["suggestedAction"] = String("Operator-oriented recovery action."),
            ["requiresReconnect"] = Boolean("Whether reconnect is recommended before more mutations."),
            ["stateAfterTimeoutUnknown"] = Boolean("Whether runtime state may be unknown after timeout/interruption."),
            ["processId"] = Integer("Target process id when recovery guidance is process-scoped."),
            ["timeoutSeconds"] = Number("Timeout duration associated with the recovery payload."),
            ["retryAfterSeconds"] = Integer("Recommended retry delay in seconds."),
            ["retryAfter"] = String("Human-readable retry guidance."),
            ["availableTokens"] = Integer("Available rate-limit token count when reported."),
            ["availableEvents"] = ArrayOfString("Available event names when reported."),
            ["retryAfterMs"] = Integer("Recommended retry delay in milliseconds."),
            ["nextTools"] = ArrayOfString("Suggested follow-up tools."),
            ["tool"] = String("Recommended recovery tool."),
            ["params"] = MapOf("Recommended recovery tool parameters.", JsonValue()),
            ["rollbackOnFailure"] = Boolean("Whether automatic rollback was requested."),
            ["attempted"] = Boolean("Whether automatic rollback was attempted."),
            ["succeeded"] = Boolean("Whether automatic rollback succeeded."),
            ["details"] = MapOf("Additional recovery details.", JsonValue())
        });

    private static object NextStep()
        => ObjectSchema("Recommended action.", new()
        {
            ["tool"] = String("MCP tool."),
            ["params"] = MapOf("Tool arguments.", JsonValue()),
            ["reason"] = String("Why it helps."),
            ["kind"] = Integer("ToolNextStepKind number."),
            ["priority"] = Integer("Sort order; lower first."),
            ["preconditions"] = ArrayOfString("Preconditions for this step."),
            ["expectedOutcome"] = String("Expected result."),
            ["workflowId"] = String("Guided-workflow identifier."),
            ["prefetchTools"] = ArrayOfString("Tools to prefetch."),
            ["whyNow"] = String("Why this step applies."),
            ["confidence"] = String("Recommendation confidence.")
        });

    private static object ContextRef()
        => ObjectSchema("Structured reference for a follow-up tool call.", new()
        {
            ["type"] = String("Reference type discriminator."),
            ["elementId"] = String("Element id."),
            ["processId"] = Integer("Process id."),
            ["propertyName"] = String("Property name."),
            ["bindingPath"] = String("Binding path when the reference describes a binding issue."),
            ["diagnosis"] = String("Diagnostic classification for issue references."),
            ["snapshotId"] = String("Snapshot id for mutation-session references."),
            ["workflowId"] = String("Workflow id associated with the reference."),
            ["sourceTool"] = String("Tool that produced or should consume this context."),
            ["rootCause"] = String("Visibility or diagnostic root cause summary."),
            ["value"] = JsonValue()
        });

    private static object PendingEvent()
        => ObjectSchema("Piggybacked runtime event.", new()
        {
            ["eventId"] = String("Event id."),
            ["eventType"] = String("Event type."),
            ["eventName"] = String("WPF event name when available."),
            ["timestampUtc"] = String("UTC event timestamp."),
            ["elementId"] = String("Related element id."),
            ["propertyName"] = String("Related property name when available."),
            ["summary"] = String("Compact event summary."),
            ["payload"] = MapOf("Event payload.", JsonValue())
        });

    private static object UiSummaryPayloadLimits()
        => ObjectSchema("UI summary payload budget limits.", new()
        {
            ["maxTraversalNodes"] = Integer("Maximum traversal nodes."),
            ["maxSemanticNodes"] = Integer("Maximum semantic nodes."),
            ["maxSummaryTextLength"] = Integer("Maximum summary text length."),
            ["maxStringValueLength"] = Integer("Maximum string value length.")
        });

    private static object FormSummaryPayloadLimits()
        => ObjectSchema("Form summary payload budget limits.", new()
        {
            ["maxTraversalNodes"] = Integer("Maximum traversal nodes."),
            ["maxInputs"] = Integer("Maximum input controls."),
            ["maxCommands"] = Integer("Maximum command controls."),
            ["maxStringValueLength"] = Integer("Maximum string value length.")
        });

    private static object FormInput()
        => ObjectSchema("Form input summary.", new()
        {
            ["elementId"] = String("Input element id."),
            ["elementType"] = String("Input WPF type."),
            ["elementName"] = String("Input element name when available."),
            ["label"] = String("Best available input label."),
            ["currentValue"] = JsonValue(),
            ["bindingPath"] = String("Binding path when available."),
            ["isEmpty"] = Boolean("Whether the current value is empty."),
            ["validationErrors"] = ArrayOfString("Validation error messages associated with this input.")
        });

    private static object FormCommand()
        => ObjectSchema("Form command summary.", new()
        {
            ["elementId"] = String("Command element id."),
            ["elementType"] = String("Command WPF type."),
            ["elementName"] = String("Command element name when available."),
            ["text"] = String("Button text or accessible command label."),
            ["isPrimary"] = Boolean("Whether the command appears to be the primary submit action."),
            ["isReady"] = Boolean("Whether the command is interaction-ready."),
            ["blockers"] = ArrayOfString("Interaction readiness blockers.")
        });

    private static object FormSummary()
        => ObjectSchema("Aggregate form submittability summary.", new()
        {
            ["totalInputs"] = Integer("Number of discovered input controls."),
            ["emptyInputs"] = Integer("Number of empty inputs."),
            ["errorCount"] = Integer("Number of validation errors."),
            ["validationSubmittable"] = Boolean("Whether validation state allows submission."),
            ["interactionSubmittable"] = Boolean("Whether command/readiness state allows submission."),
            ["isSubmittable"] = Boolean("Combined form submittability result.")
        });

    private static object StyleSetter()
        => ObjectSchema("Style setter summary.", new()
        {
            ["propertyName"] = String("Setter property."),
            ["value"] = JsonValue(),
            ["source"] = String("Setter source.")
        });

    private static object TriggerSummary()
        => ObjectSchema("Trigger summary.", new()
        {
            ["triggerType"] = String("Trigger type."),
            ["propertyName"] = String("Trigger property."),
            ["value"] = JsonValue(),
            ["isActive"] = Boolean("Whether the trigger currently applies.")
        });

    private static object StateChange()
        => ObjectSchema("Detected state change.", new()
        {
            ["source"] = String("State source."),
            ["elementId"] = String("Element id."),
            ["propertyName"] = String("Changed property."),
            ["before"] = JsonValue(),
            ["after"] = JsonValue(),
            ["changeType"] = String("Change classification.")
        });

    private static object PropertyChange()
        => ObjectSchema("DependencyProperty state change.", new()
        {
            ["elementId"] = String("Element id."),
            ["propertyName"] = String("Changed DependencyProperty name."),
            ["beforeValue"] = JsonValue(),
            ["afterValue"] = JsonValue(),
            ["beforeBaseValueSource"] = String("Baseline DependencyProperty value source."),
            ["afterBaseValueSource"] = String("Current DependencyProperty value source.")
        });

    private static object ViewModelChange()
        => ObjectSchema("ViewModel property state change.", new()
        {
            ["elementId"] = String("Element id."),
            ["propertyName"] = String("Changed ViewModel property name."),
            ["beforeValue"] = JsonValue(),
            ["afterValue"] = JsonValue()
        });

    private static object BindingErrorDelta()
        => ObjectSchema("Binding error addition or resolution.", new()
        {
            ["elementId"] = String("Element id associated with the binding error."),
            ["suggestedElementId"] = String("Best-effort stable element id suggestion."),
            ["matchConfidence"] = String("Confidence of the suggested element id match."),
            ["propertyName"] = String("Target property name."),
            ["bindingPath"] = String("Binding path."),
            ["message"] = String("Binding error message.")
        });

    private static object ValidationChange()
        => ObjectSchema("Validation error state change.", new()
        {
            ["changeType"] = String("Validation change type."),
            ["elementType"] = String("Element type."),
            ["elementName"] = String("Element name."),
            ["errorContent"] = String("Validation error content."),
            ["isRuleError"] = Boolean("Whether the change came from a validation rule."),
            ["ruleType"] = String("Validation rule type.")
        });

    private static object FocusChange()
        => ObjectSchema("Focus state change.", new()
        {
            ["changed"] = Boolean("Whether focus changed."),
            ["beforeFocusKind"] = String("Baseline focus kind."),
            ["beforeFocusedElementId"] = String("Baseline focused element id."),
            ["afterFocusKind"] = String("Current focus kind."),
            ["afterFocusedElementId"] = String("Current focused element id.")
        });

    private static object Bounds()
        => ObjectSchema("Element bounds.", new()
        {
            ["x"] = Number("Left coordinate."),
            ["y"] = Number("Top coordinate."),
            ["width"] = Number("Width."),
            ["height"] = Number("Height.")
        });

    private static object NodeReference()
        => ObjectSchema("Child node reference.", new()
        {
            ["elementId"] = String("Runtime element id."),
            ["elementType"] = String("WPF element type."),
            ["name"] = String("Element name when available.")
        });

    private static object MapOf(string description, object valueSchema)
        => ObjectSchema(description, [], additionalProperties: valueSchema);

    private static object ObjectSchema(
        string description,
        Dictionary<string, object> properties,
        string[]? required = null,
        object? additionalProperties = null)
    {
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["description"] = description,
            ["additionalProperties"] = additionalProperties ?? false,
            ["properties"] = properties
        };

        if (required is { Length: > 0 })
        {
            schema["required"] = required;
        }

        return schema;
    }

    private static object ArrayOf(string description, object itemSchema)
        => new { type = "array", items = itemSchema, description };

    private static object ArrayOfString(string description)
        => new { type = "array", items = new { type = "string" }, description };

    private static object String(string description)
        => new { type = "string", description };

    private static object EnumString(string description, string[] values)
        => new Dictionary<string, object>
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = values
        };

    private static object Number(string description)
        => new { type = "number", description };

    private static object Integer(string description)
        => new { type = "integer", description };

    private static object Boolean(string description)
        => new { type = "boolean", description };

    private static object JsonValue(int depth = 2)
    {
        var scalarSchemas = new object[]
        {
            new { type = "string" },
            new { type = "number" },
            new { type = "integer" },
            new { type = "boolean" },
            new { type = "null" }
        };

        if (depth <= 0)
        {
            return new { oneOf = scalarSchemas };
        }

        return new
        {
            oneOf = scalarSchemas.Concat([
                ObjectSchema("JSON object.", [], additionalProperties: JsonValue(depth - 1)),
                ArrayOf("JSON array.", JsonValue(depth - 1))
            ]).ToArray()
        };
    }
}
