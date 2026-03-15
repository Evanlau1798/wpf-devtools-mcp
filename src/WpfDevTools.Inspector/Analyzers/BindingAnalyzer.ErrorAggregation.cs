namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    private static IReadOnlyList<BindingErrorInfo> MergeBindingErrors(
        IReadOnlyList<BindingErrorInfo> traceErrors,
        IReadOnlyList<BindingErrorInfo> liveErrors)
    {
        if (traceErrors.Count == 0)
        {
            return liveErrors;
        }

        if (liveErrors.Count == 0)
        {
            return traceErrors;
        }

        var correlatedTraceErrors = traceErrors
            .Select(traceError => traceError.ElementId != null
                ? traceError
                : CorrelateTraceError(traceError, liveErrors))
            .ToList();

        var merged = new List<BindingErrorInfo>(correlatedTraceErrors.Count + liveErrors.Count);
        merged.AddRange(correlatedTraceErrors);

        foreach (var liveError in liveErrors)
        {
            if (correlatedTraceErrors.Any(traceError => RepresentsSameBindingIssue(traceError, liveError)))
            {
                continue;
            }

            merged.Add(liveError);
        }

        return merged;
    }

    private static bool RepresentsSameBindingIssue(
        BindingErrorInfo traceError,
        BindingErrorInfo liveError)
    {
        if (!string.Equals(
                traceError.BindingPath,
                liveError.BindingPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var traceElementId = traceError.ElementId ?? traceError.SuggestedElementId;
        if (!string.IsNullOrWhiteSpace(traceElementId))
        {
            return string.Equals(traceElementId, liveError.ElementId, StringComparison.Ordinal)
                && MatchesPropertyWhenAvailable(traceError.PropertyName, liveError.PropertyName);
        }

        if (!string.IsNullOrWhiteSpace(traceError.PropertyName))
        {
            return MatchesPropertyWhenAvailable(traceError.PropertyName, liveError.PropertyName);
        }

        return false;
    }

    private static bool MatchesPropertyWhenAvailable(
        string? expectedPropertyName,
        string? actualPropertyName)
    {
        return string.IsNullOrWhiteSpace(expectedPropertyName)
            || string.Equals(expectedPropertyName, actualPropertyName, StringComparison.OrdinalIgnoreCase);
    }
}
