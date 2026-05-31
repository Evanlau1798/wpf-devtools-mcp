namespace WpfDevTools.Mcp.Server.McpTools;

internal static partial class McpToolOutputSchemas
{
    private static object StateDiff()
        => ObjectSchema("State diff tool result payload.", new()
        {
            ["success"] = Boolean("Whether diff calculation succeeded."),
            ["error"] = String("Diff error message when success is false."),
            ["errorCode"] = String("Machine-readable diff error code."),
            ["snapshotId"] = String("Snapshot id used for the diff."),
            ["trigger"] = String("Human-readable trigger label for the observed change."),
            ["durationMs"] = Integer("Elapsed time between snapshot capture and diff calculation."),
            ["propertyChanges"] = ArrayOf("DependencyProperty changes since the snapshot.", PropertyChange()),
            ["viewModelChanges"] = ArrayOf("ViewModel property changes since the snapshot.", ViewModelChange()),
            ["newBindingErrors"] = ArrayOf("Binding errors that appeared after the snapshot.", BindingErrorDelta()),
            ["resolvedBindingErrors"] = ArrayOf("Binding errors that disappeared after the snapshot.", BindingErrorDelta()),
            ["validationChanges"] = ArrayOf("Validation error changes since the snapshot.", ValidationChange()),
            ["focusChange"] = FocusChange(),
            ["changedDependencyPropertyCount"] = Integer("Legacy DependencyProperty change count."),
            ["changedViewModelPropertyCount"] = Integer("Legacy ViewModel property change count."),
            ["focusChanged"] = Boolean("Legacy focus changed flag."),
            ["changes"] = ArrayOf("Legacy detected state changes.", StateChange()),
            ["before"] = MapOf("Optional baseline state excerpt.", JsonValue()),
            ["after"] = MapOf("Optional current state excerpt.", JsonValue())
        });
}
