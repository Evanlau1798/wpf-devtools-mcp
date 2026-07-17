using System.Xml;
using System.Xml.Linq;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static class ExistingWindowHostComposer
{
    public static ProjectContentPatchResult Compose(
        string projectRoot,
        string targetPath,
        string renderedXaml,
        bool isWindowRoot,
        bool hasPackCodeBehind,
        int? targetWindowWidth,
        int? targetWindowHeight,
        out TargetWindowPlan targetWindowPlan)
    {
        targetWindowPlan = NotApplicable();
        XDocument rendered;
        try
        {
            rendered = XDocument.Parse(renderedXaml, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return ProjectContentPatchResult.CreateSuccess(renderedXaml);
        }

        if (rendered.Root is null)
        {
            return ProjectContentPatchResult.CreateSuccess(renderedXaml);
        }

        if (isWindowRoot)
        {
            ApplyDimensions(rendered.Root, targetWindowWidth, targetWindowHeight);
            targetWindowPlan = CreatePlan("rendered-window", targetWindowWidth, targetWindowHeight);
            return ProjectContentPatchResult.CreateSuccess(rendered.ToString(SaveOptions.DisableFormatting));
        }

        if (!File.Exists(targetPath)
            || !IsStartupTarget(projectRoot, targetPath))
        {
            return ProjectContentPatchResult.CreateSuccess(renderedXaml);
        }

        XDocument existing;
        try
        {
            existing = XDocument.Load(targetPath, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return ProjectContentPatchResult.CreateFailure(new ApplyBlueprintIssue(
                "$.targetPath",
                "ExistingWindowHostReadFailed",
                $"Could not inspect the existing XAML host '{targetPath}': {ex.Message}",
                "Resolve the XAML or file access issue, then rerun the dry-run plan."));
        }

        if (existing.Root is null
            || !string.Equals(existing.Root.Name.LocalName, "Window", StringComparison.Ordinal))
        {
            return ProjectContentPatchResult.CreateSuccess(renderedXaml);
        }

        var hasNestedClass = rendered.Root.Attributes()
            .Any(attribute => attribute.Name.LocalName == "Class");
        if (hasPackCodeBehind || hasNestedClass)
        {
            return ProjectContentPatchResult.CreateFailure(new ApplyBlueprintIssue(
                "$.targetPath",
                "NestedCodeBehindRootUnsupported",
                "A non-Window root with its own code-behind cannot be nested inside an existing Window shell.",
                "Use a pack-declared Window root or target a standalone view and host its generated class explicitly."));
        }

        existing.Root.ReplaceNodes(new XElement(rendered.Root));
        ApplyDimensions(existing.Root, targetWindowWidth, targetWindowHeight);
        targetWindowPlan = CreatePlan("existing-window", targetWindowWidth, targetWindowHeight);
        return ProjectContentPatchResult.CreateSuccess(existing.ToString(SaveOptions.DisableFormatting));
    }

    private static void ApplyDimensions(XElement root, int? width, int? height)
    {
        if (width.HasValue)
        {
            root.SetAttributeValue("Width", width.Value);
        }

        if (height.HasValue)
        {
            root.SetAttributeValue("Height", height.Value);
        }
    }

    private static TargetWindowPlan CreatePlan(string hostKind, int? width, int? height)
    {
        var status = width.HasValue || height.HasValue
            ? $"configured-{hostKind}"
            : $"preserved-{hostKind}";
        var guidance = width.HasValue || height.HasValue
            ? "Target Window sizing uses the reviewed values; keep them aligned with preview_ui_blueprint viewportWidth and viewportHeight."
            : "Existing Window sizing is preserved. Pass targetWindowWidth and targetWindowHeight from the reviewed preview_ui_blueprint viewport when exact viewport parity is required.";
        return new TargetWindowPlan(status, width, height, guidance);
    }

    private static TargetWindowPlan NotApplicable()
        => new(
            "not-applicable",
            null,
            null,
            "The target is not a rendered Window root or an existing startup Window host; target Window dimensions do not apply.");

    private static bool IsStartupTarget(string projectRoot, string targetPath)
    {
        try
        {
            var appPath = Path.Combine(projectRoot, "App.xaml");
            var startupUri = XDocument.Load(appPath).Root?.Attribute("StartupUri")?.Value;
            if (string.IsNullOrWhiteSpace(startupUri)
                || Uri.TryCreate(startupUri, UriKind.Absolute, out _))
            {
                return false;
            }

            var startupPath = Path.GetFullPath(Path.Combine(
                projectRoot,
                startupUri.Replace('/', Path.DirectorySeparatorChar)));
            return string.Equals(startupPath, Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return false;
        }
    }
}
