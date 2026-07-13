using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfDevTools.Mcp.Server.Composer.Contracts;

internal abstract class ComposerJsonDocument
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourceFilePath { get; internal set; } = string.Empty;

    [JsonIgnore]
    public string JsonPath { get; internal set; } = "$";
}

internal sealed class UiPackManifest : ComposerJsonDocument
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public UiPackNuGetPackage[] NugetPackages { get; set; } = [];
    public Dictionary<string, string> XmlNamespaces { get; set; } = new(StringComparer.Ordinal);
    public UiPackResourceSetup ResourceSetup { get; set; } = new();
    public Dictionary<string, JsonElement> ThemeTokens { get; set; } = new(StringComparer.Ordinal);
    public UiPackPreviewContract? Preview { get; set; }
    public string[] Blocks { get; set; } = [];
    public string[] Recipes { get; set; } = [];
}

internal sealed class UiPackNuGetPackage
{
    public string Id { get; set; } = string.Empty;
    public string VersionRange { get; set; } = string.Empty;
}

internal sealed class UiPackResourceSetup
{
    public string[] ApplicationMergedDictionaries { get; set; } = [];
    public string DefaultVariant { get; set; } = string.Empty;
    public Dictionary<string, UiPackResourceVariant> Variants { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class UiPackResourceVariant
{
    public string Appearance { get; set; } = string.Empty;
    public string[] ApplicationMergedDictionaries { get; set; } = [];
}

internal sealed class UiBlockDefinition : ComposerJsonDocument
{
    public string Kind { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Dictionary<string, UiBlockProperty> Properties { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, UiBlockSlot> Slots { get; set; } = new(StringComparer.Ordinal);
    public UiBlockInteraction? Interaction { get; set; }
    public UiBlockRenderer Renderer { get; set; } = new();
    public SourceHint[] SourceHints { get; set; } = [];
}

internal sealed class UiBlockProperty
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PreviewWarning { get; set; } = string.Empty;
    public string VisualRole { get; set; } = string.Empty;
    public bool Required { get; set; }
    public JsonElement? Default { get; set; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public bool Integer { get; set; }
    public string Format { get; set; } = string.Empty;
    public string[] AllowedValues { get; set; } = [];

    [JsonPropertyName("enum")]
    public string[] EnumValues { get; set; } = [];
}

internal sealed class UiBlockSlot
{
    public string Description { get; set; } = string.Empty;
    public string[] AllowedKinds { get; set; } = [];
    public string XamlItemTemplate { get; set; } = string.Empty;
}

internal sealed class UiPackPreviewContract
{
    public string NamespaceUri { get; set; } = string.Empty;
    public string ClrNamespace { get; set; } = string.Empty;
    public Dictionary<string, UiPackPreviewType> Types { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class UiPackPreviewType
{
    public string BaseKind { get; set; } = string.Empty;
    public string ContentProperty { get; set; } = string.Empty;
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class UiBlockRenderer
{
    public string XamlTemplate { get; set; } = string.Empty;
    public string CodeBehindBaseType { get; set; } = string.Empty;
}

internal sealed class UiBlockInteraction
{
    public string Kind { get; set; } = string.Empty;
    public string CommandProperty { get; set; } = string.Empty;
    public string CommandParameterProperty { get; set; } = string.Empty;
    public string TargetProperty { get; set; } = string.Empty;
    public string LabelProperty { get; set; } = string.Empty;
}

internal sealed class SourceHint
{
    public string Path { get; set; } = string.Empty;
    public string Usage { get; set; } = string.Empty;
}

internal sealed class UiRecipeDefinition : ComposerJsonDocument
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PackId { get; set; } = string.Empty;
    public Dictionary<string, UiRecipeInput> Inputs { get; set; } = new(StringComparer.Ordinal);
    public ComposerPackReference[] RequiredPacks { get; set; } = [];
    public string[] CustomizationGuidance { get; set; } = [];
    public JsonElement ExpandsTo { get; set; }
}

internal sealed class UiRecipeInput
{
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public JsonElement? Default { get; set; }
    public string Description { get; set; } = string.Empty;
    public string[] AllowedValues { get; set; } = [];

    [JsonPropertyName("enum")]
    public string[] EnumValues { get; set; } = [];
}

internal sealed class UiBlueprint : ComposerJsonDocument
{
    public string Name { get; set; } = string.Empty;
    public ComposerPackReference[] Packs { get; set; } = [];
    public string PrimaryPack { get; set; } = string.Empty;
    public Dictionary<string, string> ResourceVariants { get; set; } = new(StringComparer.Ordinal);
    public UiBlueprintNode Layout { get; set; } = new();
    public Dictionary<string, JsonElement> Metadata { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class UiBlueprintNode
{
    public string Kind { get; set; } = string.Empty;
    public string? ElementName { get; set; }
    public string? AutomationId { get; set; }
    public Dictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonElement> Bindings { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, UiBlueprintNode[]> Slots { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonElement> Metadata { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class ComposerPackReference
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool Required { get; set; }
}

internal sealed class SourceLock : ComposerJsonDocument
{
    public SourceLockSource[] Sources { get; set; } = [];
    public Dictionary<string, JsonElement> TransformPolicy { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class SourceLockSource
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string[] Paths { get; set; } = [];
}

internal sealed class PackInstallManifest : ComposerJsonDocument
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public Dictionary<string, JsonElement> Metadata { get; set; } = new(StringComparer.Ordinal);
}
