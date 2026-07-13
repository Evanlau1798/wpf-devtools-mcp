using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static partial class BlueprintSemanticIdentityDiagnostics
{
    private const int MaxAutomationIdLength = 128;

    public static void AddIssues(UiBlueprintNode root, List<BlueprintValidationIssue> errors)
    {
        var elementNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var automationIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new Stack<(UiBlueprintNode Node, string Path)>();
        pending.Push((root, "$.layout"));

        while (pending.Count > 0)
        {
            var (node, path) = pending.Pop();
            ValidateElementName(node.ElementName, path, elementNames, errors);
            ValidateAutomationId(node.AutomationId, path, automationIds, errors);

            foreach (var (slotName, children) in node.Slots.Reverse())
            {
                for (var index = children.Length - 1; index >= 0; index--)
                {
                    pending.Push((children[index], $"{path}.slots.{slotName}[{index}]"));
                }
            }
        }
    }

    private static void ValidateElementName(
        string? value,
        string nodePath,
        Dictionary<string, string> seen,
        List<BlueprintValidationIssue> errors)
    {
        if (value is null)
        {
            return;
        }

        var path = nodePath + ".elementName";
        if (!ElementNamePattern().IsMatch(value))
        {
            errors.Add(Issue(
                path,
                "InvalidElementName",
                $"Element name '{value}' is not a safe WPF x:Name identifier.",
                "Use an ASCII identifier that starts with a letter or underscore and contains only letters, digits, or underscores."));
            return;
        }

        AddDuplicateIssue(value, path, "DuplicateElementName", "elementName", seen, errors);
    }

    private static void ValidateAutomationId(
        string? value,
        string nodePath,
        Dictionary<string, string> seen,
        List<BlueprintValidationIssue> errors)
    {
        if (value is null)
        {
            return;
        }

        var path = nodePath + ".automationId";
        if (value.Length > MaxAutomationIdLength || !AutomationIdPattern().IsMatch(value))
        {
            errors.Add(Issue(
                path,
                "InvalidAutomationId",
                $"Automation id '{value}' is not a safe stable identifier.",
                $"Use 1-{MaxAutomationIdLength} ASCII letters, digits, underscores, periods, or hyphens, starting with a letter or underscore."));
            return;
        }

        AddDuplicateIssue(value, path, "DuplicateAutomationId", "automationId", seen, errors);
    }

    private static void AddDuplicateIssue(
        string value,
        string path,
        string code,
        string field,
        Dictionary<string, string> seen,
        List<BlueprintValidationIssue> errors)
    {
        if (seen.TryGetValue(value, out var firstPath))
        {
            errors.Add(Issue(
                path,
                code,
                $"Authored {field} '{value}' duplicates {firstPath}.",
                $"Choose a unique {field} for every blueprint node."));
            return;
        }

        seen[value] = path;
    }

    private static BlueprintValidationIssue Issue(string path, string code, string message, string repair)
        => new(path, code, message, repair, [], [], null);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ElementNamePattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex AutomationIdPattern();
}
