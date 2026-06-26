using System.Text.Json;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class ToolCallHelper
{
    private static ToolNavigationEnvelope BuildExceptionNavigation(string errorCode, JsonElement? args)
    {
        if (!string.Equals(errorCode, "SecurityError", StringComparison.Ordinal))
        {
            return ToolNavigationEnvelope.Empty;
        }

        var connectParams = TryGetProcessId(args) is int processId
            ? NavigationParamBuilders.Create(("processId", processId))
            : NavigationParamBuilders.Create();

        return ToolNavigationEnvelope.FromRecommended(
            [
                new ToolNextStep(
                    "get_processes",
                    NavigationParamBuilders.Create(),
                    "Re-check the target executable path and architecture after restoring release metadata trust.",
                    ToolNextStepKind.Diagnostic,
                    1,
                    Preconditions:
                    [
                        "SHA256SUMS.txt and release-assets.json are beside the original archive, or WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY points to that directory."
                    ],
                    ExpectedOutcome: "The agent can confirm the exact target path and matching package architecture before retrying connect.",
                    WhyNow: "Security verification failed before attach, so process identity should be rechecked after fixing the release metadata trust path.",
                    Confidence: "medium")
            ],
            alternatives:
            [
                new ToolNextStep(
                    "connect",
                    connectParams,
                    "Retry the connection after fixing release metadata trust.",
                    ToolNextStepKind.Action,
                    2,
                    Preconditions:
                    [
                        "The installed or portable package can access the original archive, SHA256SUMS.txt, and release-assets.json."
                    ],
                    ExpectedOutcome: "connect succeeds or returns the next precise policy, architecture, or process error.",
                    WhyNow: "The trust path is external to the target app and can be corrected before retrying the same workflow.",
                    Confidence: "medium")
            ],
            prefetchTools: ["connect"]);
    }
}
