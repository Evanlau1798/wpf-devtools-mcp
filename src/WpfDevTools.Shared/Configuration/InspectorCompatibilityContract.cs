using System.Reflection;

namespace WpfDevTools.Shared.Configuration;

/// <summary>
/// Shared compatibility contract between the MCP server and Inspector host.
/// Existing host reuse is only allowed when both sides agree on these values.
/// </summary>
public static class InspectorCompatibilityContract
{
    /// <summary>
    /// Version of the inspector request/response compatibility contract.
    /// Increment when connect-time host reuse requires a newer handshake shape.
    /// </summary>
    public const int ProtocolVersion = 1;

    /// <summary>
    /// Returns a repository-level build fingerprint that is stable across assemblies
    /// produced from the same source revision.
    /// </summary>
    public static string GetBuildFingerprint(Type anchorType)
    {
        if (anchorType == null)
        {
            throw new ArgumentNullException(nameof(anchorType));
        }

        var assembly = typeof(InspectorCompatibilityContract).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var resolvedInformationalVersion = informationalVersion!;
            var separatorIndex = resolvedInformationalVersion.IndexOf('+');
            return separatorIndex >= 0 && separatorIndex < resolvedInformationalVersion.Length - 1
                ? resolvedInformationalVersion.Substring(separatorIndex + 1)
                : resolvedInformationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}