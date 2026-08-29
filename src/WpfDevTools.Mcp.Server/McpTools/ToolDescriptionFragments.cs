namespace WpfDevTools.Mcp.Server.McpTools;

internal static class ToolDescriptionFragments
{
    public const string ConnectPrerequisite =
        "PREREQUISITE: connect() selected target.\n\n";

    public const string ContractGuidance =
        "CONTRACT: structuredContent is canonical; content[0].text is compact fallback. See wpf://contracts/response.\n\n";

    public const string DetailMode =
        "DETAIL: detail=minimal|compact (default; standard alias)|verbose. Verbose adds inputs, observedEffect, and fallback indicators.\n\n";

    public const string ActiveProcessIdParameter =
        "Process ID from get_processes; omit to use the active process.";

    public const string BatchElementIdsParameter =
        "Element IDs for batch inspection; do not combine with elementId.";

    public const string ComposerProjectRootParameter =
        "Project root; discovers .wpfdevtools/packs before global and built-in packs.";

    public const string ComposerLocalAppDataRootParameter =
        "LocalApplicationData override for global packs; omit for the current user's default.";

    public const string MutationDetailParameter =
        "Detail: minimal, compact (default/standard), or verbose with inputs and observedEffect.";

    public const string SuccessDetailParameter =
        "Detail: minimal, compact (default/standard), or verbose with inputs and observedEffect.";
}
