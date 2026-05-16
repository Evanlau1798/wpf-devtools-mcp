using System.IO.Compression;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class ReleasePackagingContractTests
{
    [Fact]
    public void InvokeArchiveCreation_ShouldRepairMissingRequiredEntriesAfterArchiveCmdletReturnsSuccess()
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
                BuildPartialArchiveCommand("bin/install.ps1"),
                new Dictionary<string, string?>
                {
                    ["NATIVE_SCRIPT_PATH"] = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.Native.ps1"),
                    ["PACKAGE_DIR"] = packageDir,
                    ["ARCHIVE_PATH"] = archivePath
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            ReadArchiveEntries(archivePath).Should().Contain("bin/install.ps1");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeArchiveCreation_ShouldNotDuplicateExistingSlashEntriesWithBackslashEntries()
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
                BuildSlashEntryArchiveCommand(),
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
    public void PublishReleaseScript_ShouldRepairMissingInstallerHelperEntriesAfterArchiveCreation()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var testRepo = CreateMinimalSkipBuildReleaseRepo(tempRoot, "x64");
            var sdkDirectory = CreateArchiveTestWindowsSdk(tempRoot, "10.0.26100.0", "x64");
            var fakeMsbuild = CreateArchiveTestVisualStudioToolchain(tempRoot, "14.44.35207", "x64");
            var packagingRoot = Path.GetDirectoryName(testRepo.PackagingScriptPath)!;
            var nativeScriptPath = Path.Combine(packagingRoot, "Publish-Release.Native.ps1");
            File.WriteAllText(
                nativeScriptPath,
                BuildPartialPublishCompressArchiveFunction() + Environment.NewLine + File.ReadAllText(nativeScriptPath));

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

    private static string BuildPartialArchiveCommand(string requiredEntry)
        => """
            . $env:NATIVE_SCRIPT_PATH
            function Compress-Archive {
                param(
                    [string]$Path,
                    [string]$DestinationPath,
                    [switch]$Force
                )

                Add-Type -AssemblyName System.IO.Compression
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                if (Test-Path -LiteralPath $DestinationPath) {
                    Remove-Item -LiteralPath $DestinationPath -Force
                }

                $archive = [System.IO.Compression.ZipFile]::Open($DestinationPath, [System.IO.Compression.ZipArchiveMode]::Create)
                try {
                    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                        $archive,
                        (Join-Path $env:PACKAGE_DIR 'run.bat'),
                        'run.bat',
                        [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
                }
                finally {
                    $archive.Dispose()
                }
            }

            Invoke-ArchiveCreation `
                -PackageDirectory $env:PACKAGE_DIR `
                -ArchivePath $env:ARCHIVE_PATH `
                -RequiredRelativePaths @('
            """ + requiredEntry + """
            ')
            """;

    private static string BuildSlashEntryArchiveCommand()
        => """
            . $env:NATIVE_SCRIPT_PATH
            function Compress-Archive {
                param(
                    [string]$Path,
                    [string]$DestinationPath,
                    [switch]$Force
                )

                Add-Type -AssemblyName System.IO.Compression
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                if (Test-Path -LiteralPath $DestinationPath) {
                    Remove-Item -LiteralPath $DestinationPath -Force
                }

                $archive = [System.IO.Compression.ZipFile]::Open($DestinationPath, [System.IO.Compression.ZipArchiveMode]::Create)
                try {
                    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                        $archive,
                        (Join-Path $env:PACKAGE_DIR 'bin\install.ps1'),
                        'bin/install.ps1',
                        [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
                }
                finally {
                    $archive.Dispose()
                }
            }

            Invoke-ArchiveCreation `
                -PackageDirectory $env:PACKAGE_DIR `
                -ArchivePath $env:ARCHIVE_PATH `
                -RequiredRelativePaths @('bin\install.ps1')
            """;

    private static string BuildPartialPublishCompressArchiveFunction()
        => """
            function Compress-Archive {
                param(
                    [string]$Path,
                    [string]$DestinationPath,
                    [switch]$Force
                )

                Add-Type -AssemblyName System.IO.Compression
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                if (Test-Path -LiteralPath $DestinationPath) {
                    Remove-Item -LiteralPath $DestinationPath -Force
                }

                $packageDirectory = Join-Path `
                    ([System.IO.Path]::GetDirectoryName($DestinationPath)) `
                    ([System.IO.Path]::GetFileNameWithoutExtension($DestinationPath))
                $archive = [System.IO.Compression.ZipFile]::Open($DestinationPath, [System.IO.Compression.ZipArchiveMode]::Create)
                try {
                    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                        $archive,
                        (Join-Path $packageDirectory 'run.bat'),
                        'run.bat',
                        [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
                    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                        $archive,
                        (Join-Path $packageDirectory 'bin\install.ps1'),
                        'bin/install.ps1',
                        [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
                }
                finally {
                    $archive.Dispose()
                }
            }
            """;

    private static string[] ReadArchiveEntries(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        return archive.Entries.Select(static entry => entry.FullName).ToArray();
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
