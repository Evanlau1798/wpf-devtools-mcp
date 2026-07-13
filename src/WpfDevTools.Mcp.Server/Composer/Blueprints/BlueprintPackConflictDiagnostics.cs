namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class BlueprintPackConflictDiagnostics
{
    public static void AddIssues(
        BlueprintResolutionPlan resolution,
        List<BlueprintValidationIssue> errors,
        List<BlueprintValidationIssue> warnings)
    {
        foreach (var conflict in resolution.Conflicts)
        {
            var issue = new BlueprintValidationIssue(
                "$.packs",
                conflict.Code,
                conflict.Message,
                conflict.RepairSuggestion,
                [],
                [],
                null);
            (conflict.Severity == "error" ? errors : warnings).Add(issue);
        }
    }
}
