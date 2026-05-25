using System.Text.RegularExpressions;

namespace WpfDevTools.Shared.Utilities;

/// <summary>
/// Central redaction rules for diagnostic logs.
/// </summary>
public static class SensitiveLogRedactor
{
    /// <summary>
    /// Replacement used for sensitive scalar values in diagnostic output.
    /// </summary>
    public const string RedactedValue = "[redacted]";
    private const string RedactedPath = "[redacted-path]";
    private const string SensitiveNames =
        "WPFDEVTOOLS_AUTH_SECRET|authSecret(?:Base64|File)?|secret(?:File)?|password|pwd|token|api[-_]?key|credential|cookie|session|windowTitle|secondaryWindowTitle|base64Image|screenshot|viewModel(?:Value)?|dataContext|propertyValue|currentValue|oldValue|newValue|value";

    private static readonly Regex JsonSensitiveValuePattern = new(
        $"(\"(?:{SensitiveNames})\"\\s*:\\s*)(\"(?:\\\\.|[^\"])*\"|-?\\d+(?:\\.\\d+)?|true|false|null)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AssignmentPattern = new(
        $@"\b({SensitiveNames})\s*[:=]\s*(""[^""]*""|'[^']*'|[^\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WindowTitleAssignmentPattern = new(
        @"\b(windowTitle|secondaryWindowTitle)\s*[:=]\s*([^,;\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LocalPathPattern = new(
        "(?i)(?:[A-Za-z]:(?:\\\\|\\\\\\\\)|\\\\\\\\)[^\\s'\\\"]+",
        RegexOptions.CultureInvariant);

    private static readonly Regex AuthSecretFileNamePattern = new(
        @"WpfDevTools_AuthSecret_[A-Za-z0-9_.-]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Redacts sensitive values and local paths before they are written to diagnostics.
    /// </summary>
    /// <param name="value">The candidate log value or message.</param>
    /// <returns>A diagnostic-safe string with sensitive content replaced.</returns>
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redacted = JsonSensitiveValuePattern.Replace(
            value,
            match => $"{match.Groups[1].Value}\"{RedactedValue}\"");
        redacted = WindowTitleAssignmentPattern.Replace(
            redacted,
            match => $"{match.Groups[1].Value}={RedactedValue}");
        redacted = AssignmentPattern.Replace(
            redacted,
            match => $"{match.Groups[1].Value}={RedactedValue}");
        redacted = AuthSecretFileNamePattern.Replace(redacted, RedactedValue);
        return LocalPathPattern.Replace(redacted, RedactedPath);
    }
}
