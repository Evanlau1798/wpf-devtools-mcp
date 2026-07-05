using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfDevTools.Mcp.Server.Composer.Contracts;

internal static class ComposerJsonLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    public static T Load<T>(string filePath, string expectedSchemaVersion)
        where T : ComposerJsonDocument, new()
        => Parse<T>(
            File.ReadAllText(filePath),
            Path.GetFullPath(filePath),
            expectedSchemaVersion);

    public static T Parse<T>(string json, string sourceFilePath, string expectedSchemaVersion)
        where T : ComposerJsonDocument, new()
    {
        using var document = JsonDocument.Parse(json);
        var actualSchemaVersion = document.RootElement.TryGetProperty("schemaVersion", out var schema)
            ? schema.GetString()
            : null;

        if (!string.Equals(actualSchemaVersion, expectedSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Document '{sourceFilePath}' schemaVersion must be {expectedSchemaVersion}; found '{actualSchemaVersion ?? "<missing>"}'.");
        }

        var model = JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidDataException($"Document '{sourceFilePath}' could not be parsed.");
        model.SourceFilePath = sourceFilePath;
        model.JsonPath = "$";
        return model;
    }
}
