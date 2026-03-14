using System.Linq;
using System.Text.RegularExpressions;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    private static readonly Regex BindingPathRegex =
        new(@"path '(?<path>[^']+)'|'(?<tracePath>[^']+)' property not found",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private object BuildBindingErrorPayload(
        BindingErrorInfo error,
        IReadOnlyList<BindingErrorInfo> liveErrors,
        bool compact)
    {
        var correlatedError = error.ElementId != null
            ? error
            : CorrelateTraceError(error, liveErrors);

        if (compact)
        {
            return new
            {
                diagnosticKind = "BindingError",
                sourceKind = correlatedError.Origin,
                severity = "Error",
                timestamp = correlatedError.Timestamp.ToString("O"),
                eventType = correlatedError.EventType,
                sourceId = correlatedError.SourceId,
                elementId = correlatedError.ElementId,
                suggestedElementId = correlatedError.SuggestedElementId,
                matchConfidence = correlatedError.MatchConfidence,
                propertyName = correlatedError.PropertyName,
                bindingPath = correlatedError.BindingPath
            };
        }

        return new
        {
            diagnosticKind = "BindingError",
            sourceKind = correlatedError.Origin,
            severity = "Error",
            timestamp = correlatedError.Timestamp.ToString("O"),
            message = correlatedError.Message,
            eventType = correlatedError.EventType,
            sourceId = correlatedError.SourceId,
            elementId = correlatedError.ElementId,
            suggestedElementId = correlatedError.SuggestedElementId,
            matchConfidence = correlatedError.MatchConfidence,
            propertyName = correlatedError.PropertyName,
            bindingPath = correlatedError.BindingPath
        };
    }

    private static BindingErrorInfo CorrelateTraceError(
        BindingErrorInfo traceError,
        IReadOnlyList<BindingErrorInfo> liveErrors)
    {
        var bindingPath = traceError.BindingPath ?? TryExtractBindingPath(traceError.Message);
        if (string.IsNullOrWhiteSpace(bindingPath))
        {
            return traceError.WithParsedFields(bindingPath: null);
        }

        var candidates = liveErrors
            .Where(error => string.Equals(error.BindingPath, bindingPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(error => error.ElementId, StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            return traceError.WithParsedFields(bindingPath, null, null, null);
        }

        if (candidates.Count == 1)
        {
            var match = candidates[0];
            return traceError.WithParsedFields(
                bindingPath,
                match.PropertyName,
                match.ElementId,
                "high");
        }

        var bestMatch = candidates[0];
        return traceError.WithParsedFields(
            bindingPath,
            bestMatch.PropertyName,
            bestMatch.ElementId,
            "low");
    }

    private static string? TryExtractBindingPath(string message)
    {
        var match = BindingPathRegex.Match(message);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["path"].Success
            ? match.Groups["path"].Value
            : match.Groups["tracePath"].Success
                ? match.Groups["tracePath"].Value
                : null;
    }
}

file static class BindingErrorInfoExtensions
{
    public static BindingErrorInfo WithParsedFields(
        this BindingErrorInfo error,
        string? bindingPath,
        string? propertyName = null,
        string? suggestedElementId = null,
        string? matchConfidence = null)
    {
        return new BindingErrorInfo
        {
            Timestamp = error.Timestamp,
            Message = error.Message,
            EventType = error.EventType,
            SourceId = error.SourceId,
            Origin = error.Origin,
            ElementId = error.ElementId,
            SuggestedElementId = suggestedElementId,
            MatchConfidence = matchConfidence,
            PropertyName = propertyName,
            BindingPath = bindingPath
        };
    }
}
