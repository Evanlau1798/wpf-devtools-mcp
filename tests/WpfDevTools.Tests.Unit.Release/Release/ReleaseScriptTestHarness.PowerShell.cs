using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WpfDevTools.Tests.Unit.Release;

internal static partial class ReleaseScriptTestHarness
{
    public static string CreateFakeCommand(string directory, string commandName, string logPath)
    {
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, commandName + ".cmd");
        File.WriteAllText(
            scriptPath,
            "@echo off" + Environment.NewLine +
            "setlocal EnableDelayedExpansion" + Environment.NewLine +
            "set \"STATE_PATH=" + logPath + ".state\"" + Environment.NewLine +
            $"echo %*>>\"{logPath}\"" + Environment.NewLine +
            "for %%A in (%*) do set \"LAST_ARG=%%~A\"" + Environment.NewLine +
            "if /I \"%1 %2\"==\"mcp add\" >\"%STATE_PATH%\" echo wpf-devtools !LAST_ARG!" + Environment.NewLine +
            "if /I \"%1 %2\"==\"mcp list\" if exist \"%STATE_PATH%\" type \"%STATE_PATH%\"" + Environment.NewLine +
            "exit /b 0" + Environment.NewLine);

        return scriptPath;
    }

    public static (int ExitCode, string Stdout, string Stderr) RunPowerShellScript(
        string scriptPath,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        TimeSpan? timeout = null)
    {
        var argumentList = arguments.ToList();
        var canReuseSharedEnvironmentRoot = string.Equals(Path.GetFileName(scriptPath), "online-installer.ps1", StringComparison.OrdinalIgnoreCase);
        var (environmentRoot, ownsEnvironmentRoot) = canReuseSharedEnvironmentRoot
            ? ResolveProcessEnvironmentRoot(environmentOverrides)
            : (CreateShortTempDirectory("e"), true);
        string? injectedWorkingRoot = null;
        if (string.Equals(Path.GetFileName(scriptPath), "online-installer.ps1", StringComparison.OrdinalIgnoreCase) &&
            !argumentList.Contains("-WorkingRoot", StringComparer.OrdinalIgnoreCase))
        {
            injectedWorkingRoot = ownsEnvironmentRoot
                ? Path.Combine(environmentRoot, WorkingRootDirectoryName)
                : Path.Combine(environmentRoot, WorkingRootDirectoryName, Guid.NewGuid().ToString("N"));
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

        if (string.Equals(startInfo.Environment["WPFDEVTOOLS_INSTALLER_TEST_MODE"], "1", StringComparison.Ordinal) &&
            !startInfo.Environment.ContainsKey("WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED"))
        {
            startInfo.Environment["WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED"] = "0";
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
            return RunProcess(startInfo, timeout);
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
        invocation.Append(GetPowerShellProcessBootstrapCommand());
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

        if (string.Equals(startInfo.Environment["WPFDEVTOOLS_INSTALLER_TEST_MODE"], "1", StringComparison.Ordinal) &&
            !startInfo.Environment.ContainsKey("WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED"))
        {
            startInfo.Environment["WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED"] = "0";
        }

        var commandText = string.Equals(startInfo.Environment["WPFDEVTOOLS_INSTALLER_TEST_MODE"], "1", StringComparison.Ordinal)
            ? GetPowerShellProcessBootstrapCommand() + "$script:WpfDevToolsInstallerTestModeHarnessEnabled = $true; $script:WpfDevToolsInstallerTestModeEnabled = $true; " + command
            : GetPowerShellProcessBootstrapCommand() + command;
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

    private static string GetPowerShellProcessBootstrapCommand()
        => string.Join(" ",
            "Remove-TypeData -TypeName System.Security.AccessControl.ObjectSecurity -ErrorAction SilentlyContinue;",
            "Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop;",
            "Import-Module Microsoft.PowerShell.Security -ErrorAction Stop;",
            "if ($null -ne (Get-PSProvider Certificate -ErrorAction SilentlyContinue) -and $null -eq (Get-PSDrive -Name Cert -ErrorAction SilentlyContinue)) { New-PSDrive -Name Cert -PSProvider Certificate -Root '\\' -ErrorAction Stop | Out-Null };");

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
                     "WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED",
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
