using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.Navigation.Rules;

internal static class ConditionalNavigationRules
{
    public const string ActiveSnapshot = "activeSnapshot";
    public const string ActiveTrace = "activeTrace";
    public const string SafeMutationLoopWorkflow = "safe-mutation-loop";

    public static ToolNextStep CreateActiveSnapshotStep(
        string tool,
        System.Text.Json.JsonElement @params,
        string reason,
        ToolNextStepKind kind,
        int priority,
        string expectedOutcome,
        params string?[] prefetchTools) =>
        ConditionalNextStep.Create(
            tool,
            @params,
            reason,
            kind,
            priority,
            [ActiveSnapshot],
            expectedOutcome,
            SafeMutationLoopWorkflow,
            NavigationLoadHint.ToolNames(prefetchTools));

    public static ToolNextStep CreateActiveTraceStep(
        string tool,
        System.Text.Json.JsonElement @params,
        string reason,
        ToolNextStepKind kind,
        int priority,
        string expectedOutcome) =>
        ConditionalNextStep.Create(
            tool,
            @params,
            reason,
            kind,
            priority,
            [ActiveTrace],
            expectedOutcome);
}
