using System.Text.Json;
using WpfDevTools.Mcp.Server.State;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class RestoreStateSnapshotTool
{
    private async Task VerifyFinalViewModelPropertiesAsync(
        int processId,
        long sessionGeneration,
        IReadOnlyList<StoredViewModelPropertySnapshot> snapshots,
        RestoreProgress progress,
        CancellationToken cancellationToken)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        var restoredResults = new List<object>();
        var skippedResults = new List<object>();
        var skippedRestores = new List<ViewModelRestoreFailure>();

        foreach (var group in snapshots.GroupBy(snapshot => snapshot.ElementId, StringComparer.Ordinal))
        {
            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                processId,
                sessionGeneration,
                "get_viewmodel",
                new { elementId = group.Key },
                cancellationToken,
                piggybackPendingEvents: false).ConfigureAwait(false));

            if (!IsSuccess(response))
            {
                ThrowIfStructuredRestoreFailure(response);
            }

            foreach (var snapshot in group)
            {
                var verification = GetFinalViewModelVerification(response, snapshot);
                if (snapshot.CanRestore)
                {
                    restoredResults.Add(new
                    {
                        propertyName = snapshot.PropertyName,
                        verified = verification.verified,
                        expectedValue = snapshot.Value,
                        currentValue = verification.currentValue,
                        verificationSkippedReason = verification.skippedReason
                    });

                    if (!verification.verified)
                    {
                        progress.Warnings.Add($"ViewModel final restore verification failed for '{snapshot.PropertyName}'.");
                        progress.FailedViewModelRestores.Add(new(
                            snapshot.ElementId,
                            snapshot.PropertyName,
                            "ReadBackMismatch",
                            "The final get_viewmodel read-back after DependencyProperty verification did not match the captured value.",
                            false));
                    }

                    continue;
                }

                var restoreDisposition = ClassifyRestoreDisposition(snapshot);
                var reason = snapshot.SkipReason ?? $"Property '{snapshot.PropertyName}' is not writable.";
                skippedResults.Add(new
                {
                    propertyName = snapshot.PropertyName,
                    reason,
                    restoreDisposition,
                    verified = verification.verified,
                    expectedValue = snapshot.Value,
                    currentValue = verification.currentValue,
                    verificationSkippedReason = verification.skippedReason
                });
                skippedRestores.Add(new(
                    snapshot.ElementId,
                    snapshot.PropertyName,
                    restoreDisposition,
                    reason,
                    verification.verified));

                if (!verification.verified)
                {
                    progress.Warnings.Add($"ViewModel final restore verification failed for skipped property '{snapshot.PropertyName}'.");
                }
            }
        }

        progress.RestoredViewModelProperties.Clear();
        progress.RestoredViewModelProperties.AddRange(restoredResults);
        progress.SkippedViewModelProperties.Clear();
        progress.SkippedViewModelProperties.AddRange(skippedResults);
        progress.SkippedViewModelRestores.Clear();
        progress.SkippedViewModelRestores.AddRange(skippedRestores);
    }

    private static (bool verified, string? currentValue, string? skippedReason) GetFinalViewModelVerification(
        JsonElement response,
        StoredViewModelPropertySnapshot snapshot)
    {
        if (!IsSuccess(response))
        {
            return (false, null, "Final get_viewmodel read-back failed.");
        }

        var property = response.GetProperty("properties")
            .EnumerateArray()
            .FirstOrDefault(item => string.Equals(
                GetOptionalString(item, "name"),
                snapshot.PropertyName,
                StringComparison.Ordinal));
        if (property.ValueKind == JsonValueKind.Undefined)
        {
            return (false, null, $"ViewModel property '{snapshot.PropertyName}' was not returned by final get_viewmodel read-back.");
        }

        var currentValue = GetOptionalString(property, "value");
        return (string.Equals(currentValue, snapshot.Value, StringComparison.Ordinal), currentValue, null);
    }
}
