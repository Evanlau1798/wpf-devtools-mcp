namespace WpfDevTools.Mcp.Server.McpTools;

internal static partial class McpToolOutputSchemas
{
    private static object ProcessSummary()
        => ObjectSchema("Allowlisted WPF process summary.", new()
        {
            ["processId"] = Integer("Operating system process id."),
            ["processName"] = String("Process name."),
            ["windowTitle"] = String("Main WPF window title."),
            ["architecture"] = String("Process architecture."),
            ["runtime"] = String("Detected CLR/runtime family."),
            ["requiresElevationToConnect"] = Boolean("Whether elevation is required from the current server."),
            ["canConnectFromCurrentServer"] = Boolean("Whether the current server can connect directly.")
        });

    private static object SceneNode()
        => ObjectSchema("Semantic UI scene node.", new()
        {
            ["elementId"] = String("Runtime element id."),
            ["elementType"] = String("WPF element type."),
            ["name"] = String("Element name when available."),
            ["text"] = String("Visible text or accessible label."),
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

    private static object BindingEntry()
        => ObjectSchema("WPF binding entry.", new()
        {
            ["elementId"] = String("Runtime element id."),
            ["elementType"] = String("WPF element type."),
            ["targetProperty"] = String("Bound target property."),
            ["path"] = String("Binding path."),
            ["sourceType"] = String("Binding source type."),
            ["mode"] = String("Binding mode."),
            ["updateSourceTrigger"] = String("UpdateSourceTrigger value."),
            ["status"] = String("Binding status."),
            ["value"] = JsonValue(),
            ["error"] = String("Binding error when present.")
        });

    private static object BindingBatchResult()
        => ObjectSchema("Per-target binding inspection result.", new()
        {
            ["elementId"] = String("Requested element id."),
            ["success"] = Boolean("Whether this target succeeded."),
            ["error"] = String("Target-specific error message."),
            ["bindings"] = ArrayOf("Bindings returned for this target.", BindingEntry())
        });

    private static object BindingErrorEntry()
        => ObjectSchema("Binding error record.", new()
        {
            ["elementId"] = String("Element id associated with the error."),
            ["elementType"] = String("WPF element type."),
            ["targetProperty"] = String("Target property."),
            ["path"] = String("Binding path."),
            ["message"] = String("Binding error message."),
            ["trace"] = String("Original binding trace text."),
            ["source"] = String("Diagnostic source.")
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
            ["viewModelPropertyCount"] = Integer("Captured ViewModel property count."),
            ["capturedElementCount"] = Integer("Captured element count."),
            ["focusElementId"] = String("Focused element id when captured."),
            ["warnings"] = ArrayOfString("Snapshot capture warnings.")
        });

    private static object StateDiff()
        => ObjectSchema("State diff payload.", new()
        {
            ["changedDependencyPropertyCount"] = Integer("DependencyProperty change count."),
            ["changedViewModelPropertyCount"] = Integer("ViewModel property change count."),
            ["focusChanged"] = Boolean("Whether focus changed."),
            ["changes"] = ArrayOf("Detected state changes.", StateChange()),
            ["before"] = MapOf("Optional baseline state excerpt.", JsonValue()),
            ["after"] = MapOf("Optional current state excerpt.", JsonValue())
        });

    private static object RestoredStateEntry()
        => ObjectSchema("Restored state entry.", new()
        {
            ["elementId"] = String("Element id."),
            ["propertyName"] = String("Property name."),
            ["oldValue"] = JsonValue(),
            ["restoredValue"] = JsonValue(),
            ["source"] = String("State source.")
        });

    private static object SkippedStateEntry()
        => ObjectSchema("Skipped state entry.", new()
        {
            ["elementId"] = String("Element id."),
            ["propertyName"] = String("Property name."),
            ["reason"] = String("Skip reason."),
            ["source"] = String("State source.")
        });

    private static object MutationResult()
        => ObjectSchema("Per-mutation result.", new()
        {
            ["index"] = Integer("Mutation index."),
            ["tool"] = String("Mutation tool name."),
            ["success"] = Boolean("Whether this mutation succeeded."),
            ["error"] = String("Mutation error message."),
            ["errorCode"] = String("Machine-readable error code."),
            ["message"] = String("Mutation status message."),
            ["elementId"] = String("Element id affected by the mutation."),
            ["result"] = MapOf("Tool-specific mutation result payload.", JsonValue())
        });

    private static object RollbackInfo()
        => ObjectSchema("Rollback availability and restore parameters.", new()
        {
            ["available"] = Boolean("Whether rollback is available."),
            ["snapshotId"] = String("Snapshot id to restore."),
            ["tool"] = String("Recommended rollback tool."),
            ["args"] = MapOf("Rollback tool arguments.", JsonValue()),
            ["reason"] = String("Reason rollback is unavailable when applicable.")
        });

    private static object Recovery()
        => ObjectSchema("Machine-readable recovery guidance.", new()
        {
            ["retryable"] = Boolean("Whether retrying can reasonably succeed."),
            ["suggestedAction"] = String("Operator-oriented recovery action."),
            ["retryAfterMs"] = Integer("Recommended retry delay in milliseconds."),
            ["nextTools"] = ArrayOfString("Suggested follow-up tools."),
            ["details"] = MapOf("Additional recovery details.", JsonValue())
        });

    private static object NextStep()
        => ObjectSchema("Recommended follow-up action.", new()
        {
            ["tool"] = String("Recommended MCP tool name."),
            ["label"] = String("Human-readable action label."),
            ["reason"] = String("Why this action is useful."),
            ["priority"] = String("Priority or ordering hint."),
            ["args"] = MapOf("Suggested tool arguments.", JsonValue())
        });

    private static object ContextRef()
        => ObjectSchema("Structured reference for a follow-up tool call.", new()
        {
            ["kind"] = String("Reference kind."),
            ["elementId"] = String("Element id."),
            ["processId"] = Integer("Process id."),
            ["propertyName"] = String("Property name."),
            ["value"] = JsonValue()
        });

    private static object PendingEvent()
        => ObjectSchema("Piggybacked runtime event.", new()
        {
            ["eventId"] = String("Event id."),
            ["eventType"] = String("Event type."),
            ["timestampUtc"] = String("UTC event timestamp."),
            ["elementId"] = String("Related element id."),
            ["summary"] = String("Compact event summary."),
            ["payload"] = MapOf("Event payload.", JsonValue())
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
                ObjectSchema("Arbitrary JSON object.", [], additionalProperties: JsonValue(depth - 1)),
                ArrayOf("Arbitrary JSON array.", JsonValue(depth - 1))
            ]).ToArray()
        };
    }
}
