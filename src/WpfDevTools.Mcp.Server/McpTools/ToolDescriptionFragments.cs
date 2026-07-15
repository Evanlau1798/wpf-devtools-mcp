namespace WpfDevTools.Mcp.Server.McpTools;

internal static class ToolDescriptionFragments
{
    public const string ConnectPrerequisite =
        "PREREQUISITE: connect() selected target.\n\n";

    public const string ContractGuidance =
        "CONTRACT: Use structuredContent; content[0].text is only a compact fallback. Fields/envelope: wpf://contracts/response.\n\n";

    public const string DetailMode =
        "DETAIL: detail=minimal|compact (default)|verbose; standard is an alias. Verbose adds requested/effective input and observedEffect; semantic fallback indicators remain.\n\n";

    public const string ActiveProcessIdParameter =
        "Process ID from get_processes; omit to use the active process.";

    public const string BatchElementIdsParameter =
        "Element IDs for batch inspection; do not combine with elementId.";

    public const string ComposerProjectRootParameter =
        "Project root; discovers .wpfdevtools/packs before global and built-in packs.";

    public const string ComposerLocalAppDataRootParameter =
        "LocalApplicationData override for global packs; omit for the current user's default.";

    public const string MutationDetailParameter =
        "Detail: minimal returns success/property/newValue only; compact (default); verbose adds requested/effective input and observedEffect; standard=compact.";

    public const string SuccessDetailParameter =
        "Detail: minimal returns success only; compact (default); verbose adds requested/effective input and observedEffect; standard=compact.";
}
