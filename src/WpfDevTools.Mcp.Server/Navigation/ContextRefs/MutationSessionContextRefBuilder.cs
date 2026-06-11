using WpfDevTools.Mcp.Server.Schema;
using WpfDevTools.Mcp.Server.Navigation.Rules;

namespace WpfDevTools.Mcp.Server.Navigation.ContextRefs;

internal static class MutationSessionContextRefBuilder
{
    public static ToolNavigationReference Create(string snapshotId, string sourceTool) =>
        ToolNavigationReference.Create(
            "mutation-session",
            ("snapshotId", snapshotId),
            ("workflowId", ConditionalNavigationRules.SafeMutationLoopWorkflow),
            ("sourceTool", sourceTool));
}
