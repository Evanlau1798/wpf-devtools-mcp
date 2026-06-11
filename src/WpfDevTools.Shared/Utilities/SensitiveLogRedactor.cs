using System.Text;
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
        "WPFDEVTOOLS_AUTH_SECRET|WPFDEVTOOLS_CERT_DIR|authSecret(?:Base64|File|FilePath)?|cert(?:ificate)?Directory|targetProcessPath|processPath|pipeName|secret(?:File)?|password|pwd|token|api[-_]?key|credential|cookie|session|windowTitle|secondaryWindowTitle|base64Image|screenshot(?:Id)?|resource(?:Uri|Id)|viewModel(?:Value)?|dataContext|propertyValue|currentValue|oldValue|newValue|value";

    private static readonly Regex JsonSensitiveValuePattern = new(
        $"(\"(?:{SensitiveNames})\"\\s*:\\s*)(\"(?:\\\\.|[^\"])*\"|-?\\d+(?:\\.\\d+)?|true|false|null)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AssignmentPattern = new(
        $@"\b({SensitiveNames})\s*[:=]\s*(""[^""]*""|'[^']*'|[^\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WindowTitleAssignmentPattern = new(
        @"\b(windowTitle|secondaryWindowTitle)\s*[:=]\s*([^,;\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
        return RedactLocalPaths(redacted);
    }

    private static string RedactLocalPaths(string value)
    {
        var builder = new StringBuilder(value.Length);
        var index = 0;

        while (index < value.Length)
        {
            if (TryGetLocalPathLength(value, index, out var pathLength))
            {
                builder.Append(RedactedPath);
                index += pathLength;
                continue;
            }

            builder.Append(value[index]);
            index++;
        }

        return builder.ToString();
    }

    private static bool TryGetLocalPathLength(string value, int start, out int length)
    {
        length = 0;

        if (!TryGetLocalPathPrefixLength(value, start, out var prefixLength))
        {
            return false;
        }

        var index = start + prefixLength;
        var extensionEnd = -1;
        while (index < value.Length && !IsPathDelimiter(value[index]))
        {
            if (value[index] == '.' && TryGetFileExtensionEnd(value, index, out var candidateExtensionEnd))
            {
                extensionEnd = candidateExtensionEnd;
            }

            index++;
        }

        if (extensionEnd > start && extensionEnd < index && char.IsWhiteSpace(value[extensionEnd]))
        {
            length = extensionEnd - start;
            return true;
        }

        length = index - start;
        return length > prefixLength;
    }

    private static bool TryGetLocalPathPrefixLength(string value, int start, out int prefixLength)
    {
        prefixLength = 0;

        if (start + 2 < value.Length
            && char.IsLetter(value[start])
            && value[start + 1] == ':'
            && IsPathSeparator(value[start + 2]))
        {
            prefixLength = 3;
            return true;
        }

        if (start + 1 < value.Length
            && IsPathSeparator(value[start])
            && IsPathSeparator(value[start + 1]))
        {
            prefixLength = 2;
            return true;
        }

        return false;
    }

    private static bool TryGetFileExtensionEnd(string value, int dotIndex, out int extensionEnd)
    {
        extensionEnd = dotIndex + 1;
        var extensionLength = 0;

        while (extensionEnd < value.Length
               && extensionLength < 8
               && char.IsLetterOrDigit(value[extensionEnd]))
        {
            extensionEnd++;
            extensionLength++;
        }

        return extensionLength > 0
               && (extensionEnd >= value.Length
                   || char.IsWhiteSpace(value[extensionEnd])
                   || IsPathDelimiter(value[extensionEnd]));
    }

    private static bool IsPathSeparator(char value)
        => value is '\\' or '/';

    private static bool IsPathDelimiter(char value)
        => value is '\r' or '\n' or '"' or '\'' or '<' or '>' or '|' or ';' or ',';
}
