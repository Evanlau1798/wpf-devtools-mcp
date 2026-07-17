using System.Xml;
using System.Xml.Linq;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static class ProjectIntegrationXmlPatcher
{
    public static ProjectContentPatchResult PatchProjectPackages(
        string path,
        IReadOnlyList<RequiredNuGetPackage> packages,
        bool central)
        => PatchXml(path, document =>
        {
            var root = document.Root ?? throw new XmlException("Project XML has no root element.");
            foreach (var package in packages)
            {
                var reference = root.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "PackageReference"
                        && string.Equals((string?)element.Attribute("Include"), package.Id, StringComparison.OrdinalIgnoreCase));
                if (reference is null)
                {
                    var itemGroup = FindOrCreateUnconditionalItemGroup(root, "PackageReference");
                    reference = new XElement(root.Name.Namespace + "PackageReference", new XAttribute("Include", package.Id));
                    itemGroup.Add(reference);
                }

                if (central)
                {
                    reference.Attribute("Version")?.Remove();
                }
                else
                {
                    reference.SetAttributeValue("Version", package.VersionRange);
                }
            }
        });

    public static ProjectContentPatchResult PatchCentralPackages(
        string path,
        IReadOnlyList<RequiredNuGetPackage> packages)
        => PatchXml(path, document =>
        {
            var root = document.Root ?? throw new XmlException("Central package XML has no root element.");
            foreach (var package in packages)
            {
                var version = root.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "PackageVersion"
                        && string.Equals((string?)element.Attribute("Include"), package.Id, StringComparison.OrdinalIgnoreCase));
                if (version is null)
                {
                    var itemGroup = FindOrCreateUnconditionalItemGroup(root, "PackageVersion");
                    version = new XElement(root.Name.Namespace + "PackageVersion", new XAttribute("Include", package.Id));
                    itemGroup.Add(version);
                }

                version.SetAttributeValue("Version", package.VersionRange);
            }
        }, createRootName: "Project");

    public static ProjectContentPatchResult PatchApplication(
        string appPath,
        string projectRoot,
        string targetPath,
        IReadOnlyList<string> resources,
        IReadOnlyDictionary<string, string> namespaces,
        bool setStartup)
        => PatchXml(appPath, document =>
        {
            var application = document.Root ?? throw new XmlException("App.xaml has no Application root.");
            if (setStartup)
            {
                application.SetAttributeValue(
                    "StartupUri",
                    Path.GetRelativePath(projectRoot, targetPath).Replace(Path.DirectorySeparatorChar, '/'));
            }

            foreach (var (prefix, namespaceUri) in namespaces.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                application.SetAttributeValue(XNamespace.Xmlns + prefix, namespaceUri);
            }

            if (resources.Count == 0)
            {
                return;
            }

            var presentation = application.Name.Namespace;
            var appResources = application.Elements().FirstOrDefault(element => element.Name.LocalName == "Application.Resources");
            if (appResources is null)
            {
                appResources = new XElement(presentation + "Application.Resources");
                application.AddFirst(appResources);
            }

            var dictionary = appResources.Elements().FirstOrDefault(element => element.Name.LocalName == "ResourceDictionary");
            if (dictionary is null)
            {
                dictionary = new XElement(presentation + "ResourceDictionary");
                var existingNodes = appResources.Nodes().ToArray();
                appResources.RemoveNodes();
                dictionary.Add(existingNodes);
                appResources.Add(dictionary);
            }

            var merged = dictionary.Elements().FirstOrDefault(element => element.Name.LocalName == "ResourceDictionary.MergedDictionaries");
            if (merged is null)
            {
                merged = new XElement(presentation + "ResourceDictionary.MergedDictionaries");
                dictionary.AddFirst(merged);
            }

            foreach (var resource in resources)
            {
                var element = ParseResource(resource, presentation, namespaces);
                if (!merged.Elements().Any(existing => XNode.DeepEquals(existing, element)))
                {
                    merged.Add(element);
                }
            }
        });

    private static XElement ParseResource(
        string resource,
        XNamespace presentation,
        IReadOnlyDictionary<string, string> namespaces)
    {
        if (!resource.TrimStart().StartsWith('<'))
        {
            if (!PreviewResourcePolicy.IsApplicationLocalPackSource(resource))
            {
                throw new XmlException("Pack resource URI must reference an application-local pack resource.");
            }

            return new XElement(
                presentation + "ResourceDictionary",
                new XAttribute("Source", resource));
        }

        var wrapper = new XElement(presentation + "Wrapper");
        foreach (var (prefix, namespaceUri) in namespaces)
        {
            wrapper.SetAttributeValue(XNamespace.Xmlns + prefix, namespaceUri);
        }

        var wrapperText = wrapper.ToString(SaveOptions.DisableFormatting);
        var close = wrapperText.IndexOf(" />", StringComparison.Ordinal);
        if (close < 0)
        {
            throw new XmlException("Could not construct a resource namespace wrapper.");
        }

        var document = XDocument.Parse(wrapperText[..close] + ">" + resource + "</Wrapper>");
        return new XElement(document.Root?.Elements().Single()
            ?? throw new XmlException("Pack resource must contain one XAML element."));
    }

    private static XElement FindOrCreateUnconditionalItemGroup(XElement root, string itemName)
    {
        var itemGroup = root.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "ItemGroup"
            && element.Attribute("Condition") is null
            && element.Elements().Any(item => item.Name.LocalName == itemName));
        if (itemGroup is not null)
        {
            return itemGroup;
        }

        itemGroup = new XElement(root.Name.Namespace + "ItemGroup");
        root.Add(itemGroup);
        return itemGroup;
    }

    private static ProjectContentPatchResult PatchXml(
        string path,
        Action<XDocument> patch,
        string? createRootName = null)
    {
        try
        {
            XDocument document;
            if (!File.Exists(path) && createRootName is not null)
            {
                document = new XDocument(new XElement(createRootName));
            }
            else
            {
                using var reader = XmlReader.Create(path, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
                document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            }

            patch(document);
            return ProjectContentPatchResult.CreateSuccess(
                document.ToString().TrimEnd('\r', '\n') + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or InvalidOperationException)
        {
            return ProjectContentPatchResult.CreateFailure(new ApplyBlueprintIssue(
                "$.projectRoot",
                "ProjectIntegrationPatchFailed",
                $"Could not create a deterministic patch for '{path}': {ex.Message}",
                "Repair the project file, rerun apply_ui_blueprint dry-run, and review the new integration plan."));
        }
    }
}

internal sealed record ProjectContentPatchResult(bool Success, string Content, ApplyBlueprintIssue? Error)
{
    public static ProjectContentPatchResult CreateSuccess(string content) => new(true, content, null);
    public static ProjectContentPatchResult CreateFailure(ApplyBlueprintIssue error) => new(false, string.Empty, error);
}
