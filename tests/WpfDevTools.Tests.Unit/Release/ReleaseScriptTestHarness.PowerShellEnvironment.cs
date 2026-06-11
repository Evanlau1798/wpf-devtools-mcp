using System.Diagnostics;

namespace WpfDevTools.Tests.Unit.Release;

internal static partial class ReleaseScriptTestHarness
{
    public static (int ExitCode, string Stdout, string Stderr) RunPowerShellCommand(
        string command,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        TimeSpan? timeout = null)
    {
        var isolatedEnvironmentRoot = CreateShortTempDirectory("e");
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = GetRepoFilePath(".")
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");

        if (environmentOverrides is not null)
        {
            foreach (var pair in environmentOverrides)
            {
                startInfo.Environment[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        EnsureIsolatedProcessEnvironment(startInfo, isolatedEnvironmentRoot, environmentOverrides);
        ClearInheritedReleaseTestEnvironment(startInfo, environmentOverrides);

        if (!startInfo.Environment.ContainsKey("WPFDEVTOOLS_INSTALLER_TEST_MODE"))
        {
            startInfo.Environment["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "1";
        }

        if (string.Equals(startInfo.Environment["WPFDEVTOOLS_INSTALLER_TEST_MODE"], "1", StringComparison.Ordinal) &&
            !startInfo.Environment.ContainsKey("WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA"))
        {
            startInfo.Environment["WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA"] = "1";
        }

        var commandText = string.Equals(startInfo.Environment["WPFDEVTOOLS_INSTALLER_TEST_MODE"], "1", StringComparison.Ordinal)
            ? "$script:WpfDevToolsInstallerTestModeHarnessEnabled = $true; $script:WpfDevToolsInstallerTestModeEnabled = $true; " + command
            : command;
        startInfo.ArgumentList.Add(commandText);

        try
        {
            return RunProcess(startInfo, timeout);
        }
        finally
        {
            DeleteDirectory(isolatedEnvironmentRoot);
        }
    }

    private static (string EnvironmentRoot, bool OwnsEnvironmentRoot) ResolveProcessEnvironmentRoot(
        IReadOnlyDictionary<string, string?>? environmentOverrides)
    {
        if (TryResolveSharedEnvironmentRoot(environmentOverrides, out var sharedEnvironmentRoot))
        {
            return (sharedEnvironmentRoot, false);
        }

        return (CreateShortTempDirectory("e"), true);
    }

    private static bool TryResolveSharedEnvironmentRoot(
        IReadOnlyDictionary<string, string?>? environmentOverrides,
        out string sharedEnvironmentRoot)
    {
        sharedEnvironmentRoot = string.Empty;
        if (environmentOverrides is null ||
            !TryGetPathOverride(environmentOverrides, "APPDATA", out var appDataPath) ||
            !TryGetPathOverride(environmentOverrides, "LOCALAPPDATA", out var localAppDataPath) ||
            !TryGetPathOverride(environmentOverrides, "USERPROFILE", out var userProfilePath))
        {
            return false;
        }

        var appDataRoot = TryGetEnvironmentRootFromAppDataPath(appDataPath, "Roaming");
        var localAppDataRoot = TryGetEnvironmentRootFromAppDataPath(localAppDataPath, "Local");
        if (appDataRoot is null ||
            localAppDataRoot is null ||
            !string.Equals(appDataRoot, localAppDataRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var userProfileRoot = Directory.GetParent(userProfilePath)?.FullName;
        if (string.IsNullOrWhiteSpace(userProfileRoot) ||
            !string.Equals(appDataRoot, userProfileRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        sharedEnvironmentRoot = appDataRoot;
        return true;
    }

    private static bool TryGetPathOverride(
        IReadOnlyDictionary<string, string?> environmentOverrides,
        string key,
        out string path)
    {
        path = string.Empty;
        if (!environmentOverrides.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        path = Path.GetFullPath(value);
        return true;
    }

    private static string? TryGetEnvironmentRootFromAppDataPath(string appDataPath, string leafDirectoryName)
    {
        var leafDirectory = new DirectoryInfo(appDataPath);
        if (!leafDirectory.Name.Equals(leafDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var appDataDirectory = leafDirectory.Parent;
        if (appDataDirectory is null || !appDataDirectory.Name.Equals("AppData", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return appDataDirectory.Parent?.FullName;
    }

    private static void EnsureIsolatedProcessEnvironment(
        ProcessStartInfo startInfo,
        string environmentRoot,
        IReadOnlyDictionary<string, string?>? environmentOverrides)
    {
        var roamingAppData = Path.Combine(environmentRoot, "AppData", "Roaming");
        var localAppData = Path.Combine(environmentRoot, "AppData", "Local");
        var tempPath = Path.Combine(environmentRoot, "Temp");
        Directory.CreateDirectory(roamingAppData);
        Directory.CreateDirectory(localAppData);
        Directory.CreateDirectory(tempPath);

        startInfo.Environment["USERPROFILE"] = environmentOverrides is not null && TryGetPathOverride(environmentOverrides, "USERPROFILE", out var userProfileOverride)
            ? userProfileOverride
            : environmentRoot;
        startInfo.Environment["APPDATA"] = environmentOverrides is not null && TryGetPathOverride(environmentOverrides, "APPDATA", out var appDataOverride)
            ? appDataOverride
            : roamingAppData;
        startInfo.Environment["LOCALAPPDATA"] = environmentOverrides is not null && TryGetPathOverride(environmentOverrides, "LOCALAPPDATA", out var localAppDataOverride)
            ? localAppDataOverride
            : localAppData;
        startInfo.Environment["TEMP"] = environmentOverrides is not null && TryGetPathOverride(environmentOverrides, "TEMP", out var tempOverride)
            ? tempOverride
            : tempPath;
        startInfo.Environment["TMP"] = environmentOverrides is not null && TryGetPathOverride(environmentOverrides, "TMP", out var tmpOverride)
            ? tmpOverride
            : startInfo.Environment["TEMP"];
    }

    private static void ClearInheritedReleaseTestEnvironment(
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string?>? environmentOverrides)
    {
        foreach (var variableName in new[]
                 {
                     "WPFDEVTOOLS_INSTALLER_TEST_MODE",
                     "WPFDEVTOOLS_TEST_SIGNATURE_STATUS",
                     "WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA",
                     "WPFDEVTOOLS_TEST_FORCE_SIGNING_CERTIFICATE_CLEANUP_FAILURE",
                     "WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT",
                     "WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT",
                     "WPFDEVTOOLS_RELEASE_CERTIFICATE_THUMBPRINT",
                     "WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH",
                     "WPFDEVTOOLS_PFX_PASSWORD",
                     "WPFDEVTOOLS_SIGNTOOL_PATH",
                     "WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"
                 })
        {
            if (environmentOverrides is null || !environmentOverrides.ContainsKey(variableName))
            {
                startInfo.Environment.Remove(variableName);
            }
        }
    }
}
