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
                $"(?<declaration>(?:public|internal|protected|private)?\\s*partial\\s+class\\s+{Regex.Escape(className)})\\s*(?::\\s*[^{{\\r\\n]+)?",
                RegexOptions.CultureInvariant);
            if (!pattern.IsMatch(content))
            {
                return ProjectContentPatchResult.CreateFailure(new ApplyBlueprintIssue(
                    "$.targetPath",
                    "CodeBehindClassNotFound",
                    $"Code-behind '{path}' does not declare partial class '{className}'.",
                    "Align the code-behind class with generated x:Class, then rerun the dry-run plan."));
            }

            return ProjectContentPatchResult.CreateSuccess(pattern.Replace(
                content,
                match => match.Groups["declaration"].Value + " : " + baseType,
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
}
