using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Events;

internal sealed class ValidationChangeTracker(ElementFinder elementFinder)
{
    private readonly ElementFinder _elementFinder = elementFinder;

    internal ValidationSnapshot CaptureSnapshot(DependencyObject scope, int maxDepth = 50)
    {
        var entries = new List<string>();

        foreach (var current in DependencyObjectTraversal.EnumerateDescendantsAndSelf(scope, maxDepth))
        {
            var currentElementId = _elementFinder.GenerateElementId(current);
            foreach (var error in Validation.GetErrors(current))
            {
                entries.Add($"{currentElementId}|{error.RuleInError?.GetType().Name}|{error.ErrorContent}");
            }
        }

        entries.Sort(StringComparer.Ordinal);
        return new ValidationSnapshot(entries.Count, ComputeFingerprint(entries));
    }

    internal WatchEventRecord? CreateTransitionEvent(
        string scopeElementId,
        ValidationSnapshot before,
        ValidationSnapshot after)
    {
        if (before.ErrorCount == after.ErrorCount
            && string.Equals(before.Fingerprint, after.Fingerprint, StringComparison.Ordinal))
        {
            return null;
        }

        return new WatchEventRecord(
            EventType: "ValidationChange",
            TimestampUtc: DateTimeOffset.UtcNow,
            SourceKey: $"validation:{scopeElementId}:{before.Fingerprint}:{after.Fingerprint}",
            ElementId: scopeElementId,
            PropertyName: null,
            EventName: null,
            NewValue: $"{before.ErrorCount}->{after.ErrorCount}",
            ValueType: "ValidationErrorCount",
            SenderType: null,
            SenderName: null,
            RoutingStrategy: null,
            Handled: null,
            OriginalSourceType: null);
    }

    internal sealed record ValidationSnapshot(int ErrorCount, string Fingerprint);

    private static string ComputeFingerprint(IReadOnlyList<string> entries)
    {
        if (entries.Count == 0)
        {
            return "empty";
        }

        using var sha = SHA256.Create();
        var payload = string.Join("\n", entries);
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}
