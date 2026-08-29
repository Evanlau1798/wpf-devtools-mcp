namespace WpfDevTools.Mcp.Server.McpResources;

public static partial class CapabilityResources
{
    private static object[] CreateBlueprintDraftExamples()
        =>
        [
            new
            {
                name = "Create a minimal pack-neutral draft",
                arguments = new
                {
                    blueprintJson =
                        "{\"schemaVersion\":\"wpfdevtools.ui-blueprint.v1\",\"name\":\"MinimalShell\",\"packs\":[{\"id\":\"core\",\"version\":\"0.1.0\",\"required\":true,\"role\":\"primary\"}],\"primaryPack\":\"core\",\"layout\":{\"kind\":\"core.grid\"}}"
                },
                outputGuidance = new
                {
                    stored = true,
                    validationStatus = "not-run",
                    next = "Pass the returned draftRef to validate_ui_blueprint before rendering or applying."
                }
            }
        ];
}
