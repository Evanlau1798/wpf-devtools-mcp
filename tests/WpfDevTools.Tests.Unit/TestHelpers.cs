using System.Text.Json;

namespace WpfDevTools.Tests.Unit;

/// <summary>
/// Shared test helper methods
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Convert an anonymous object to JsonElement for tool parameter passing
    /// </summary>
    public static JsonElement ToJsonElement(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}

/// <summary>
/// Temporarily skips DLL signature verification for testing purposes.
/// Use with 'using' statement to ensure cleanup.
/// </summary>
public sealed class SkipSignatureCheckScope : IDisposable
{
    private readonly string? _previousValue;
    private const string EnvVarName = "WPFDEVTOOLS_SKIP_SIGNATURE_CHECK";

    public SkipSignatureCheckScope()
    {
        _previousValue = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, "1");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVarName, _previousValue);
    }
}
