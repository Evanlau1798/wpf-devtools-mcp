using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Linq;

namespace WpfDevTools.Tests.Integration.TestSupport;

internal static partial class ReleasePackagingTestHarness
{
    private static readonly string RepoRoot = ResolveRepoRoot();

    internal static bool ForceTaskKillFallbackForTesting { get; set; }

    public static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(RepoRoot, relativePath));

    public static string CreateTempDirectory()
    {
        var path = Path.Combine(GetRepoFilePath("tmp"), "release-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }



    public static (int ExitCode, string Stdout, string Stderr) RunPowerShellScript(
        string scriptPath,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        TimeSpan? timeout = null,
        bool scaleTimeout = true)
    {
        var argumentList = arguments.ToList();
        var canReuseSharedEnvironmentRoot = string.Equals(Path.GetFileName(scriptPath), "online-installer.ps1", StringComparison.OrdinalIgnoreCase);
        var (environmentRoot, ownsEnvironmentRoot) = canReuseSharedEnvironmentRoot
            ? ResolveProcessEnvironmentRoot(environmentOverrides)
            : (Path.Combine(GetRepoFilePath("tmp"), "release-integration-env", Guid.NewGuid().ToString("N")), true);
        string? injectedWorkingRoot = null;
        if (string.Equals(Path.GetFileName(scriptPath), "online-installer.ps1", StringComparison.OrdinalIgnoreCase) &&
            !argumentList.Contains("-WorkingRoot", StringComparer.OrdinalIgnoreCase))
        {
            injectedWorkingRoot = ownsEnvironmentRoot
                ? Path.Combine(environmentRoot, "wpf-devtools-working-root")
                : Path.Combine(environmentRoot, "wpf-devtools-working-root", Guid.NewGuid().ToString("N"));
            argumentList.Add("-WorkingRoot");
            argumentList.Add(injectedWorkingRoot);
        }

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

        if (environmentOverrides is not null)
        {
            foreach (var pair in environmentOverrides)
            {
                startInfo.Environment[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        EnsureIsolatedProcessEnvironment(startInfo, environmentRoot, environmentOverrides);
        ClearInheritedWpfDevToolsEnvironment(startInfo, environmentOverrides);

        if (!startInfo.Environment.ContainsKey("WPFDEVTOOLS_INSTALLER_TEST_MODE"))
        {
            startInfo.Environment["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "1";
        }

        if (string.Equals(startInfo.Environment["WPFDEVTOOLS_INSTALLER_TEST_MODE"], "1", StringComparison.Ordinal) &&
            !startInfo.Environment.ContainsKey("WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA"))
        {
            startInfo.Environment["WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA"] = "1";
        }

        if (string.Equals(startInfo.Environment["WPFDEVTOOLS_INSTALLER_TEST_MODE"], "1", StringComparison.Ordinal))
        {
            startInfo.ArgumentList.Add("-Command");
            var isolatedScriptRoot = ShouldIsolateOnlineInstallerTestScript(scriptPath)
                ? Path.Combine(environmentRoot, "script-root", Guid.NewGuid().ToString("N"))
                : null;
            startInfo.ArgumentList.Add(BuildPowerShellScriptInvocation(scriptPath, argumentList, enableInternalTestMode: true, isolatedScriptRoot));
        }
        else
        {
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);

            foreach (var argument in argumentList)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        try
        {
            return RunProcess(startInfo, timeout, scaleTimeout);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(injectedWorkingRoot) && !ownsEnvironmentRoot)
            {
                DeleteDirectory(injectedWorkingRoot);
            }

            if (ownsEnvironmentRoot)
            {
                DeleteDirectory(environmentRoot);
            }
        }
    }

    private static bool ShouldIsolateOnlineInstallerTestScript(string scriptPath)
        => string.Equals(Path.GetFileName(scriptPath), "online-installer.ps1", StringComparison.OrdinalIgnoreCase);

    private static string BuildPowerShellScriptInvocation(
        string scriptPath,
        IReadOnlyList<string> arguments,
        bool enableInternalTestMode,
        string? isolatedScriptRoot)
    {
        var hasParamBlock = HasPowerShellParamBlock(File.ReadAllText(scriptPath));
        var preserveLastExitCode = ShouldPreserveLastExitCode(scriptPath);
        var invocation = new StringBuilder();
        invocation.Append("$sourceScriptPath = ");
        invocation.Append(QuotePowerShellString(scriptPath));
        invocation.Append("; $scriptDirectory = Split-Path -Parent $sourceScriptPath; ");
        if (isolatedScriptRoot is not null)
        {
            invocation.Append("$tempScriptRoot = ");
            invocation.Append(QuotePowerShellString(isolatedScriptRoot));
            invocation.Append("; New-Item -ItemType Directory -Force -Path $tempScriptRoot | Out-Null; $helperSource = Join-Path $scriptDirectory 'installer'; if (Test-Path -LiteralPath $helperSource) { Copy-Item -LiteralPath $helperSource -Destination (Join-Path $tempScriptRoot 'installer') -Recurse -Force }; $tempScriptPath = Join-Path $tempScriptRoot (Split-Path -Leaf $sourceScriptPath); ");
        }
        else
        {
            invocation.Append("$tempScriptPath = Join-Path $scriptDirectory ([System.IO.Path]::GetFileNameWithoutExtension($sourceScriptPath) + '.test-' + [guid]::NewGuid().ToString('N') + '.ps1'); ");
        }
        invocation.Append("$scriptContent = Get-Content -LiteralPath $sourceScriptPath -Raw; ");
        if (enableInternalTestMode)
        {
            invocation.Append("$scriptContent = $scriptContent.Replace('$script:WpfDevToolsInstallerTestModeEnabled = [bool]$script:WpfDevToolsInstallerTestModeEnabled -and [bool]$script:WpfDevToolsInstallerTestModeHarnessEnabled', '$script:WpfDevToolsInstallerTestModeEnabled = $true'); ");
            if (!hasParamBlock)
            {
                invocation.Append("$scriptContent = '$script:WpfDevToolsInstallerTestModeEnabled = $true' + [Environment]::NewLine + $scriptContent; ");
            }
        }
        else
        {
            if (!hasParamBlock)
            {
                invocation.Append("$scriptContent = '$script:WpfDevToolsInstallerTestModeEnabled = $false' + [Environment]::NewLine + $scriptContent; ");
            }
        }

        invocation.Append("try { Set-Content -LiteralPath $tempScriptPath -Value $scriptContent -Encoding UTF8; $global:LASTEXITCODE = 0; & $tempScriptPath");

        foreach (var argument in arguments)
        {
            invocation.Append(' ');
            invocation.Append(FormatPowerShellArgument(argument));
        }

        if (preserveLastExitCode)
        {
            invocation.Append("; $scriptExitCode = if ($global:LASTEXITCODE -is [int]) { $global:LASTEXITCODE } else { 0 }; if ($scriptExitCode -ne 0) { exit $scriptExitCode }; exit 0 } finally { Remove-Item -LiteralPath $tempScriptPath -Force -ErrorAction SilentlyContinue }");
        }
        else
        {
            invocation.Append("; exit 0 } finally { Remove-Item -LiteralPath $tempScriptPath -Force -ErrorAction SilentlyContinue }");
        }

        return invocation.ToString();
    }

    private static bool ShouldPreserveLastExitCode(string scriptPath)
        => true;

    private static bool HasPowerShellParamBlock(string scriptContent)
        => Regex.IsMatch(
            scriptContent,
            @"(?is)^\s*(?:(?:#.*?(?:\r?\n|$))\s*|(?:<#.*?#>\s*)|(?:\[CmdletBinding(?:\([^\]]*\))?\]\s*))*param\s*\(",
            RegexOptions.CultureInvariant);

    private static string QuotePowerShellString(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string FormatPowerShellArgument(string value)
    {
        return value.Length > 0 &&
            value[0] == '-' &&
            value.Length > 1 &&
            value.Skip(1).All(static character => char.IsLetterOrDigit(character) || character is '_' or '-')
            ? value
            : QuotePowerShellString(value);
    }

    public static (int ExitCode, string Stdout, string Stderr) RunPowerShellCommand(
        string command,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        TimeSpan? timeout = null)
    {
        var isolatedEnvironmentRoot = Path.Combine(GetRepoFilePath("tmp"), "release-integration-env", Guid.NewGuid().ToString("N"));
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
        ClearInheritedWpfDevToolsEnvironment(startInfo, environmentOverrides);

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
            return RunProcess(startInfo, timeout, scaleTimeout: true);
        }
        finally
        {
            DeleteDirectory(isolatedEnvironmentRoot);
        }
    }

    public static string CreateFakeCommand(
        string directory,
        string commandName,
        string logPath,
        string trailingCommand = "exit /b 0")
    {
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, commandName + ".cmd");
        File.WriteAllText(
            scriptPath,
            "@echo off" + Environment.NewLine +
            $"echo %*>>\"{logPath}\"" + Environment.NewLine +
            trailingCommand + Environment.NewLine);

        return scriptPath;
    }

    public static string ExtractArchive(string archivePath, string tempRoot)
    {
        var extractRoot = Path.Combine(tempRoot, "extracted");
        ZipFile.ExtractToDirectory(archivePath, extractRoot);
        return extractRoot;
    }

    public static JsonDocument ParseJson(string json)
        => JsonDocument.Parse(json);

    private static (string EnvironmentRoot, bool OwnsEnvironmentRoot) ResolveProcessEnvironmentRoot(
        IReadOnlyDictionary<string, string?>? environmentOverrides)
    {
        if (TryResolveSharedEnvironmentRoot(environmentOverrides, out var sharedEnvironmentRoot))
        {
            return (sharedEnvironmentRoot, false);
        }

        return (Path.Combine(GetRepoFilePath("tmp"), "release-integration-env", Guid.NewGuid().ToString("N")), true);
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
        ReleasePackagingNuGetEnvironment.EnsureStablePackageCache(startInfo, GetRepoFilePath("tmp"));
    }

    private static void ClearInheritedWpfDevToolsEnvironment(
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string?>? environmentOverrides)
    {
        var explicitOverrideKeys = environmentOverrides is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(environmentOverrides.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var variableName in startInfo.Environment.Keys
                     .Where(static key => key.StartsWith("WPFDEVTOOLS_", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            if (!explicitOverrideKeys.Contains(variableName))
            {
                startInfo.Environment.Remove(variableName);
            }
        }
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, ".git")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }

}
