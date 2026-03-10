using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    private static readonly string[] ValidationTraceMarkers =
    [
        "ValidationError",
        "DataErrorValidationRule",
        "ExceptionValidationRule",
        "NotifyDataErrorValidationRule",
        "validation failed"
    ];

    private IReadOnlyList<BindingErrorInfo> FilterOutValidationErrors(IReadOnlyList<BindingErrorInfo> errors)
    {
        if (errors.Count == 0)
        {
            return errors;
        }

        var activeValidationMessages = GetActiveValidationMessages();
        return errors
            .Where(error => !IsValidationRelatedError(error, activeValidationMessages))
            .ToList();
    }

    private HashSet<string> GetActiveValidationMessages()
    {
        var rootElement = ResolveElement(elementId: null);
        var messages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rootElement == null)
        {
            return messages;
        }

        foreach (var current in DependencyObjectTraversal.EnumerateDescendantsAndSelf(rootElement))
        {
            foreach (var error in Validation.GetErrors(current))
            {
                var message = error.ErrorContent?.ToString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    messages.Add(message);
                }
            }
        }

        return messages;
    }

    private static bool IsValidationRelatedError(
        BindingErrorInfo error,
        HashSet<string> activeValidationMessages)
    {
        if (string.IsNullOrWhiteSpace(error.Message))
        {
            return false;
        }

        if (activeValidationMessages.Any(message =>
                error.Message.IndexOf(message, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return true;
        }

        return ValidationTraceMarkers.Any(marker =>
            error.Message.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
