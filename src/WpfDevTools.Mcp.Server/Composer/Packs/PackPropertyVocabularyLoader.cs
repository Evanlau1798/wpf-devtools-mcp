using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal static class PackPropertyVocabularyLoader
{
    private const int MaxVocabularyFileBytes = 1_048_576;
    private const int MaxVocabularyValues = 16_384;

    public static void Hydrate(string packRoot, IReadOnlyList<UiBlockDefinition> blocks)
    {
        foreach (var block in blocks)
        {
            foreach (var (propertyName, property) in block.Properties)
            {
                HydrateProperty(packRoot, block.Kind, propertyName, property);
            }
        }
    }

    private static void HydrateProperty(
        string packRoot,
        string blockKind,
        string propertyName,
        UiBlockProperty property)
    {
        if (string.IsNullOrWhiteSpace(property.AllowedValuesPath))
        {
            return;
        }

        if (property.AllowedValues.Length > 0 || property.EnumValues.Length > 0)
        {
            throw Invalid(blockKind, propertyName, "allowedValuesPath cannot be combined with allowedValues or enum.");
        }

        var relativePath = property.AllowedValuesPath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relativePath))
        {
            throw Invalid(blockKind, propertyName, "allowedValuesPath must be pack-relative.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(packRoot, relativePath));
        if (!IsUnderRoot(packRoot, fullPath))
        {
            throw Invalid(blockKind, propertyName, "allowedValuesPath escapes pack root.");
        }
        if (property.AllowedValuesPath.Contains('\\')
            || !property.AllowedValuesPath.StartsWith("vocabularies/", StringComparison.Ordinal)
            || !property.AllowedValuesPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(blockKind, propertyName, "allowedValuesPath must use a forward-slash path under vocabularies/ and end in .json.");
        }
        var vocabularyRoot = Path.Combine(packRoot, "vocabularies");
        if (!IsUnderRoot(vocabularyRoot, fullPath))
        {
            throw Invalid(blockKind, propertyName, "allowedValuesPath must stay under vocabularies/ after path normalization.");
        }

        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw Invalid(blockKind, propertyName, $"allowedValuesPath does not exist: {property.AllowedValuesPath}.");
        }
        if (file.Length > MaxVocabularyFileBytes)
        {
            throw Invalid(blockKind, propertyName, $"allowedValuesPath exceeds {MaxVocabularyFileBytes} bytes.");
        }

        string[] values;
        try
        {
            values = JsonSerializer.Deserialize<string[]>(File.ReadAllText(fullPath)) ?? [];
        }
        catch (JsonException ex)
        {
            throw Invalid(blockKind, propertyName, $"allowedValuesPath must contain a JSON string array: {ex.Message}");
        }

        if (values.Length == 0 || values.Length > MaxVocabularyValues)
        {
            throw Invalid(blockKind, propertyName, $"allowedValuesPath must contain 1 to {MaxVocabularyValues} values.");
        }
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw Invalid(blockKind, propertyName, "allowedValuesPath cannot contain empty values.");
        }
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw Invalid(blockKind, propertyName, "allowedValuesPath cannot contain ordinal duplicates.");
        }

        property.AllowedValues = values;
    }

    private static InvalidDataException Invalid(string blockKind, string propertyName, string message)
        => new($"Property vocabulary for '{blockKind}.{propertyName}' is invalid: {message}");

    private static bool IsUnderRoot(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
