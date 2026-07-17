using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static class ViewModelBindingRequirementBuilder
{
    public static IReadOnlyList<ViewModelBindingRequirement> Build(
        PackRegistry registry,
        string blueprintJson)
    {
        var blueprint = ComposerJsonLoader.Parse<UiBlueprint>(
            blueprintJson,
            "<inline-blueprint>",
            UiComposerSchemaVersions.UiBlueprint);
        var blocks = LoadBlocks(registry, blueprint.Packs);
        var usages = new List<ViewModelBindingUsage>();
        Collect(blueprint.Layout, "$.layout", blocks, usages);

        return usages
            .GroupBy(
                usage => usage.BindingPath is null
                    ? "raw\0" + usage.RawBinding
                    : "path\0" + usage.BindingPath,
                StringComparer.Ordinal)
            .Select(group => new ViewModelBindingRequirement(
                group.First().BindingPath is null ? "path-unresolved" : "resolved",
                group.First().BindingPath,
                group.Select(usage => usage.RawBinding)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                group.OrderBy(usage => usage.JsonPath, StringComparer.Ordinal).ToArray()))
            .OrderBy(requirement => requirement.BindingStatus, StringComparer.Ordinal)
            .ThenBy(requirement => requirement.BindingPath ?? requirement.RawBindings[0], StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, UiBlockDefinition> LoadBlocks(
        PackRegistry registry,
        IReadOnlyList<ComposerPackReference> packReferences)
    {
        var requested = packReferences
            .Select(reference => (reference.Id, reference.Version))
            .ToHashSet();
        return registry.ListPacks().Packs
            .Where(pack => requested.Contains((pack.Id, pack.Version)))
            .SelectMany(pack => ComposerPackLoader.Load(pack.RootPath).Blocks)
            .GroupBy(block => block.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    private static void Collect(
        UiBlueprintNode node,
        string jsonPath,
        IReadOnlyDictionary<string, UiBlockDefinition> blocks,
        List<ViewModelBindingUsage> usages)
    {
        if (blocks.TryGetValue(node.Kind, out var block))
        {
            foreach (var (propertyName, value) in node.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (value.ValueKind != JsonValueKind.String
                    || !block.Properties.TryGetValue(propertyName, out var property))
                {
                    continue;
                }

                var rawBinding = value.GetString() ?? string.Empty;
                var isBindingExpression = TryNormalizeBindingPath(rawBinding, out var bindingPath);
                if (!isBindingExpression)
                {
                    continue;
                }

                usages.Add(new ViewModelBindingUsage(
                    $"{jsonPath}.properties.{propertyName}",
                    node.Kind,
                    propertyName,
                    property.Type,
                    rawBinding,
                    bindingPath));
            }
        }

        foreach (var (slotName, children) in node.Slots.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            for (var index = 0; index < children.Length; index++)
            {
                Collect(children[index], $"{jsonPath}.slots.{slotName}[{index}]", blocks, usages);
            }
        }
    }

    private static bool TryNormalizeBindingPath(string value, out string? path)
    {
        path = null;
        var binding = value.Trim();
        if (!binding.StartsWith("{Binding", StringComparison.Ordinal)
            || !binding.EndsWith('}'))
        {
            return false;
        }

        var arguments = SplitArguments(binding["{Binding".Length..^1]);
        foreach (var argument in arguments)
        {
            var separator = argument.IndexOf('=');
            if (separator > 0
                && string.Equals(argument[..separator].Trim(), "Path", StringComparison.Ordinal))
            {
                path = NormalizePath(argument[(separator + 1)..]);
                return true;
            }
        }

        var positional = arguments.FirstOrDefault(argument => !argument.Contains('='));
        path = positional is null ? null : NormalizePath(positional);
        return true;
    }

    private static IReadOnlyList<string> SplitArguments(string value)
    {
        var arguments = new List<string>();
        var start = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        var quote = '\0';
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            braceDepth += character == '{' ? 1 : character == '}' ? -1 : 0;
            bracketDepth += character == '[' ? 1 : character == ']' ? -1 : 0;
            if (character == ',' && braceDepth == 0 && bracketDepth == 0)
            {
                arguments.Add(value[start..index].Trim());
                start = index + 1;
            }
        }

        var final = value[start..].Trim();
        if (final.Length > 0)
        {
            arguments.Add(final);
        }
        return arguments;
    }

    private static string? NormalizePath(string value)
    {
        var path = value.Trim().Trim('\'', '"');
        return path.Length == 0 ? null : path;
    }
}

internal sealed record ViewModelBindingRequirement(
    string BindingStatus,
    string? BindingPath,
    IReadOnlyList<string> RawBindings,
    IReadOnlyList<ViewModelBindingUsage> Usages);

internal sealed record ViewModelBindingUsage(
    string JsonPath,
    string BlockKind,
    string PropertyName,
    string DeclaredPropertyType,
    string RawBinding,
    string? BindingPath);
