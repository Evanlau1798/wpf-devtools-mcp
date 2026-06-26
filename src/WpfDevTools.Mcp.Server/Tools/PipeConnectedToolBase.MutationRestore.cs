namespace WpfDevTools.Mcp.Server.Tools;

public abstract partial class PipeConnectedToolBase
{
    private const string MutationRestoreStatus = "notRestored";
    private const string MutationRestoreSuggestedAction =
        "Verify state, then call restore_state_snapshot if the app must be left unchanged.";

    private static void AddMutationRestoreGuidance(Dictionary<string, object?> payload)
    {
        payload["restoreRequired"] = true;
        payload["restoreStatus"] = MutationRestoreStatus;
        payload["restoreSuggestedAction"] = MutationRestoreSuggestedAction;
    }
}
