using System.Text.RegularExpressions;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static class ProjectIntegrationCodePatcher
{
    public static ProjectContentPatchResult Patch(
        string path,
        string rootNamespace,
        string className,
        string baseType)
    {
        if (!File.Exists(path))
        {
            return ProjectContentPatchResult.CreateSuccess(
                $"namespace {rootNamespace};{Environment.NewLine}{Environment.NewLine}"
                + $"public partial class {className} : {baseType}{Environment.NewLine}"
                + $"{{{Environment.NewLine}"
                + $"    public {className}() => InitializeComponent();{Environment.NewLine}"
                + $"}}{Environment.NewLine}");
        }

        try
        {
            var content = File.ReadAllText(path);
            var pattern = new Regex(
                $"(?<declaration>(?:public|internal|protected|private)?\\s*partial\\s+class\\s+{Regex.Escape(className)}\\b)(?:\\s*:\\s*(?<inheritance>[^{{\\r\\n]+?))?(?=\\s*\\{{)",
                RegexOptions.CultureInvariant);
            var matches = pattern.Matches(content);
            if (matches.Count == 0)
            {
                return CreateClassFailure(
                    "CodeBehindClassNotFound",
                    $"Code-behind '{path}' does not declare partial class '{className}'.");
            }

            if (matches.Count != 1)
            {
                return CreateClassFailure(
                    "CodeBehindClassAmbiguous",
                    $"Code-behind '{path}' declares partial class '{className}' more than once.");
            }

            var inheritance = matches[0].Groups["inheritance"];
            var interfaceSuffix = string.Empty;
            if (inheritance.Success && !TryGetInterfaceSuffix(inheritance.Value.Trim(), out interfaceSuffix))
            {
                return CreateClassFailure(
                    "CodeBehindInheritanceInvalid",
                    $"Code-behind '{path}' has an ambiguous or malformed inheritance clause for '{className}'.");
            }

            return ProjectContentPatchResult.CreateSuccess(pattern.Replace(content, match =>
                match.Groups["declaration"].Value + " : " + baseType + interfaceSuffix,
                count: 1));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ProjectContentPatchResult.CreateFailure(new ApplyBlueprintIssue(
                "$.targetPath",
                "CodeBehindReadFailed",
                $"Could not read code-behind '{path}': {ex.Message}",
                "Resolve the file access issue and rerun the dry-run plan."));
        }
    }

    private static bool TryGetInterfaceSuffix(string inheritance, out string suffix)
    {
        suffix = string.Empty;
        if (string.IsNullOrWhiteSpace(inheritance))
        {
            return false;
        }

        var depth = 0;
        var segmentStart = 0;
        var firstSeparator = -1;
        for (var index = 0; index < inheritance.Length; index++)
        {
            depth += inheritance[index] switch { '<' => 1, '>' => -1, _ => 0 };
            if (depth < 0)
            {
                return false;
            }

            if (inheritance[index] != ',' || depth != 0)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(inheritance[segmentStart..index]))
            {
                return false;
            }

            firstSeparator = firstSeparator < 0 ? index : firstSeparator;
            segmentStart = index + 1;
        }

        if (depth != 0 || string.IsNullOrWhiteSpace(inheritance[segmentStart..]))
        {
            return false;
        }

        suffix = firstSeparator < 0 ? string.Empty : inheritance[firstSeparator..];
        return true;
    }

    private static ProjectContentPatchResult CreateClassFailure(string code, string message)
        => ProjectContentPatchResult.CreateFailure(new ApplyBlueprintIssue(
            "$.targetPath",
            code,
            message,
            "Align the code-behind class with generated x:Class, then rerun the dry-run plan."));
}
