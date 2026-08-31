using System.Text.Json;
using WpfDevTools.Mcp.Server.State;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class RestoreStateSnapshotTool
{
    private async Task<bool> RestoreFocusAsync(
        int processId,
        long sessionGeneration,
        StoredFocusSnapshot? snapshot,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (snapshot == null)
        {
            return false;
        }

        var method = snapshot.FocusedElementId == null ? "clear_focus" : "focus_element";
        object parameters = snapshot.FocusedElementId == null
            ? new { }
            : new { elementId = snapshot.FocusedElementId };
        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            sessionGeneration,
            method,
            parameters,
            cancellationToken,
            piggybackPendingEvents: false).ConfigureAwait(false));

        if (!IsSuccess(response))
        {
            ThrowIfStructuredRestoreFailure(response);
            warnings.Add("Focus restore failed before final verification.");
            return false;
        }

        var readBack = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            sessionGeneration,
            "get_focus_state",
            new { },
            cancellationToken,
            piggybackPendingEvents: false).ConfigureAwait(false));
        if (!IsSuccess(readBack))
        {
            ThrowIfStructuredRestoreFailure(readBack);
            warnings.Add("Focus restore verification failed because get_focus_state read-back failed.");
            return false;
        }

        var actualElementId = GetOptionalString(readBack, "focusedElementId");
        var actualFocusKind = GetOptionalString(readBack, "focusKind");
        var verified = snapshot.FocusedElementId == null
            ? actualElementId == null && string.Equals(actualFocusKind, "None", StringComparison.Ordinal)
            : string.Equals(actualElementId, snapshot.FocusedElementId, StringComparison.Ordinal) &&
              string.Equals(actualFocusKind, snapshot.FocusKind, StringComparison.Ordinal);
        if (!verified)
        {
            warnings.Add("Focus restore verification failed because the final focus state did not match the captured baseline.");
        }

        return verified;
    }
}
