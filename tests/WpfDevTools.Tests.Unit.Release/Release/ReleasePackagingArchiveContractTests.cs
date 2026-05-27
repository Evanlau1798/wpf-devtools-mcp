using System.IO.Compression;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class ReleasePackagingContractTests
{
    [Fact]
    public void InvokeArchiveCreation_ShouldWriteAllPackageEntriesWithCanonicalNames()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = Path.Combine(tempRoot, "package");
            var binDir = Path.Combine(packageDir, "bin");
            var archivePath = Path.Combine(tempRoot, "release.zip");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(packageDir, "run.bat"), "run");
            File.WriteAllText(Path.Combine(binDir, "install.ps1"), "installer");

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                BuildArchiveCreationCommand(),
                new Dictionary<string, string?>
                {
                    ["NATIVE_SCRIPT_PATH"] = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.Native.ps1"),
                    ["PACKAGE_DIR"] = packageDir,
                    ["ARCHIVE_PATH"] = archivePath
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            var entries = ReadArchiveEntries(archivePath);
            entries.Should().Contain("run.bat");
            entries.Should().Contain("bin/install.ps1");
            entries.Should().OnlyContain(entry => !entry.Contains('\\'));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeArchiveCreation_ShouldNormalizeRequiredRelativePathsWithoutCreatingDuplicates()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = Path.Combine(tempRoot, "package");
            var binDir = Path.Combine(packageDir, "bin");
            var archivePath = Path.Combine(tempRoot, "release.zip");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(binDir, "install.ps1"), "installer");

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                BuildArchiveCreationWithDuplicateRequiredPathsCommand(),
                new Dictionary<string, string?>
                {
                    ["NATIVE_SCRIPT_PATH"] = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.Native.ps1"),
                    ["PACKAGE_DIR"] = packageDir,
                    ["ARCHIVE_PATH"] = archivePath
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            var entries = ReadArchiveEntries(archivePath);
            entries.Should().Contain("bin/install.ps1");
            entries.Should().NotContain("bin\\install.ps1");
            entries.Select(entry => entry.Replace('\\', '/'))
                .Should().ContainSingle(entry => entry == "bin/install.ps1");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task InvokeArchiveCreation_ShouldRetryWhenPackageEntryIsTemporarilyLocked()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        FileStream? lockStream = null;
        try
        {
            var packageDir = Path.Combine(tempRoot, "package");
            var binDir = Path.Combine(packageDir, "bin");
            var archivePath = Path.Combine(tempRoot, "release.zip");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(packageDir, "run.bat"), "run");
            var lockedFilePath = Path.Combine(binDir, "install.ps1");
            File.WriteAllText(lockedFilePath, "installer");

            lockStream = new FileStream(lockedFilePath, FileMode.Open, FileAccess.Read, FileShare.None);
            var markerPath = Path.Combine(tempRoot, "archive-started.marker");
            var releaseLockTask = ReleaseLockAfterMarkerAsync(markerPath, () =>
            {
                lockStream.Dispose();
                lockStream = null;
            });

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                BuildArchiveCreationCommand(markerPath),
                new Dictionary<string, string?>
                {
                    ["NATIVE_SCRIPT_PATH"] = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.Native.ps1"),
                    ["MARKER_PATH"] = markerPath,
                    ["PACKAGE_DIR"] = packageDir,
                    ["ARCHIVE_PATH"] = archivePath
                });

            await releaseLockTask;
            result.ExitCode.Should().Be(0, result.Stderr);
            ReadArchiveEntries(archivePath).Should().Contain("bin/install.ps1");
        }
        finally
        {
            lockStream?.Dispose();
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task RemovePathIfExists_ShouldRetryWhenPackageFileIsTemporarilyLocked()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        FileStream? lockStream = null;
        try
        {
            var packageDir = Path.Combine(tempRoot, "package");
            var binDir = Path.Combine(packageDir, "bin");
            Directory.CreateDirectory(binDir);
            var lockedFilePath = Path.Combine(binDir, "install.ps1");
            File.WriteAllText(lockedFilePath, "installer");

            lockStream = new FileStream(lockedFilePath, FileMode.Open, FileAccess.Read, FileShare.None);
            var markerPath = Path.Combine(tempRoot, "remove-started.marker");
            var releaseLockTask = ReleaseLockAfterMarkerAsync(markerPath, () =>
            {
                lockStream.Dispose();
                lockStream = null;
            });

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                """
                . $env:CORE_SCRIPT_PATH
                $script:RemoveFailureRecorded = $false
                function Remove-Item {
                    param(
                        [string]$LiteralPath,
                        [switch]$Recurse,
                        [switch]$Force,
                        [System.Management.Automation.ActionPreference]$ErrorAction
                    )

                    try {
                        Microsoft.PowerShell.Management\Remove-Item @PSBoundParameters
                    }
                    catch {
                        if (-not $script:RemoveFailureRecorded) {
                            $script:RemoveFailureRecorded = $true
                            Set-Content -LiteralPath $env:MARKER_PATH -Value 'started' -Encoding UTF8
                        }

                        throw
                    }
                }

                Remove-PathIfExists -Path $env:PACKAGE_DIR
                """,
                new Dictionary<string, string?>
                {
                    ["CORE_SCRIPT_PATH"] = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.Core.ps1"),
                    ["MARKER_PATH"] = markerPath,
                    ["PACKAGE_DIR"] = packageDir
                });

            await releaseLockTask;
            result.ExitCode.Should().Be(0, result.Stderr);
            Directory.Exists(packageDir).Should().BeFalse();
        }
        finally
        {
            lockStream?.Dispose();
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PublishReleaseScript_ShouldIncludeInstallerHelperEntriesAfterArchiveCreation()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var testRepo = CreateMinimalSkipBuildReleaseRepo(tempRoot, "x64");
            var sdkDirectory = CreateArchiveTestWindowsSdk(tempRoot, "10.0.26100.0", "x64");
            var fakeMsbuild = CreateArchiveTestVisualStudioToolchain(tempRoot, "14.44.35207", "x64");

            var toolRoot = Path.Combine(tempRoot, "tools");
            Directory.CreateDirectory(toolRoot);
            var fakeDotnet = Path.Combine(toolRoot, "dotnet.cmd");
            File.WriteAllText(fakeDotnet, "@echo off\r\nexit /b 0\r\n");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                testRepo.PackagingScriptPath,
                ["-Configuration", "Release", "-Architectures", "x64", "-OutputRoot", testRepo.OutputRoot, "-SkipBuild"],
                new Dictionary<string, string?>
                {
                    ["PATH"] = toolRoot + ";" + Environment.GetEnvironmentVariable("PATH"),
                    ["WindowsSDKDir"] = sdkDirectory,
                    ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeMsbuild,
                    ["WPFDEVTOOLS_TEST_SIGNATURE_STATUS"] = "Valid"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            var archivePath = Directory.GetFiles(testRepo.OutputRoot, "release_*_win-x64.zip").Single();
            var entries = ReadArchiveEntries(archivePath);
            entries.Should().Contain("bin/installer/installer-helpers.manifest.json");
            entries.Should().Contain("bin/installer/Installer.Uninstall.ps1");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PublishReleaseScript_ShouldWritePortableSlashSeparatedArchiveEntryNames()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var testRepo = CreateMinimalSkipBuildReleaseRepo(tempRoot, "x64");
            var sdkDirectory = CreateArchiveTestWindowsSdk(tempRoot, "10.0.26100.0", "x64");
            var fakeMsbuild = CreateArchiveTestVisualStudioToolchain(tempRoot, "14.44.35207", "x64");
            var toolRoot = Path.Combine(tempRoot, "tools");
            Directory.CreateDirectory(toolRoot);
            var fakeDotnet = Path.Combine(toolRoot, "dotnet.cmd");
            File.WriteAllText(fakeDotnet, "@echo off\r\nexit /b 0\r\n");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                testRepo.PackagingScriptPath,
                ["-Configuration", "Release", "-Architectures", "x64", "-OutputRoot", testRepo.OutputRoot, "-SkipBuild"],
                new Dictionary<string, string?>
                {
                    ["PATH"] = toolRoot + ";" + Environment.GetEnvironmentVariable("PATH"),
                    ["WindowsSDKDir"] = sdkDirectory,
                    ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeMsbuild,
                    ["WPFDEVTOOLS_TEST_SIGNATURE_STATUS"] = "Valid"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            var archivePath = Directory.GetFiles(testRepo.OutputRoot, "release_*_win-x64.zip").Single();
            var entries = ReadArchiveEntries(archivePath);
            entries.Should().Contain("bin/install.ps1");
            entries.Should().OnlyContain(entry => !entry.Contains('\\'));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string BuildArchiveCreationCommand(string? markerPath = null)
        => markerPath is null
            ? """
            . $env:NATIVE_SCRIPT_PATH
            Invoke-ArchiveCreation `
                -PackageDirectory $env:PACKAGE_DIR `
                -ArchivePath $env:ARCHIVE_PATH
            """
            : """
            . $env:NATIVE_SCRIPT_PATH
            $script:ArchiveFailureRecorded = $false
            $originalNewReleaseArchive = ${function:New-ReleaseArchive}
            function New-ReleaseArchive {
                param(
                    [Parameter(Mandatory)] [string]$PackageDirectory,
                    [Parameter(Mandatory)] [string]$ArchivePath,
                    [string[]]$EntryNames = @()
                )

                try {
                    & $originalNewReleaseArchive @PSBoundParameters
                }
                catch {
                    if (-not $script:ArchiveFailureRecorded) {
                        $script:ArchiveFailureRecorded = $true
                        Set-Content -LiteralPath $env:MARKER_PATH -Value 'started' -Encoding UTF8
                    }

                    throw
                }
            }

            Invoke-ArchiveCreation `
                -PackageDirectory $env:PACKAGE_DIR `
                -ArchivePath $env:ARCHIVE_PATH
            """;

    private static string BuildArchiveCreationWithDuplicateRequiredPathsCommand()
        => """
            . $env:NATIVE_SCRIPT_PATH
            Invoke-ArchiveCreation `
                -PackageDirectory $env:PACKAGE_DIR `
                -ArchivePath $env:ARCHIVE_PATH `
                -RequiredRelativePaths @('bin\install.ps1', '/bin/install.ps1')
            """;

    private static string[] ReadArchiveEntries(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        return archive.Entries.Select(static entry => entry.FullName).ToArray();
    }

    private static async Task ReleaseLockAfterMarkerAsync(string markerPath, Action releaseLock)
    {
        var timeout = ReleaseScriptTestHarness.ScaleTimeout(TimeSpan.FromSeconds(10));
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (!File.Exists(markerPath))
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                releaseLock();
                throw new TimeoutException(
                    $"The packaging retry test marker was not written within {timeout.TotalSeconds:0.###} second(s).");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        await Task.Delay(TimeSpan.FromMilliseconds(1800));
        releaseLock();
    }

    private static string CreateArchiveTestVisualStudioToolchain(
        string tempRoot,
        string toolsVersion,
        string architecture)
    {
        var visualStudioRoot = Path.Combine(tempRoot, "VS");
        var msbuildDirectory = Path.Combine(visualStudioRoot, "MSBuild", "Current", "Bin");
        var toolsDirectory = Path.Combine(visualStudioRoot, "VC", "Tools", "MSVC", toolsVersion);
        Directory.CreateDirectory(msbuildDirectory);
        Directory.CreateDirectory(Path.Combine(toolsDirectory, "include"));
        Directory.CreateDirectory(Path.Combine(toolsDirectory, "lib", architecture));
        var compilerDirectory = Path.Combine(toolsDirectory, "bin", "HostX64", architecture);
        Directory.CreateDirectory(compilerDirectory);
        File.WriteAllText(Path.Combine(compilerDirectory, "cl.exe"), string.Empty);
        File.WriteAllText(Path.Combine(compilerDirectory, "link.exe"), string.Empty);
        var msbuildPath = Path.Combine(msbuildDirectory, "MSBuild.cmd");
        File.WriteAllText(msbuildPath, "@echo off\r\nexit /b 0\r\n");
        return msbuildPath;
    }

    private static string CreateArchiveTestWindowsSdk(
        string tempRoot,
        string sdkVersion,
        string architecture)
    {
        var sdkDirectory = Path.Combine(tempRoot, "Windows Kits", "10");
        foreach (var includeName in new[] { "ucrt", "shared", "um", "winrt", "cppwinrt" })
        {
            Directory.CreateDirectory(Path.Combine(sdkDirectory, "Include", sdkVersion, includeName));
        }

        Directory.CreateDirectory(Path.Combine(sdkDirectory, "Lib", sdkVersion, "ucrt", architecture));
        Directory.CreateDirectory(Path.Combine(sdkDirectory, "Lib", sdkVersion, "um", architecture));
        var sdkHostToolsDirectory = Path.Combine(sdkDirectory, "bin", sdkVersion, "x64");
        Directory.CreateDirectory(sdkHostToolsDirectory);
        File.WriteAllText(Path.Combine(sdkHostToolsDirectory, "rc.exe"), string.Empty);
        return sdkDirectory;
    }
}
