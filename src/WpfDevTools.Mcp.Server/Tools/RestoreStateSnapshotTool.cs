using System.Text.Json;
using WpfDevTools.Mcp.Server.State;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed class RestoreStateSnapshotTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
{
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments);
        if (error != null)
        {
            return error;
        }

        var snapshotId = ParseStringParam(arguments, "snapshotId");
        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            return CreateMissingParamError("snapshotId");
        }

        var removeAfterRestore = ParseBoolParam(arguments, "removeAfterRestore") ?? true;

        if (!_sessionManager.TryGetStateSnapshot(processId, snapshotId, out var snapshot) || snapshot == null)
        {
            return new { success = false, error = $"No stored snapshot found for snapshotId '{snapshotId}'." };
        }

        var warnings = new List<string>();
        var restoredDependencyProperties = await RestoreDependencyPropertiesAsync(
            processId,
            snapshot.DependencyProperties,
            warnings,
            cancellationToken).ConfigureAwait(false);
        var restoredViewModelProperties = await RestoreViewModelPropertiesAsync(
            processId,
            snapshot.ViewModelProperties,
            warnings,
            cancellationToken).ConfigureAwait(false);
        var restoredFocus = await RestoreFocusAsync(
            processId,
            snapshot.Focus,
            warnings,
            cancellationToken).ConfigureAwait(false);

        if (removeAfterRestore && warnings.Count == 0)
        {
            _sessionManager.RemoveStateSnapshot(processId, snapshotId);
        }

        return new
        {
            success = warnings.Count == 0,
            restoredDependencyPropertyCount = restoredDependencyProperties,
            restoredViewModelPropertyCount = restoredViewModelProperties,
            restoredFocus,
            warnings
        };
    }

    private async Task<int> RestoreDependencyPropertiesAsync(
        int processId,
        IReadOnlyList<StoredDependencyPropertySnapshot> snapshots,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var restored = 0;
        foreach (var snapshot in snapshots)
        {
            object parameters = snapshot.HadLocalValue
                ? new { elementId = snapshot.ElementId, propertyName = snapshot.PropertyName, value = snapshot.LocalValue }
                : new { elementId = snapshot.ElementId, propertyName = snapshot.PropertyName };
            var method = snapshot.HadLocalValue ? "set_dp_value" : "clear_dp_value";
            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                processId,
                method,
                parameters,
                cancellationToken).ConfigureAwait(false));

            if (IsSuccess(response))
            {
                restored++;
                continue;
            }

            warnings.Add($"DependencyProperty restore failed for '{snapshot.PropertyName}'.");
        }

        return restored;
    }

    private async Task<int> RestoreViewModelPropertiesAsync(
        int processId,
        IReadOnlyList<StoredViewModelPropertySnapshot> snapshots,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var restored = 0;
        foreach (var snapshot in snapshots)
        {
            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                processId,
                "modify_viewmodel",
                new { elementId = snapshot.ElementId, propertyName = snapshot.PropertyName, value = snapshot.Value },
                cancellationToken).ConfigureAwait(false));

            if (IsSuccess(response))
            {
                restored++;
                continue;
            }

            warnings.Add($"ViewModel restore failed for '{snapshot.PropertyName}'.");
        }

        return restored;
    }

    private async Task<bool> RestoreFocusAsync(
        int processId,
        StoredFocusSnapshot? snapshot,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (snapshot?.FocusedElementId == null)
        {
            return false;
        }

        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            "focus_element",
            new { elementId = snapshot.FocusedElementId },
            cancellationToken).ConfigureAwait(false));

        if (IsSuccess(response))
        {
            return true;
        }

        warnings.Add("Focus restore failed.");
        return false;
    }

    private static bool IsSuccess(JsonElement response) =>
        response.TryGetProperty("success", out var successProperty) && successProperty.GetBoolean();
}
