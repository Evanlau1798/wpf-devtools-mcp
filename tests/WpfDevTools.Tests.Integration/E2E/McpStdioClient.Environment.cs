using System.IO;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed partial class McpStdioClient
{
    internal static IReadOnlyDictionary<string, string> CreateProcessEnvironment(
        IReadOnlyDictionary<string, string>? environmentVariables,
        string defaultTempRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultTempRoot);

        var mergedEnvironment = CreateMergedEnvironment(environmentVariables);
        var effectiveTempRoot = ResolveTempRoot(mergedEnvironment) ?? defaultTempRoot;
        if (!mergedEnvironment.TryGetValue("TEMP", out var tempRoot) ||
            string.IsNullOrWhiteSpace(tempRoot))
        {
            mergedEnvironment["TEMP"] = effectiveTempRoot;
        }

        if (!mergedEnvironment.TryGetValue("TMP", out var tmpRoot) ||
            string.IsNullOrWhiteSpace(tmpRoot))
        {
            mergedEnvironment["TMP"] = effectiveTempRoot;
        }

        return mergedEnvironment;
    }

    internal static Dictionary<string, string> CreateMergedEnvironment(
        IReadOnlyDictionary<string, string>? environmentVariables)
    {
        var mergedEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (environmentVariables == null)
        {
            return mergedEnvironment;
        }

        foreach (var environmentVariable in environmentVariables)
        {
            mergedEnvironment[environmentVariable.Key] = environmentVariable.Value;
        }

        return mergedEnvironment;
    }

    internal static string? ResolveTempRoot(IReadOnlyDictionary<string, string>? environmentVariables)
    {
        if (environmentVariables == null)
        {
            return null;
        }

        if (environmentVariables.TryGetValue("TEMP", out var tempRoot) &&
            !string.IsNullOrWhiteSpace(tempRoot))
        {
            return tempRoot;
        }

        if (environmentVariables.TryGetValue("TMP", out var tempRootFromTmp) &&
            !string.IsNullOrWhiteSpace(tempRootFromTmp))
        {
            return tempRootFromTmp;
        }

        return null;
    }

    private void DeleteOwnedTempDirectory()
    {
        if (string.IsNullOrWhiteSpace(_ownedTempDirectory))
        {
            return;
        }

        try
        {
            if (Directory.Exists(_ownedTempDirectory))
            {
                Directory.Delete(_ownedTempDirectory, recursive: true);
            }
        }
        catch
        {
        }
        finally
        {
            _ownedTempDirectory = null;
        }
    }

    private static IReadOnlyCollection<string> GetTempDirectoriesToEnsure(
        IReadOnlyDictionary<string, string> environmentVariables)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (environmentVariables.TryGetValue("TEMP", out var tempRoot) &&
            !string.IsNullOrWhiteSpace(tempRoot))
        {
            directories.Add(tempRoot);
        }

        if (environmentVariables.TryGetValue("TMP", out var tmpRoot) &&
            !string.IsNullOrWhiteSpace(tmpRoot))
        {
            directories.Add(tmpRoot);
        }

        return directories;
    }
}
