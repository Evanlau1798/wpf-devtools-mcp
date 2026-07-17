using WpfDevTools.Mcp.Server.Composer.Preview;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal sealed partial class UiBlueprintApplyService
{
    private static void ValidateTargetWindowDimensions(
        ApplyBlueprintRequest request,
        List<ApplyBlueprintIssue> errors)
    {
        ValidateTargetWindowDimension("targetWindowWidth", request.TargetWindowWidth, errors);
        ValidateTargetWindowDimension("targetWindowHeight", request.TargetWindowHeight, errors);
    }

    private static void ValidateTargetWindowDimension(
        string parameterName,
        int? value,
        List<ApplyBlueprintIssue> errors)
    {
        if (value is null or >= 1 and <= UiPreviewProjectFiles.MaximumViewportDimension)
        {
            return;
        }

        errors.Add(new ApplyBlueprintIssue(
            "$." + parameterName,
            "InvalidTargetWindowDimension",
            $"{parameterName} must be between 1 and {UiPreviewProjectFiles.MaximumViewportDimension} DIPs.",
            "Copy the reviewed preview_ui_blueprint viewport dimension or omit this parameter to preserve existing Window sizing."));
    }
}
