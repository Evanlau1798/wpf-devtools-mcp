namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal sealed partial class UiBlueprintApplyService
{
    private static ExistingXamlContractAnalysis AnalyzeExistingContracts(
        string targetPath,
        string proposedXaml)
    {
        if (!File.Exists(targetPath))
        {
            return ExistingXamlContractAnalysis.NotApplicable;
        }

        try
        {
            var codeBehindPath = Path.ChangeExtension(targetPath, ".xaml.cs");
            var codeBehind = File.Exists(codeBehindPath)
                ? File.ReadAllText(codeBehindPath)
                : null;
            return ExistingXamlContractAnalyzer.Analyze(
                File.ReadAllText(targetPath),
                proposedXaml,
                codeBehind);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ExistingXamlContractAnalysis(
                [new ExistingXamlContractChange(
                    "ExistingContractAnalysisUnavailable", null, null, null, null, null,
                    "Existing XAML contracts could not be compared because the target files are not readable.")],
                Truncated: false,
                AnalysisAvailable: false);
        }
    }
}
