using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace WpfDevTools.Tests.Unit.Release;

internal static class ReleaseScriptTestHarness
{
    private static readonly string RepoRoot = ResolveRepoRoot();
    private static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SelfSignedPayloadTimeout = TimeSpan.FromMinutes(3);
    private static readonly ConcurrentDictionary<string, Lazy<CachedPackageArtifacts>> PackageArtifactCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> GeneratedCertificateThumbprints = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<SignedPayloadInfo> SignedPayload =
        new(ResolveSignedPayloadInfo, LazyThreadSafetyMode.ExecutionAndPublication);
    private static int generatedCertificateCleanupRegistered;

    internal static bool ForceTaskKillFallbackForTesting { get; set; }

    [DllImport("Kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, nint securityAttributes);

    private sealed record CachedPackageArtifacts(string PackageDirectoryPath, string ArchivePath, string MetadataDirectoryPath);

    private sealed record SignedPayloadInfo(string Path, string Thumbprint, string Subject);

    private static readonly SignedPayloadInfo UnsignedPayload = new(
        string.Empty,
        "0000000000000000000000000000000000000000",
        "CN=WPFDEVTOOLS UNSIGNED TEST PAYLOAD");

    public static string CreateTempDirectory()
    {
        var path = Path.Combine(GetRepoFilePath("tmp"), "wpf-devtools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Exception? lastException = null;

        static void NormalizeAttributes(string root)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(entry, FileAttributes.Normal);
            }

            File.SetAttributes(root, FileAttributes.Normal);
        }

        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    NormalizeAttributes(path);
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException ex)
            {
                lastException = ex;
                if (!Directory.Exists(path))
                {
                    return;
                }

                if (attempt == 0 && TryQuarantineDirectory(path))
                {
                    return;
                }

                if (attempt < 59)
                {
                    Thread.Sleep(250);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
                if (!Directory.Exists(path))
                {
                    return;
                }

                if (attempt == 0 && TryQuarantineDirectory(path))
                {
                    return;
                }

                if (attempt < 59)
                {
                    Thread.Sleep(250);
                }
            }
        }

        TryQuarantineDirectory(path);

        if (Directory.Exists(path) && lastException is not null)
        {
            System.Diagnostics.Debug.WriteLine($"ReleaseScriptTestHarness: best-effort cleanup skipped for '{path}': {lastException.Message}");
        }
    }

    private static bool TryQuarantineDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return true;
            }

            var quarantineRoot = Path.Combine(GetRepoFilePath("tmp"), "wpf-devtools-tests-pending-delete");
            Directory.CreateDirectory(quarantineRoot);
            var quarantinePath = Path.Combine(quarantineRoot, Path.GetFileName(path) + "-" + Guid.NewGuid().ToString("N"));
            Directory.Move(path, quarantinePath);
            return !Directory.Exists(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string CreatePackageDirectory(string tempRoot, string architecture = "x64", bool useSignedPayload = false)
    {
        var packageDir = Path.Combine(tempRoot, "package");
        if (Directory.Exists(packageDir))
        {
            DeleteDirectory(packageDir);
        }

        var cachedArtifacts = GetCachedPackageArtifacts(architecture, useSignedPayload);
        CopyDirectory(cachedArtifacts.PackageDirectoryPath, packageDir);
        return packageDir;
    }

    public static string CreatePackageArchive(
        string tempRoot,
        string architecture = "x64",
        bool useSignedPayload = false,
        bool isolateArchiveContents = false)
    {
        var archivePath = Path.Combine(tempRoot, $"release_1.2.3_win-{architecture}.zip");
        var cachedArtifacts = GetCachedPackageArtifacts(architecture, useSignedPayload);
        ReplicateFile(cachedArtifacts.ArchivePath, archivePath, preferHardLink: !isolateArchiveContents);
        ReplicateFile(
            Path.Combine(cachedArtifacts.MetadataDirectoryPath, "SHA256SUMS.txt"),
            Path.Combine(tempRoot, "SHA256SUMS.txt"),
            preferHardLink: !isolateArchiveContents);
        ReplicateFile(
            Path.Combine(cachedArtifacts.MetadataDirectoryPath, "release-assets.json"),
            Path.Combine(tempRoot, "release-assets.json"),
            preferHardLink: !isolateArchiveContents);
        return archivePath;
    }

    public static void WriteAdjacentReleaseMetadata(string archivePath, string? publishedAssetName = null)
    {
        var archiveFile = new FileInfo(archivePath);
        if (!archiveFile.Exists)
        {
            throw new FileNotFoundException("Archive for release metadata was not found.", archivePath);
        }

        var assetName = string.IsNullOrWhiteSpace(publishedAssetName)
            ? archiveFile.Name
            : publishedAssetName;
        var versionMatch = System.Text.RegularExpressions.Regex.Match(
            assetName,
            @"^release_(?<version>.+)_win-(?<architecture>x64|x86|arm64)\.zip$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var tag = versionMatch.Success
            ? "v" + versionMatch.Groups["version"].Value
            : "vtest";
        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archivePath)))
            .ToLowerInvariant();
        var metadataRoot = archiveFile.DirectoryName!;

        File.WriteAllText(
            Path.Combine(metadataRoot, "SHA256SUMS.txt"),
            $"{sha256}  {assetName}{Environment.NewLine}");

        var manifest = JsonSerializer.Serialize(
            new
            {
                tag,
                assetCount = 1,
                assets = new[]
                {
                    new
                    {
                        name = assetName,
                        sizeBytes = archiveFile.Length,
                        sha256
                    }
                }
            });
        File.WriteAllText(Path.Combine(metadataRoot, "release-assets.json"), manifest);
    }

    public static void WriteAdjacentReleaseMetadata(
        string archivePath,
        string? signerThumbprint,
        string? signerSubject,
        string? publishedAssetName = null)
    {
        var archiveFile = new FileInfo(archivePath);
        if (!archiveFile.Exists)
        {
            throw new FileNotFoundException("Archive for release metadata was not found.", archivePath);
        }

        var assetName = string.IsNullOrWhiteSpace(publishedAssetName)
            ? archiveFile.Name
            : publishedAssetName;
        var versionMatch = System.Text.RegularExpressions.Regex.Match(
            assetName,
            @"^release_(?<version>.+)_win-(?<architecture>x64|x86|arm64)\.zip$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var tag = versionMatch.Success
            ? "v" + versionMatch.Groups["version"].Value
            : "vtest";
        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archivePath)))
            .ToLowerInvariant();
        var metadataRoot = archiveFile.DirectoryName!;

        File.WriteAllText(
            Path.Combine(metadataRoot, "SHA256SUMS.txt"),
            $"{sha256}  {assetName}{Environment.NewLine}");

        var manifest = JsonSerializer.Serialize(
            new
            {
                tag,
                assetCount = 1,
                assets = new[]
                {
                    new
                    {
                        name = assetName,
                        sizeBytes = archiveFile.Length,
                        sha256,
                        signerThumbprint,
                        signerSubject
                    }
                }
            });
        File.WriteAllText(Path.Combine(metadataRoot, "release-assets.json"), manifest);
    }

    public static (string Thumbprint, string Subject) GetSignedPayloadSigner()
    {
        var signedPayload = SignedPayload.Value;
        return (signedPayload.Thumbprint, signedPayload.Subject);
    }

    public static (string Path, string Thumbprint, string Subject) CreateSelfSignedPayloadForTesting(string tempRoot)
    {
        var signedPayload = CreateSelfSignedPayloadInfo(tempRoot, []);
        return (signedPayload.Path, signedPayload.Thumbprint, signedPayload.Subject);
    }

    public static void CleanupGeneratedCertificateForTesting(string thumbprint)
    {
        CleanupGeneratedCertificate(thumbprint);
    }

    private static CachedPackageArtifacts GetCachedPackageArtifacts(string architecture, bool useSignedPayload)
    {
        var cacheKey = ComputePackageArtifactCacheKey(architecture, useSignedPayload);
        var lazyArtifacts = PackageArtifactCache.GetOrAdd(
            cacheKey,
            _ => new Lazy<CachedPackageArtifacts>(
                () => BuildCachedPackageArtifacts(cacheKey, architecture, useSignedPayload),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyArtifacts.Value;
    }

    private static string ComputePackageArtifactCacheKey(string architecture, bool useSignedPayload)
    {
        var inputs = new List<string>
        {
            "release-cache-v4",
            architecture,
            useSignedPayload ? "signed" : "unsigned",
            GetFileContentHash(GetRepoFilePath("scripts/online-installer.ps1")),
            GetFileContentHash(GetRepoFilePath(Path.Combine("scripts", "tools", "packaging", "run-template.bat"))),
            GetFileContentHash(GetRepoFilePath(Path.Combine("scripts", "installer", "installer-helpers.manifest.json")))
        };

        foreach (var helperFile in GetInstallerHelperFiles())
        {
            inputs.Add(GetFileContentHash(GetRepoFilePath(Path.Combine("scripts", "installer", helperFile))));
        }

        if (useSignedPayload)
        {
            var signerMetadata = SignedPayload.Value;
            inputs.Add(signerMetadata.Thumbprint);
            inputs.Add(signerMetadata.Subject);
            inputs.Add(signerMetadata.Path);
        }
        else
        {
            inputs.Add("unsigned-payload");
        }

        var content = string.Join("\n", inputs);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static CachedPackageArtifacts BuildCachedPackageArtifacts(string cacheKey, string architecture, bool useSignedPayload)
    {
        var cacheRoot = Path.Combine(GetRepoFilePath("tmp"), "release-script-harness-cache", cacheKey);
        var packageDir = Path.Combine(cacheRoot, "package");
        var archiveLayoutDir = Path.Combine(cacheRoot, "archive-layout");
        var archivePath = Path.Combine(cacheRoot, $"release_1.2.3_win-{architecture}.zip");
        var metadataDirectoryPath = cacheRoot;

        if (Directory.Exists(packageDir) &&
            File.Exists(archivePath) &&
            File.Exists(Path.Combine(metadataDirectoryPath, "SHA256SUMS.txt")) &&
            File.Exists(Path.Combine(metadataDirectoryPath, "release-assets.json")) &&
            IsValidArchive(archivePath))
        {
            return new CachedPackageArtifacts(packageDir, archivePath, metadataDirectoryPath);
        }

        if (Directory.Exists(cacheRoot))
        {
            DeleteDirectory(cacheRoot);
        }

        Directory.CreateDirectory(cacheRoot);
        BuildPackageDirectory(packageDir, architecture, useSignedPayload);
        CopyDirectory(packageDir, archiveLayoutDir);

        var archiveBinDir = Path.Combine(archiveLayoutDir, "bin");
        File.Copy(GetRepoFilePath("scripts/online-installer.ps1"), Path.Combine(archiveBinDir, "install.ps1"), overwrite: true);
        File.Copy(GetRepoFilePath("scripts/tools/packaging/run-template.bat"), Path.Combine(archiveLayoutDir, "run.bat"), overwrite: true);

        ZipFile.CreateFromDirectory(archiveLayoutDir, archivePath);
        WriteAdjacentReleaseMetadata(archivePath);
        DeleteDirectory(archiveLayoutDir);

        return new CachedPackageArtifacts(packageDir, archivePath, metadataDirectoryPath);
    }

    private static void BuildPackageDirectory(string packageDir, string architecture, bool useSignedPayload)
    {
        var binDir = Path.Combine(packageDir, "bin");
        var helperDir = Path.Combine(binDir, "installer");
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(helperDir);
        var inspectorNet8Dir = Path.Combine(binDir, "inspectors", "net8.0-windows");
        var inspectorNet48Dir = Path.Combine(binDir, "inspectors", "net48");
        var bootstrapperDir = Path.Combine(binDir, "bootstrapper", architecture);
        var signerMetadata = useSignedPayload ? SignedPayload.Value : UnsignedPayload;
        Directory.CreateDirectory(inspectorNet8Dir);
        Directory.CreateDirectory(inspectorNet48Dir);
        Directory.CreateDirectory(bootstrapperDir);
        WritePackagePayloadFile(Path.Combine(binDir, $"wpf-devtools-{architecture}.exe"), signerMetadata.Path, "stub", useSignedPayload);
        WritePackagePayloadFile(Path.Combine(inspectorNet8Dir, "WpfDevTools.Inspector.dll"), signerMetadata.Path, "net8-inspector", useSignedPayload);
        WritePackagePayloadFile(Path.Combine(inspectorNet48Dir, "WpfDevTools.Inspector.dll"), signerMetadata.Path, "net48-inspector", useSignedPayload);
        WritePackagePayloadFile(Path.Combine(bootstrapperDir, $"WpfDevTools.Bootstrapper.{architecture}.dll"), signerMetadata.Path, "bootstrapper", useSignedPayload);
        File.WriteAllText(
            Path.Combine(binDir, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                name = "wpf-devtools",
                version = "1.2.3",
                architecture,
                runtimeId = architecture == "x86" ? "win-x86" : architecture == "arm64" ? "win-arm64" : "win-x64",
                channel = useSignedPayload ? "release" : "dev",
                buildConfiguration = useSignedPayload ? "Release" : "Debug",
                signaturePolicy = useSignedPayload ? "RequireAuthenticodeSignature" : "DebugTrustedRootSkip",
                entryExecutable = $"bin/wpf-devtools-{architecture}.exe",
                runBatch = "run.bat",
                installScript = "bin/install.ps1",
                signerThumbprint = signerMetadata.Thumbprint,
                signerSubject = signerMetadata.Subject,
                inspector = new
                {
                    net8 = "bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll",
                    net48 = "bin/inspectors/net48/WpfDevTools.Inspector.dll"
                },
                bootstrapper = $"bin/bootstrapper/{architecture}/WpfDevTools.Bootstrapper.{architecture}.dll"
            }));

        CopyInstallerHelperFiles(helperDir);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static string GetFileContentHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static bool IsValidArchive(string archivePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            return archive.Entries.Count >= 0;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

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
            : (Path.Combine(GetRepoFilePath("tmp"), "wpf-devtools-env", Guid.NewGuid().ToString("N")), true);
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
        var isolatedEnvironmentRoot = Path.Combine(GetRepoFilePath("tmp"), "wpf-devtools-env", Guid.NewGuid().ToString("N"));
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

    public static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(RepoRoot, relativePath));

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

        return (Path.Combine(GetRepoFilePath("tmp"), "wpf-devtools-env", Guid.NewGuid().ToString("N")), true);
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

    private static void CopyInstallerHelperFiles(string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var installerDirectory = GetRepoFilePath(Path.Combine("scripts", "installer"));
        var manifestFileName = "installer-helpers.manifest.json";
        File.Copy(
            Path.Combine(installerDirectory, manifestFileName),
            Path.Combine(destinationDirectory, manifestFileName),
            overwrite: true);

        foreach (var helperFile in GetInstallerHelperFiles())
        {
            File.Copy(
                Path.Combine(installerDirectory, helperFile),
                Path.Combine(destinationDirectory, helperFile),
                overwrite: true);
        }
    }

    private static string[] GetInstallerHelperFiles()
    {
        var manifestPath = GetRepoFilePath(Path.Combine("scripts", "installer", "installer-helpers.manifest.json"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return manifest.RootElement.GetProperty("helperFiles")
            .EnumerateArray()
            .Select(static entry => entry.ValueKind == JsonValueKind.Object
                ? entry.GetProperty("path").GetString()
                : entry.GetString())
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Cast<string>()
            .ToArray();
    }

    private static void ReplicateFile(string sourcePath, string destinationPath, bool preferHardLink)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        if (preferHardLink && TryCreateHardLink(destinationPath, sourcePath))
        {
            return;
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static bool TryCreateHardLink(string destinationPath, string sourcePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return CreateHardLink(destinationPath, sourcePath, nint.Zero);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static void WritePackagePayloadFile(string destinationPath, string signedSourcePath, string unsignedContent, bool useSignedPayload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (useSignedPayload)
        {
            File.Copy(signedSourcePath, destinationPath, overwrite: true);
            return;
        }

        File.WriteAllText(destinationPath, unsignedContent);
    }

    private static SignedPayloadInfo ResolveSignedPayloadInfo()
    {
        var errors = new List<string>();
        foreach (var candidatePath in EnumerateSignedPayloadCandidatePaths())
        {
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            if (TryGetSignedPayloadSignerMetadata(candidatePath, out var signer, out var error))
            {
                return new SignedPayloadInfo(candidatePath, signer.Thumbprint, signer.Subject);
            }

            errors.Add(error);
        }

        return CreateSelfSignedPayloadInfo(
            Path.Combine(GetRepoFilePath("tmp"), "release-script-harness-signed-payload", Guid.NewGuid().ToString("N")),
            errors);
    }

    private static SignedPayloadInfo CreateSelfSignedPayloadInfo(string payloadRoot, IReadOnlyCollection<string> discoveryErrors)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                "Could not locate a signed Windows payload that exposes signer metadata. " + string.Join(" | ", discoveryErrors));
        }

        Directory.CreateDirectory(payloadRoot);
        var payloadPath = Path.Combine(payloadRoot, "payload.exe");
        var certificateThumbprintPath = Path.Combine(payloadRoot, "payload.cert-thumbprint.txt");
        File.Copy(ResolveSelfSignedPayloadTemplatePath(), payloadPath, overwrite: true);

        var subject = "CN=WpfDevTools Harness Signed Payload " + Guid.NewGuid().ToString("N");
        var command = string.Join(" ",
            "$ErrorActionPreference = 'Stop';",
            "Remove-TypeData -TypeName System.Security.AccessControl.ObjectSecurity -ErrorAction SilentlyContinue;",
            "Import-Module Microsoft.PowerShell.Security -ErrorAction Stop;",
            "try { Import-Module PKI -ErrorAction Stop } catch { };",
            "if ($null -eq (Get-PSProvider Certificate -ErrorAction SilentlyContinue)) { throw 'Certificate provider is unavailable.' };",
            "if ($null -eq (Get-PSDrive -Name Cert -ErrorAction SilentlyContinue)) { New-PSDrive -Name Cert -PSProvider Certificate -Root '\\' -ErrorAction Stop | Out-Null };",
            "Get-Command New-SelfSignedCertificate -ErrorAction Stop | Out-Null;",
            "$payload = " + QuotePowerShellString(payloadPath) + ";",
            "$thumbprintPath = " + QuotePowerShellString(certificateThumbprintPath) + ";",
            "$subject = " + QuotePowerShellString(subject) + ";",
            "$cert = $null; $success = $false;",
            "try {",
            "$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\\CurrentUser\\My -NotAfter (Get-Date).AddDays(1);",
            "Set-Content -LiteralPath $thumbprintPath -Value $cert.Thumbprint -Encoding ASCII;",
            "$store = [System.Security.Cryptography.X509Certificates.X509Store]::new('Root', 'CurrentUser');",
            "$store.Open('ReadWrite');",
            "try { $store.Add($cert) } finally { $store.Close() };",
            "$signature = Set-AuthenticodeSignature -FilePath $payload -Certificate $cert;",
            "$check = Get-AuthenticodeSignature -FilePath $payload;",
            "if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or $check.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or $null -eq $check.SignerCertificate) { throw \"Self-signed payload signature did not validate. Set=$($signature.Status); Check=$($check.Status).\" };",
            "$success = $true;",
            "[ordered]@{ Thumbprint = $check.SignerCertificate.Thumbprint; Subject = $check.SignerCertificate.Subject; CertificateThumbprint = $cert.Thumbprint } | ConvertTo-Json -Compress",
            "}",
            "finally {",
            "if (-not $success -and $null -ne $cert) { foreach ($storeName in @('Root', 'My')) { $cleanupStore = [System.Security.Cryptography.X509Certificates.X509Store]::new($storeName, 'CurrentUser'); $cleanupStore.Open('ReadWrite'); try { foreach ($cleanupCert in @($cleanupStore.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $cert.Thumbprint, $false))) { $cleanupStore.Remove($cleanupCert) } } finally { $cleanupStore.Close() } } }",
            "}");
        (int ExitCode, string Stdout, string Stderr) result;
        try
        {
            result = RunPowerShellCommand(command, timeout: SelfSignedPayloadTimeout);
        }
        catch
        {
            CleanupGeneratedCertificateFromFile(certificateThumbprintPath);
            throw;
        }

        var generatedThumbprint = RegisterGeneratedCertificateFromFile(certificateThumbprintPath);

        if (result.ExitCode != 0)
        {
            CleanupGeneratedCertificateIfKnown(generatedThumbprint);
            throw new InvalidOperationException(
                "Could not create a self-signed payload for release test harness. " +
                string.Join(" | ", discoveryErrors) + " | " + result.Stderr);
        }

        using var payload = JsonDocument.Parse(result.Stdout);
        var root = payload.RootElement;
        var thumbprint = root.GetProperty("Thumbprint").GetString();
        var signerSubject = root.GetProperty("Subject").GetString();
        var certificateThumbprint = root.GetProperty("CertificateThumbprint").GetString();
        if (string.IsNullOrWhiteSpace(thumbprint) ||
            string.IsNullOrWhiteSpace(signerSubject) ||
            string.IsNullOrWhiteSpace(certificateThumbprint))
        {
            CleanupGeneratedCertificateIfKnown(generatedThumbprint);
            throw new InvalidOperationException("Self-signed payload signer metadata was incomplete.");
        }

        RegisterGeneratedCertificateCleanup(certificateThumbprint);
        return new SignedPayloadInfo(payloadPath, thumbprint, signerSubject);
    }

    private static string? RegisterGeneratedCertificateFromFile(string certificateThumbprintPath)
    {
        var thumbprint = TryReadGeneratedCertificateThumbprint(certificateThumbprintPath);
        if (!string.IsNullOrWhiteSpace(thumbprint))
        {
            RegisterGeneratedCertificateCleanup(thumbprint);
        }

        return thumbprint;
    }

    private static void CleanupGeneratedCertificateFromFile(string certificateThumbprintPath)
    {
        CleanupGeneratedCertificateIfKnown(TryReadGeneratedCertificateThumbprint(certificateThumbprintPath));
    }

    private static string? TryReadGeneratedCertificateThumbprint(string certificateThumbprintPath)
    {
        if (!File.Exists(certificateThumbprintPath))
        {
            return null;
        }

        var thumbprint = File.ReadAllText(certificateThumbprintPath).Trim();
        return string.IsNullOrWhiteSpace(thumbprint) ? null : thumbprint;
    }

    private static void CleanupGeneratedCertificateIfKnown(string? thumbprint)
    {
        if (!string.IsNullOrWhiteSpace(thumbprint))
        {
            CleanupGeneratedCertificate(thumbprint);
        }
    }

    private static string ResolveSelfSignedPayloadTemplatePath()
    {
        foreach (var candidate in EnumerateSelfSignedPayloadTemplatePaths())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate an executable PE file to use as the self-signed test payload template.");
    }

    private static IEnumerable<string?> EnumerateSelfSignedPayloadTemplatePaths()
    {
        yield return Environment.ProcessPath;
        yield return Process.GetCurrentProcess().MainModule?.FileName;

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            yield return Path.Combine(dotnetRoot, "dotnet.exe");
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet",
            "dotnet.exe");
    }

    private static void RegisterGeneratedCertificateCleanup(string thumbprint)
    {
        GeneratedCertificateThumbprints.TryAdd(thumbprint, 0);
        if (Interlocked.Exchange(ref generatedCertificateCleanupRegistered, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupGeneratedCertificates();
        }
    }

    private static void CleanupGeneratedCertificates()
    {
        foreach (var thumbprint in GeneratedCertificateThumbprints.Keys)
        {
            try
            {
                CleanupGeneratedCertificate(thumbprint);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ReleaseScriptTestHarness: certificate cleanup skipped for '{thumbprint}': {ex.Message}");
            }
        }
    }

    private static void CleanupGeneratedCertificate(string thumbprint)
    {
        var command = string.Join(" ",
            "$thumbprint = " + QuotePowerShellString(thumbprint) + ";",
            "foreach ($storeName in @('Root', 'My')) {",
            "$store = [System.Security.Cryptography.X509Certificates.X509Store]::new($storeName, 'CurrentUser');",
            "$store.Open('ReadWrite');",
            "try { foreach ($cert in @($store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $thumbprint, $false))) { $store.Remove($cert) } } finally { $store.Close() }",
            "}");
        RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(10));
    }

    private static IEnumerable<string> EnumerateSignedPayloadCandidatePaths()
    {
        var system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32");
        foreach (var relativePath in new[]
                 {
                     Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"),
                     "notepad.exe",
                     "cmd.exe",
                     "wscript.exe",
                     "cscript.exe",
                     "msiexec.exe",
                     "regedit.exe"
                 })
        {
            yield return Path.Combine(system32, relativePath);
        }

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            yield return Path.Combine(dotnetRoot, "dotnet.exe");
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PowerShell",
            "7",
            "pwsh.exe");
    }

    private static bool TryGetSignedPayloadSignerMetadata(
        string signedSourcePath,
        out (string Thumbprint, string Subject) signer,
        out string error)
    {
        signer = default;
        error = string.Empty;
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            "$sig = Get-AuthenticodeSignature -FilePath '" + signedSourcePath.Replace("'", "''") +
            "'; [ordered]@{ Thumbprint = if ($null -ne $sig.SignerCertificate) { $sig.SignerCertificate.Thumbprint } else { $null }; Subject = if ($null -ne $sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { $null } } | ConvertTo-Json -Compress");

        var result = RunProcess(startInfo, TimeSpan.FromSeconds(10));
        if (result.ExitCode != 0)
        {
            error = $"Failed to resolve signer metadata for {signedSourcePath}: {result.Stderr}";
            return false;
        }

        using var payload = JsonDocument.Parse(result.Stdout);
        var thumbprint = payload.RootElement.GetProperty("Thumbprint").GetString();
        var subject = payload.RootElement.GetProperty("Subject").GetString();
        if (string.IsNullOrWhiteSpace(thumbprint) || string.IsNullOrWhiteSpace(subject))
        {
            error = $"Signed payload {signedSourcePath} did not expose signer metadata.";
            return false;
        }

        signer = (thumbprint, subject);
        return true;
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

    private static (int ExitCode, string Stdout, string Stderr) RunProcess(
        ProcessStartInfo startInfo,
        TimeSpan? timeout)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var effectiveTimeout = timeout ?? DefaultProcessTimeout;
        var timeoutMilliseconds = effectiveTimeout.TotalMilliseconds > int.MaxValue
            ? int.MaxValue
            : (int)Math.Ceiling(effectiveTimeout.TotalMilliseconds);
        var timeoutMessage = $"PowerShell command timed out after {effectiveTimeout.TotalSeconds:0.###} second(s).";

        if (!process.WaitForExit(timeoutMilliseconds))
        {
            string? cleanupFailure = null;
            try
            {
                TryKillProcessTree(process);
            }
            catch (Exception ex)
            {
                cleanupFailure = ex.Message;
            }

            try
            {
                process.WaitForExit(2000);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException(
                string.IsNullOrWhiteSpace(cleanupFailure)
                    ? timeoutMessage
                    : timeoutMessage + " Cleanup failed: " + cleanupFailure);
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stderr) && !string.IsNullOrWhiteSpace(stdout))
        {
            stderr = stdout;
        }

        return (process.ExitCode, stdout, stderr);
    }

    private static void TryKillProcessTree(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        Exception? managedKillFailure = null;
        try
        {
            if (ForceTaskKillFallbackForTesting)
            {
                throw new InvalidOperationException("Forced taskkill fallback for testing.");
            }

            process.Kill(entireProcessTree: true);
            if (WaitForExit(process, 5000))
            {
                return;
            }

            managedKillFailure = new InvalidOperationException(
                $"Managed process-tree kill did not terminate PID {process.Id} within 5000 ms.");
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return;
        }
        catch (Exception ex)
        {
            managedKillFailure = ex;
        }

        if (TryTaskKillProcessTree(process, out var fallbackFailure) && WaitForExit(process, 5000))
        {
            return;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(fallbackFailure)
                ? $"Failed to terminate timed-out PowerShell process tree for PID {process.Id}."
                : $"Failed to terminate timed-out PowerShell process tree for PID {process.Id}. {fallbackFailure}",
            managedKillFailure);
    }

    private static bool TryTaskKillProcessTree(Process process, out string failureMessage)
    {
        failureMessage = string.Empty;
        if (process.HasExited)
        {
            return true;
        }

        try
        {
            using var taskKill = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/PID {process.Id} /T /F",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            taskKill.Start();
            if (!taskKill.WaitForExit(5000))
            {
                failureMessage = "taskkill.exe did not exit within 5000 ms.";
                return false;
            }

            var stdout = taskKill.StandardOutput.ReadToEnd().Trim();
            var stderr = taskKill.StandardError.ReadToEnd().Trim();
            if (taskKill.ExitCode != 0 && !process.HasExited)
            {
                failureMessage = $"taskkill.exe exited with code {taskKill.ExitCode}. stdout: {stdout}; stderr: {stderr}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            failureMessage = ex.Message;
            return false;
        }
    }

    private static bool WaitForExit(Process process, int milliseconds)
    {
        try
        {
            return process.HasExited || process.WaitForExit(milliseconds);
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
