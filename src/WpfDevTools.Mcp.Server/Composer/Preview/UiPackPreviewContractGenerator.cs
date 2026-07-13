using System.Text;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed class UiPackPreviewContractGenerator(PackRegistry registry)
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
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> NativeTabProperties =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["tabControl"] = new HashSet<string>(["SelectedIndex", "SelectedItem", "SelectedValue", "SelectedValuePath"], StringComparer.Ordinal),
            ["tabItem"] = new HashSet<string>(["Header", "HeaderStringFormat", "HeaderTemplate", "IsSelected"], StringComparer.Ordinal)
        };
    private static readonly IReadOnlySet<string> ContentCapableBaseKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "button",
        "contentControl",
        "itemsControl",
        "stackPanel",
        "tabItem",
        "toggleButton",
        "window"
    };

    public PreviewContractGenerationResult Generate(string blueprintJson, string renderedXaml)
    {
        var blueprint = ComposerJsonLoader.Parse<UiBlueprint>(blueprintJson, "<inline-blueprint>", UiComposerSchemaVersions.UiBlueprint);
        var usedPackIds = CollectUsedPackIds(blueprint.Layout, blueprint.Packs.Select(pack => pack.Id).ToArray());
        var available = registry.ListPacks().Packs.ToDictionary(pack => (pack.Id, pack.Version));
        var contracts = new List<ResolvedPreviewContract>();
        var diagnostics = new List<PreviewDiagnostic>();

        foreach (var packRef in blueprint.Packs.Where(pack => usedPackIds.Contains(pack.Id)))
        {
            if (!available.TryGetValue((packRef.Id, packRef.Version), out var pack))
            {
                continue;
            }

            var manifest = ComposerPackLoader.Load(pack.RootPath).Manifest;
            if (manifest.Preview is null)
            {
                if (RequiresPreviewContract(manifest, renderedXaml))
                {
                    diagnostics.Add(Diagnostic("PreviewContractMissing", $"Pack '{pack.Id}' does not declare preview metadata."));
                }

                continue;
            }

            var prefix = manifest.XmlNamespaces.FirstOrDefault(item =>
                string.Equals(item.Value, manifest.Preview.NamespaceUri, StringComparison.Ordinal)).Key;
            var error = Validate(pack.Id, prefix, manifest.Preview);
            if (error is not null)
            {
                diagnostics.Add(Diagnostic("PreviewContractInvalid", error));
                continue;
            }

            contracts.Add(new ResolvedPreviewContract(pack.Id, prefix!, manifest.Preview));
        }

        foreach (var conflict in contracts.GroupBy(contract => contract.Prefix, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var participants = string.Join(", ", conflict.Select(contract =>
                $"'{contract.PackId}' ({contract.Contract.NamespaceUri})"));
            diagnostics.Add(Diagnostic(
                "PreviewNamespacePrefixConflict",
                $"Preview XML prefix '{conflict.Key}' is used by multiple packs: {participants}. Use a distinct pack-local prefix."));
        }

        if (diagnostics.Count > 0)
        {
            return new PreviewContractGenerationResult(false, string.Empty, new Dictionary<string, string>(), null, null, diagnostics);
        }

        var windowRoot = ResolveWindowRoot(renderedXaml, contracts);
        return new PreviewContractGenerationResult(
            true,
            GenerateSource(contracts),
            contracts.ToDictionary(contract => contract.Prefix, contract => contract.Contract.ClrNamespace, StringComparer.Ordinal),
            windowRoot?.Tag,
            windowRoot?.ClrType,
            []);
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

    private static string? Validate(string packId, string? prefix, UiPackPreviewContract contract)
    {
        if (string.IsNullOrWhiteSpace(prefix)
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
                if (NativeTabProperties.TryGetValue(type.BaseKind, out var inherited)
                    && inherited.Contains(propertyName))
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

    private static string GenerateSource(IReadOnlyList<ResolvedPreviewContract> contracts)
    {
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

    private static PreviewWindowRoot? ResolveWindowRoot(
        string renderedXaml,
        IReadOnlyList<ResolvedPreviewContract> contracts)
    {
        var rootElement = RootElementPattern.Match(renderedXaml);
        if (!rootElement.Success)
        {
            return null;
        }

        var prefix = rootElement.Groups["prefix"].Value;
        var typeName = rootElement.Groups["type"].Value;
        var contract = contracts.FirstOrDefault(candidate =>
            string.Equals(candidate.Prefix, prefix, StringComparison.Ordinal));
        if (contract is null
            || !contract.Contract.Types.TryGetValue(typeName, out var type)
            || type.BaseKind != "window")
        {
            return null;
        }

        return new PreviewWindowRoot(
            prefix + ":" + typeName,
            contract.Contract.ClrNamespace + "." + typeName);
    }

    private static PreviewDiagnostic Diagnostic(string code, string message)
        => new(code, message, "$.packs", string.Empty);

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

internal sealed record PreviewContractGenerationResult(
    bool Success,
    string Source,
    IReadOnlyDictionary<string, string> XmlNamespaces,
    string? WindowRootTag,
    string? WindowRootType,
    IReadOnlyList<PreviewDiagnostic> Diagnostics);

internal sealed record ResolvedPreviewContract(string PackId, string Prefix, UiPackPreviewContract Contract);

internal sealed record PreviewWindowRoot(string Tag, string ClrType);
