namespace WpfDevTools.Mcp.Server.McpTools;

internal static partial class McpToolOutputSchemas
{
    private static object BindingEntry()
        => ObjectSchema("WPF binding entry.", new()
        {
            ["elementId"] = String("Runtime element id."),
            ["elementType"] = String("WPF element type."),
            ["propertyName"] = String("Bound target DependencyProperty name."),
            ["targetProperty"] = String("Legacy bound target property alias."),
            ["bindingType"] = String("Binding expression type."),
            ["path"] = String("Binding path or joined MultiBinding paths."),
            ["bindingPaths"] = ArrayOfString("Individual MultiBinding paths."),
            ["sourceType"] = String("Legacy binding source type when available."),
            ["mode"] = String("Binding mode."),
            ["updateSourceTrigger"] = String("UpdateSourceTrigger value."),
            ["status"] = String("Binding status."),
            ["converter"] = String("Converter type name when configured."),
            ["currentValue"] = JsonValue(),
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
            ["diagnosticKind"] = String("Diagnostic kind discriminator."),
            ["sourceKind"] = String("Source of the diagnostic record."),
            ["severity"] = String("Diagnostic severity."),
            ["timestamp"] = String("UTC diagnostic timestamp."),
            ["message"] = String("Binding error message."),
            ["eventType"] = String("WPF event type associated with the error."),
            ["sourceId"] = String("Source trace or event identifier."),
            ["elementId"] = String("Element id associated with the error."),
            ["suggestedElementId"] = String("Best-effort correlated element id."),
            ["matchConfidence"] = String("Confidence for suggestedElementId."),
            ["propertyName"] = String("Target property name."),
            ["bindingPath"] = String("Binding path."),
            ["elementType"] = String("Legacy WPF element type."),
            ["targetProperty"] = String("Legacy target property alias."),
            ["path"] = String("Legacy binding path alias."),
            ["trace"] = String("Original binding trace text."),
            ["source"] = String("Legacy diagnostic source.")
        });
}
