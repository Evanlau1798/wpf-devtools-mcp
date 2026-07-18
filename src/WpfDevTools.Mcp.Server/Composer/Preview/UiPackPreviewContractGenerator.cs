using System.Text;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed partial class UiPackPreviewContractGenerator(PackRegistry registry)
{
    private static readonly Regex IdentifierPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex NamespacePattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex RootElementPattern = new(
        "^\\s*<(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*):(?<type>[A-Za-z_][A-Za-z0-9_]*)[\\s/>]",
        RegexOptions.CultureInvariant);
    private static readonly IReadOnlyDictionary<string, string> BaseTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["frameworkElement"] = "FrameworkElement",
        ["button"] = "System.Windows.Controls.Button",
        ["control"] = "Control",
        ["contentControl"] = "ContentControl",
        ["itemsControl"] = "ItemsControl",
        ["toggleButton"] = "ToggleButton",
        ["window"] = "Window",
        ["resourceDictionary"] = "ResourceDictionary",
        ["stackPanel"] = "System.Windows.Controls.StackPanel",
        ["tabControl"] = "System.Windows.Controls.TabControl",
        ["tabItem"] = "System.Windows.Controls.TabItem"
    };
    private static readonly IReadOnlyDictionary<string, string> PropertyTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["string"] = "string?",
        ["double"] = "double",
        ["boolean"] = "bool",
        ["object"] = "object?",
        ["objectCollection"] = "ObservableCollection<object>"
    };
    private static readonly IReadOnlySet<string> FrameworkElementProperties = new HashSet<string>(
        ["DataContext", "Focusable", "Height", "HorizontalAlignment", "IsEnabled", "Margin", "MaxHeight", "MaxWidth", "MinHeight", "MinWidth", "Name", "Opacity", "Resources", "Style", "Tag", "ToolTip", "VerticalAlignment", "Visibility", "Width"],
        StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ControlProperties = Extend(
        FrameworkElementProperties,
        "Background", "BorderBrush", "BorderThickness", "FontFamily", "FontSize", "FontStretch", "FontStyle", "FontWeight", "Foreground", "HorizontalContentAlignment", "Padding", "TabIndex", "VerticalContentAlignment");
    private static readonly IReadOnlySet<string> ContentControlProperties = Extend(
        ControlProperties,
        "Content", "ContentStringFormat", "ContentTemplate");
    private static readonly IReadOnlySet<string> ItemsControlProperties = Extend(
        ControlProperties,
        "AlternationCount", "ItemContainerStyle", "ItemsSource", "ItemTemplate");
    private static readonly IReadOnlySet<string> ButtonProperties = Extend(
        ContentControlProperties,
        "Command", "CommandParameter");
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> InheritedPropertiesByBaseKind =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["frameworkElement"] = FrameworkElementProperties,
            ["control"] = ControlProperties,
            ["contentControl"] = ContentControlProperties,
            ["itemsControl"] = ItemsControlProperties,
            ["resourceDictionary"] = Set(),
            ["stackPanel"] = Extend(FrameworkElementProperties, "Background", "Orientation"),
            ["tabControl"] = Extend(ItemsControlProperties, "SelectedIndex", "SelectedItem", "SelectedValue", "SelectedValuePath"),
            ["tabItem"] = Extend(ContentControlProperties, "Header", "HeaderStringFormat", "HeaderTemplate", "IsSelected"),
            ["button"] = ButtonProperties,
            ["toggleButton"] = Extend(ButtonProperties, "IsChecked"),
            ["window"] = Extend(ContentControlProperties, "AllowsTransparency", "Icon", "ResizeMode", "ShowInTaskbar", "SizeToContent", "Title", "Topmost", "WindowState", "WindowStyle")
        };
    public PreviewContractGenerationResult Generate(
        string blueprintJson,
        string renderedXaml,
        IReadOnlyCollection<string>? runtimePackApprovalTokens = null,
        IReadOnlyDictionary<string, string>? renderedPackFingerprints = null)
    {
        var blueprint = ComposerJsonLoader.Parse<UiBlueprint>(blueprintJson, "<inline-blueprint>", UiComposerSchemaVersions.UiBlueprint);
        var usedPackIds = CollectUsedPackIds(blueprint.Layout, blueprint.Packs.Select(pack => pack.Id).ToArray());
        var available = registry.ListPacks().Packs.ToDictionary(pack => (pack.Id, pack.Version));
        var contracts = new List<ResolvedPreviewContract>();
        var diagnostics = new List<PreviewDiagnostic>();
        var advisories = new List<PreviewDiagnostic>();
        var snapshots = LoadPackSnapshots(
            blueprint.Packs,
            available,
            renderedPackFingerprints,
            diagnostics);
        var manifests = snapshots.ToDictionary(
            item => item.Key,
            item => item.Value.Pack.Manifest,
            StringComparer.Ordinal);
        var xamlNamespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (packId, manifest) in manifests)
        {
            foreach (var (prefix, namespaceUri) in manifest.XmlNamespaces)
            {
                if (!IsValidXmlPrefix(prefix))
                {
                    diagnostics.Add(Diagnostic(
                        "PackXmlNamespaceInvalid",
                        $"Pack '{packId}' XML prefix '{prefix}' is reserved or is not a safe XML identifier."));
                    continue;
                }

                if (xamlNamespaces.TryGetValue(prefix, out var existing)
                    && !string.Equals(existing, namespaceUri, StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic(
                        "PackXmlNamespaceConflict",
                        $"XML prefix '{prefix}' maps to both '{existing}' and '{namespaceUri}' after loading pack '{packId}'."));
                    continue;
                }

                xamlNamespaces.TryAdd(prefix, namespaceUri);
            }
        }

        var resourcesByPack = blueprint.Packs
            .Where(packRef => manifests.ContainsKey(packRef.Id))
            .ToDictionary(
                packRef => packRef.Id,
                packRef => PackResourceVariantResolver.Resolve(
                    manifests[packRef.Id],
                    blueprint.ResourceVariants.GetValueOrDefault(packRef.Id)).ApplicationMergedDictionaries,
                StringComparer.Ordinal);
        var runtimeCandidates = blueprint.Packs
            .Where(packRef => manifests.ContainsKey(packRef.Id))
            .Where(packRef => manifests[packRef.Id].NugetPackages.Any()
                || resourcesByPack[packRef.Id].Count > 0)
            .ToArray();
        var runtimeApproval = ResolveRuntimeApprovals(
            runtimeCandidates,
            snapshots,
            manifests,
            resourcesByPack,
            runtimePackApprovalTokens);
        diagnostics.AddRange(runtimeApproval.Errors);
        advisories.AddRange(runtimeApproval.Advisories);
        var approvedRuntimePackIds = runtimeApproval.ApprovedPackIds;
        var packagesByPack = runtimeApproval.PackagesByPack;

        foreach (var packRef in blueprint.Packs.Where(pack => usedPackIds.Contains(pack.Id)))
        {
            if (!manifests.TryGetValue(packRef.Id, out var manifest))
            {
                continue;
            }

            if (manifest.Preview is null)
            {
                if (RequiresPreviewContract(manifest, renderedXaml))
                {
                    diagnostics.Add(Diagnostic("PreviewContractMissing", $"Pack '{packRef.Id}' does not declare preview metadata."));
                }

                continue;
            }

            var prefix = manifest.XmlNamespaces.FirstOrDefault(item =>
                string.Equals(item.Value, manifest.Preview.NamespaceUri, StringComparison.Ordinal)).Key;
            var error = Validate(packRef.Id, prefix, manifest.Preview);
            if (error is not null)
            {
                diagnostics.Add(Diagnostic("PreviewContractInvalid", error));
                continue;
            }

            contracts.Add(new ResolvedPreviewContract(
                packRef.Id,
                prefix!,
                manifest.Preview,
                RuntimeBacked: manifest.NugetPackages.Any()
                    && approvedRuntimePackIds.Contains(packRef.Id)));
        }

        foreach (var conflict in contracts.GroupBy(contract => contract.Prefix, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            if (conflict.All(contract => contract.RuntimeBacked)
                && conflict.Select(contract => contract.Contract.NamespaceUri).Distinct(StringComparer.Ordinal).Count() == 1)
            {
                continue;
            }

            var participants = string.Join(", ", conflict.Select(contract =>
                $"'{contract.PackId}' ({contract.Contract.NamespaceUri})"));
            diagnostics.Add(Diagnostic(
                "PreviewNamespacePrefixConflict",
                $"Preview XML prefix '{conflict.Key}' is used by multiple packs: {participants}. Use a distinct pack-local prefix."));
        }

        foreach (var conflict in contracts.GroupBy(
                     contract => contract.Contract.NamespaceUri,
                     StringComparer.Ordinal)
                     .Where(group => group.Any(contract => contract.RuntimeBacked)
                         && group.Any(contract => !contract.RuntimeBacked)))
        {
            diagnostics.Add(Diagnostic(
                "PreviewNamespaceUriConflict",
                $"Preview namespace URI '{conflict.Key}' cannot mix runtime-backed and structural contracts."));
        }

        foreach (var conflict in contracts
                     .SelectMany(contract => contract.Contract.Types.Keys.Select(type => (Contract: contract, Type: type)))
                     .GroupBy(item => (item.Contract.Contract.NamespaceUri, item.Type))
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(Diagnostic(
                "PreviewNamespaceTypeConflict",
                $"Preview type '{conflict.Key.Type}' is declared by multiple packs for namespace URI '{conflict.Key.NamespaceUri}'."));
        }
        diagnostics.AddRange(ValidateRuntimePackageIdentities(packagesByPack));

        if (diagnostics.Count > 0)
        {
            return new PreviewContractGenerationResult(false, string.Empty, new Dictionary<string, string>(), null, null, diagnostics)
            {
                Advisories = advisories,
                RuntimePackApprovalReviews = runtimeApproval.Reviews
            };
        }

        var windowRoot = ResolveWindowRoot(renderedXaml, contracts);
        foreach (var contract in contracts.Where(contract => !contract.RuntimeBacked))
        {
            xamlNamespaces[contract.Prefix] = "clr-namespace:" + contract.Contract.ClrNamespace;
        }

        var runtimePackages = blueprint.Packs
            .Where(packRef => approvedRuntimePackIds.Contains(packRef.Id))
            .SelectMany(packRef => packagesByPack[packRef.Id])
            .GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var runtimeResources = blueprint.Packs
            .Where(packRef => approvedRuntimePackIds.Contains(packRef.Id))
            .SelectMany(packRef => resourcesByPack[packRef.Id])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new PreviewContractGenerationResult(
            true,
            GenerateSource(contracts.Where(contract => !contract.RuntimeBacked).ToArray()),
            xamlNamespaces,
            windowRoot?.Tag,
            windowRoot?.ClrType,
            [])
        {
            UsesStructuralStubs = contracts.Any(contract => !contract.RuntimeBacked)
                || runtimeCandidates.Any(packRef => !approvedRuntimePackIds.Contains(packRef.Id)),
            UsesRuntimeDependencies = runtimePackages.Length > 0 || runtimeResources.Length > 0,
            RuntimeNuGetPackages = runtimePackages,
            RuntimeResources = runtimeResources,
            Advisories = advisories,
            RuntimePackApprovalReviews = runtimeApproval.Reviews
        };
    }

    private static HashSet<string> CollectUsedPackIds(UiBlueprintNode root, IReadOnlyCollection<string> declaredPackIds)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<UiBlueprintNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            var packId = ComposerPackKindResolver.ResolveDeclaredPackId(node.Kind, declaredPackIds);
            if (packId is not null)
            {
                result.Add(packId);
            }

            foreach (var child in node.Slots.Values.SelectMany(children => children))
            {
                pending.Push(child);
            }
        }

        return result;
    }

    private static bool RequiresPreviewContract(UiPackManifest manifest, string renderedXaml)
        => manifest.XmlNamespaces.Keys.Any(prefix =>
            renderedXaml.Contains("<" + prefix + ":", StringComparison.Ordinal));

    private static bool IsValidXmlPrefix(string prefix)
        => IdentifierPattern.IsMatch(prefix)
           && !string.Equals(prefix, "x", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(prefix, "xml", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(prefix, "xmlns", StringComparison.OrdinalIgnoreCase);

    private static string? Validate(string packId, string? prefix, UiPackPreviewContract contract)
    {
        if (string.IsNullOrWhiteSpace(prefix)
            || !IsValidXmlPrefix(prefix)
            || string.IsNullOrWhiteSpace(contract.NamespaceUri)
            || !NamespacePattern.IsMatch(contract.ClrNamespace))
        {
            return $"Pack '{packId}' preview namespace metadata is invalid or does not match xmlNamespaces.";
        }

        foreach (var (typeName, type) in contract.Types)
        {
            if (!IdentifierPattern.IsMatch(typeName) || !BaseTypes.ContainsKey(type.BaseKind))
            {
                return $"Pack '{packId}' preview type '{typeName}' uses an unsafe name or baseKind.";
            }

            if (!string.IsNullOrEmpty(type.ContentProperty) && !type.Properties.ContainsKey(type.ContentProperty))
            {
                return $"Pack '{packId}' preview type '{typeName}' contentProperty is not declared in properties.";
            }

            if (!string.IsNullOrEmpty(type.ContentProperty) && !ContentCapableBaseKinds.Contains(type.BaseKind))
            {
                return $"Pack '{packId}' preview type '{typeName}' contentProperty is not supported by baseKind '{type.BaseKind}'.";
            }

            foreach (var (propertyName, propertyType) in type.Properties)
            {
                if (IsInheritedProperty(type.BaseKind, propertyName))
                {
                    return $"Pack '{packId}' preview property '{typeName}.{propertyName}' redeclares an inherited '{type.BaseKind}' property.";
                }

                if (!IdentifierPattern.IsMatch(propertyName) || !PropertyTypes.ContainsKey(propertyType))
                {
                    return $"Pack '{packId}' preview property '{typeName}.{propertyName}' is unsafe.";
                }
            }
        }

        return null;
    }

    private static bool IsInheritedProperty(string baseKind, string propertyName)
        => InheritedPropertiesByBaseKind.TryGetValue(baseKind, out var inherited)
           && inherited.Contains(propertyName);

    private static IReadOnlySet<string> Set(params string[] names)
        => new HashSet<string>(names, StringComparer.Ordinal);

    private static IReadOnlySet<string> Extend(IReadOnlySet<string> inherited, params string[] names)
        => inherited.Concat(names).ToHashSet(StringComparer.Ordinal);

    private static string GenerateSource(IReadOnlyList<ResolvedPreviewContract> contracts)
    {
        if (contracts.Count == 0)
        {
            return string.Empty;
        }

        var source = new StringBuilder();
        source.AppendLine("using System.Collections.ObjectModel;")
            .AppendLine("using System.Windows;")
            .AppendLine("using System.Windows.Controls;")
            .AppendLine("using System.Windows.Controls.Primitives;")
            .AppendLine("using System.Windows.Markup;");
        foreach (var contract in contracts)
        {
            source.Append("[assembly: XmlnsDefinition(\"").Append(Escape(contract.Contract.NamespaceUri))
                .Append("\", \"").Append(Escape(contract.Contract.ClrNamespace)).AppendLine("\")]");
        }

        foreach (var contract in contracts)
        {
            source.Append("namespace ").Append(contract.Contract.ClrNamespace).AppendLine()
                .AppendLine("{");
            foreach (var (typeName, type) in contract.Contract.Types.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                AppendType(source, typeName, type);
            }

            source.AppendLine("}");
        }

        return source.ToString();
    }

    private static void AppendType(StringBuilder source, string typeName, UiPackPreviewType type)
    {
        var baseType = BaseTypes[type.BaseKind];
        var collections = type.Properties.Where(item => item.Value == "objectCollection").Select(item => item.Key).ToArray();
        source.Append("    public class ").Append(typeName).Append(" : ").Append(baseType).AppendLine()
            .AppendLine("    {");
        foreach (var (propertyName, propertyType) in type.Properties.Where(item => item.Value != "objectCollection"))
        {
            AppendDependencyProperty(source, typeName, type, propertyName, propertyType);
        }

        foreach (var propertyName in collections)
        {
            source.Append("        public ObservableCollection<object> ").Append(propertyName).AppendLine(" { get; } = new();");
        }

        AppendConstructor(source, typeName, type, collections);
        AppendContentCallback(source, typeName, type);
        AppendItemsRefresh(source, type, collections);
        source.AppendLine("    }");
    }

    private static void AppendDependencyProperty(
        StringBuilder source,
        string typeName,
        UiPackPreviewType type,
        string propertyName,
        string propertyType)
    {
        var csharpType = PropertyTypes[propertyType];
        var runtimeType = csharpType.TrimEnd('?');
        var callback = propertyName == type.ContentProperty ? ", new PropertyMetadata(default(" + csharpType + "), OnPreviewContentChanged)" : string.Empty;
        source.Append("        public static readonly DependencyProperty ").Append(propertyName)
            .Append("Property = DependencyProperty.Register(nameof(").Append(propertyName).Append("), typeof(")
            .Append(runtimeType).Append("), typeof(").Append(typeName).Append(')').Append(callback).AppendLine(");")
            .Append("        public ").Append(csharpType).Append(' ').Append(propertyName).AppendLine()
            .AppendLine("        {")
            .Append("            get => (").Append(csharpType).Append(")GetValue(").Append(propertyName).AppendLine("Property);")
            .Append("            set => SetValue(").Append(propertyName).AppendLine("Property, value);")
            .AppendLine("        }");
    }

    private static void AppendConstructor(StringBuilder source, string typeName, UiPackPreviewType type, string[] collections)
    {
        if (type.BaseKind != "itemsControl" || collections.Length == 0)
        {
            return;
        }

        source.Append("        public ").Append(typeName).AppendLine("()")
            .AppendLine("        {");
        foreach (var propertyName in collections)
        {
            source.Append("            ").Append(propertyName).AppendLine(".CollectionChanged += (_, _) => RefreshPreviewItems();");
        }

        source.AppendLine("        }");
    }

    private static void AppendContentCallback(StringBuilder source, string typeName, UiPackPreviewType type)
    {
        if (string.IsNullOrEmpty(type.ContentProperty))
        {
            return;
        }

        source.AppendLine("        private static void OnPreviewContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)")
            .AppendLine("        {")
            .Append("            var control = (").Append(typeName).AppendLine(")d;");
        if (type.BaseKind == "itemsControl")
        {
            source.AppendLine("            control.RefreshPreviewItems();");
        }
        else if (type.BaseKind == "stackPanel")
        {
            source.AppendLine("            if (e.OldValue is UIElement oldChild) control.Children.Remove(oldChild);")
                .AppendLine("            if (e.NewValue is UIElement newChild) control.Children.Add(newChild);");
        }
        else
        {
            source.AppendLine("            control.Content = e.NewValue;");
        }
        source.AppendLine("        }");
    }

    private static void AppendItemsRefresh(StringBuilder source, UiPackPreviewType type, string[] collections)
    {
        if (type.BaseKind != "itemsControl")
        {
            return;
        }

        source.AppendLine("        private void RefreshPreviewItems()")
            .AppendLine("        {")
            .AppendLine("            Items.Clear();");
        foreach (var propertyName in collections)
        {
            source.Append("            foreach (var item in ").Append(propertyName).AppendLine(") Items.Add(item);");
        }

        if (!string.IsNullOrEmpty(type.ContentProperty))
        {
            source.Append("            if (").Append(type.ContentProperty).Append(" is not null) Items.Add(")
                .Append(type.ContentProperty).AppendLine(");");
        }

        source.AppendLine("        }");
    }

}
