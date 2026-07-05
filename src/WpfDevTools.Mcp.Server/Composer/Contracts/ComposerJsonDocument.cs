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
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string[] Blocks { get; set; } = [];
    public string[] Recipes { get; set; } = [];
}

internal sealed class UiBlockDefinition : ComposerJsonDocument
{
    public string Kind { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Dictionary<string, UiBlockProperty> Properties { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, UiBlockSlot> Slots { get; set; } = new(StringComparer.Ordinal);
    public UiBlockRenderer Renderer { get; set; } = new();
    public SourceHint[] SourceHints { get; set; } = [];
}

internal sealed class UiBlockProperty
{
    public string Type { get; set; } = string.Empty;
    public bool Required { get; set; }
    public JsonElement? Default { get; set; }
}

internal sealed class UiBlockSlot
{
    public string[] AllowedKinds { get; set; } = [];
}

internal sealed class UiBlockRenderer
{
    public string XamlTemplate { get; set; } = string.Empty;
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
    public string PackId { get; set; } = string.Empty;
    public ComposerPackReference[] RequiredPacks { get; set; } = [];
    public JsonElement ExpandsTo { get; set; }
}

internal sealed class UiBlueprint : ComposerJsonDocument
{
    public string Name { get; set; } = string.Empty;
    public ComposerPackReference[] Packs { get; set; } = [];
    public string PrimaryPack { get; set; } = string.Empty;
    public UiBlueprintNode Layout { get; set; } = new();
}

internal sealed class UiBlueprintNode
{
    public string Kind { get; set; } = string.Empty;
    public Dictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, UiBlueprintNode[]> Slots { get; set; } = new(StringComparer.Ordinal);
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
