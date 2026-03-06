using System.Reflection;

namespace WpfDevTools.Mcp.Server;

internal static class ServerMetadata
{
    private static readonly Assembly Assembly = typeof(ServerMetadata).Assembly;

    internal static string GetDisplayVersion()
    {
        var informationalVersion = Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            throw new InvalidOperationException("Assembly informational version is missing for WpfDevTools.Mcp.Server.");
        }

        var version = informationalVersion;
        var buildMetadataIndex = version.IndexOf('+');

        return buildMetadataIndex >= 0
            ? version[..buildMetadataIndex]
            : version;
    }
}
