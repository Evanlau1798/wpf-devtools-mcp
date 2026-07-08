using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class ReleasePackagingContractTests
{
    [Fact]
    public void InstallBatchTemplate_ShouldBootstrapPackagedInstallerWithElevationAndExecutionPolicyBypass()
    {
        var batchTemplatePath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/run-template.bat");
        var content = File.ReadAllText(batchTemplatePath);

        File.Exists(batchTemplatePath).Should().BeTrue();
        content.Should().Contain("bin\\install.ps1");
        content.Should().Contain("powershell.exe");
        content.Should().Contain("-ExecutionPolicy Bypass");
        content.Should().Contain("RunAs");
    }

    [Fact]
    public void PublishReleaseScript_ShouldCopyCanonicalInstallerAndAvoidLegacyScriptChain()
    {
        var content = PublishReleaseScriptSource.ReadAll();

        content.Should().Contain("run-template.bat");
        content.Should().Contain("run.bat");
        content.Should().Contain("WpfDevTools.Inspector.Sdk.dll");
        content.Should().Contain("scripts\\online-installer.ps1");
        content.Should().Contain("scripts\\installer");
        content.Should().Contain("bin\\install.ps1");
        content.Should().Contain("Join-Path $binDir 'installer'");
        content.Should().NotContain("Setup-WpfDevTools.ps1");
        content.Should().NotContain("internal-install.ps1");
        content.Should().NotContain("Uninstall-WpfDevTools.ps1");
    }

    [Fact]
    public void PublishReleaseScript_ShouldUseVersionedReleaseArchiveNames()
    {
        var content = PublishReleaseScriptSource.ReadAll();

        content.Should().Contain("release_${version}_win-$architecture.zip");
        content.Should().NotContain("_dev_win-");
    }

    [Fact]
    public void PublishReleaseScript_ShouldFailClosedWhenExpectedReleaseTagDoesNotMatchProjectVersion()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var testRepo = CreateMinimalSkipBuildReleaseRepo(tempRoot, "x64");
            var fakeMsbuild = Path.Combine(tempRoot, "fake-msbuild.exe");
            File.WriteAllText(fakeMsbuild, "stub");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                testRepo.PackagingScriptPath,
                ["-Configuration", "Debug", "-Architectures", "x64", "-OutputRoot", testRepo.OutputRoot, "-SkipBuild", "-ExpectedReleaseTag", "v9.9.9"],
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeMsbuild
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("Expected release tag 'v9.9.9' does not match project version '1.2.3'",
                "release packaging should fail before publishing mismatched asset names into a different Git tag");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PublishReleaseScript_ShouldRetryArchiveCreationWhenTransientFileLocksOccur()
    {
        var content = PublishReleaseScriptSource.ReadAll();

        content.Should().Contain("Invoke-ArchiveCreation");
        content.Should().Contain("New-ReleaseArchive");
        content.Should().Contain("Move-Item");
        content.Should().Contain("Start-Sleep");
    }

    [Fact]
    public void PublishReleaseScript_WhenSkipBuildUsesFrameworkOnlyServerOutput_ShouldFailInsteadOfPackagingWrongArchitecture()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var repoRoot = Path.Combine(tempRoot, "repo");
            var packagingRoot = Path.Combine(repoRoot, "scripts", "tools", "packaging");
            var installerRoot = Path.Combine(repoRoot, "scripts", "installer");
            var serverProjectRoot = Path.Combine(repoRoot, "src", "WpfDevTools.Mcp.Server");
            var inspectorProjectRoot = Path.Combine(repoRoot, "src", "WpfDevTools.Inspector");
            var bootstrapperProjectRoot = Path.Combine(repoRoot, "src", "WpfDevTools.Bootstrapper");
            Directory.CreateDirectory(packagingRoot);
            Directory.CreateDirectory(installerRoot);
            Directory.CreateDirectory(serverProjectRoot);
            Directory.CreateDirectory(inspectorProjectRoot);
            Directory.CreateDirectory(bootstrapperProjectRoot);

            PublishReleaseScriptSource.CopyTo(packagingRoot);
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/run-template.bat"),
                Path.Combine(packagingRoot, "run-template.bat"),
                overwrite: true);
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                Path.Combine(repoRoot, "scripts", "online-installer.ps1"),
                overwrite: true);

            var manifestSource = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json");
            File.Copy(manifestSource, Path.Combine(installerRoot, "installer-helpers.manifest.json"), overwrite: true);
            using var helperManifest = JsonDocument.Parse(File.ReadAllText(manifestSource));
            foreach (var helperFile in helperManifest.RootElement.GetProperty("helperFiles")
                         .EnumerateArray()
                         .Select(static entry => entry.ValueKind == JsonValueKind.Object
                             ? entry.GetProperty("path").GetString()
                             : entry.GetString())
                         .Where(static entry => !string.IsNullOrWhiteSpace(entry))
                         .Cast<string>())
            {
                File.Copy(
                    ReleaseScriptTestHarness.GetRepoFilePath(Path.Combine("scripts", "installer", helperFile)),
                    Path.Combine(installerRoot, helperFile),
                    overwrite: true);
            }

            File.WriteAllText(
                Path.Combine(serverProjectRoot, "WpfDevTools.Mcp.Server.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Version>1.2.3</Version>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(inspectorProjectRoot, "WpfDevTools.Inspector.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(bootstrapperProjectRoot, "WpfDevTools.Bootstrapper.vcxproj"), "<Project />");

            var frameworkOutputDir = Path.Combine(serverProjectRoot, "bin", "Release", "net8.0");
            Directory.CreateDirectory(frameworkOutputDir);
            File.WriteAllText(Path.Combine(frameworkOutputDir, "WpfDevTools.Mcp.Server.exe"), "framework-apphost");

            var inspectorNet8Dir = Path.Combine(inspectorProjectRoot, "bin", "Release", "net8.0-windows");
            var inspectorNet48Dir = Path.Combine(inspectorProjectRoot, "bin", "Release", "net48");
            Directory.CreateDirectory(inspectorNet8Dir);
            Directory.CreateDirectory(inspectorNet48Dir);
            File.WriteAllText(Path.Combine(inspectorNet8Dir, "WpfDevTools.Inspector.dll"), "net8");
            File.WriteAllText(Path.Combine(inspectorNet48Dir, "WpfDevTools.Inspector.dll"), "net48");

            var bootstrapperOutputDir = Path.Combine(repoRoot, "artifacts", "bootstrapper", "Release", "Win32");
            Directory.CreateDirectory(bootstrapperOutputDir);
            File.WriteAllText(Path.Combine(bootstrapperOutputDir, "WpfDevTools.Bootstrapper.x86.dll"), "bootstrapper");

            var toolRoot = Path.Combine(tempRoot, "tools");
            Directory.CreateDirectory(toolRoot);
            var fakeDotnet = Path.Combine(toolRoot, "dotnet.cmd");
            File.WriteAllText(fakeDotnet, "@echo off\r\nexit /b 0\r\n");
            var fakeMsbuild = Path.Combine(toolRoot, "msbuild.cmd");
            File.WriteAllText(fakeMsbuild, "@echo off\r\nexit /b 0\r\n");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(packagingRoot, "Publish-Release.ps1"),
                ["-Configuration", "Release", "-Architectures", "x86", "-OutputRoot", Path.Combine(tempRoot, "release"), "-SkipBuild"],
                new Dictionary<string, string?>
                {
                    ["PATH"] = toolRoot + ";" + Environment.GetEnvironmentVariable("PATH"),
                    ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeMsbuild
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("win-x86");
            result.Stderr.Should().Contain("existing server output");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ReleaseScriptTestHarness_CreatePackageArchive_ShouldIncludeInspectorAndBootstrapperPayloads()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "arm64");
            var extractRoot = Path.Combine(tempRoot, "extract");
            System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, extractRoot);

            File.Exists(Path.Combine(extractRoot, "bin", "inspectors", "net8.0-windows", "WpfDevTools.Inspector.dll")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "inspectors", "net48", "WpfDevTools.Inspector.dll")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "WpfDevTools.Inspector.Sdk.dll")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "bootstrapper", "arm64", "WpfDevTools.Bootstrapper.arm64.dll")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PublishReleaseScript_ShouldPackageBuiltinComposerPacks()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var testRepo = CreateMinimalSkipBuildReleaseRepo(tempRoot, "x64");

            var toolRoot = Path.Combine(tempRoot, "tools");
            Directory.CreateDirectory(toolRoot);
            var fakeDotnet = Path.Combine(toolRoot, "dotnet.cmd");
            File.WriteAllText(fakeDotnet, "@echo off\r\nexit /b 0\r\n");
            var fakeMsbuild = Path.Combine(toolRoot, "msbuild.cmd");
            File.WriteAllText(fakeMsbuild, "@echo off\r\nexit /b 0\r\n");
            var nativeToolchain = CreateFakeNativeToolchain(tempRoot);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                testRepo.PackagingScriptPath,
                [
                    "-Configuration", "Release",
                    "-Architectures", "x64",
                    "-OutputRoot", testRepo.OutputRoot,
                    "-ReleaseTrustMode", "ReleaseChecksumOnly",
                    "-SkipBuild"
                ],
                new Dictionary<string, string?>
                {
                    ["PATH"] = toolRoot + ";" + Environment.GetEnvironmentVariable("PATH"),
                    ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeMsbuild,
                    ["VCToolsInstallDir"] = nativeToolchain.VcToolsRoot,
                    ["WindowsSDKDir"] = nativeToolchain.WindowsSdkRoot
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            var extractRoot = Path.Combine(tempRoot, "extract");
            System.IO.Compression.ZipFile.ExtractToDirectory(
                Path.Combine(testRepo.OutputRoot, "release_1.2.3_win-x64.zip"),
                extractRoot);

            File.Exists(Path.Combine(extractRoot, "packs", "builtin", "wpfui", "0.1.0", "pack.json")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "packs", "builtin", "wpfui", "0.1.0", "install.manifest.json")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "packs", "builtin", "wpfui", "0.1.0", "renderers", "xaml", "fluentWindow.xaml.sbn")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackageLocalInstaller_ShouldInstallServerExecutableFromBinDirectory()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var packageLocalScript = Path.Combine(packageDir, "bin", "install.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                packageLocalScript,
                overwrite: true);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                packageLocalScript,
                ["-InstallRoot", installRoot, "-Client", "other", "-NonInteractive", "-Force", "-OutputJson"],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["WPFDEVTOOLS_TEST_SIGNATURE_STATUS"] = "Valid"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.Exists(Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static (string VcToolsRoot, string WindowsSdkRoot) CreateFakeNativeToolchain(string tempRoot)
    {
        var vcToolsRoot = Path.Combine(tempRoot, "fake-vc");
        Directory.CreateDirectory(Path.Combine(vcToolsRoot, "include"));
        Directory.CreateDirectory(Path.Combine(vcToolsRoot, "lib", "x64"));
        var vcBinRoot = Path.Combine(vcToolsRoot, "bin", "HostX64", "x64");
        Directory.CreateDirectory(vcBinRoot);
        File.WriteAllText(Path.Combine(vcBinRoot, "cl.exe"), "stub");
        File.WriteAllText(Path.Combine(vcBinRoot, "link.exe"), "stub");

        var sdkRoot = Path.Combine(tempRoot, "fake-sdk");
        var sdkVersion = "10.0.0.0";
        foreach (var includeLeaf in new[] { "ucrt", "shared", "um" })
        {
            Directory.CreateDirectory(Path.Combine(sdkRoot, "Include", sdkVersion, includeLeaf));
        }

        foreach (var libraryLeaf in new[] { "ucrt", "um" })
        {
            Directory.CreateDirectory(Path.Combine(sdkRoot, "Lib", sdkVersion, libraryLeaf, "x64"));
        }

        var sdkBinRoot = Path.Combine(sdkRoot, "bin", sdkVersion, "x64");
        Directory.CreateDirectory(sdkBinRoot);
        File.WriteAllText(Path.Combine(sdkBinRoot, "rc.exe"), "stub");

        return (vcToolsRoot, sdkRoot);
    }

    [Fact]
    public void PublishReleaseScript_ShouldFailFastWhenArm64ToolchainIsUnavailable()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fakeVsRoot = Path.Combine(tempRoot, "vs", "MSBuild", "Current", "Bin");
            Directory.CreateDirectory(fakeVsRoot);
            var fakeMsbuildPath = Path.Combine(fakeVsRoot, "MSBuild.exe");
            File.WriteAllText(fakeMsbuildPath, "stub");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"),
                ["-Configuration", "Debug", "-Architectures", "arm64", "-SkipBuild", "-OutputRoot", Path.Combine(tempRoot, "release-output")],
                new Dictionary<string, string?> { ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeMsbuildPath, ["VCToolsInstallDir"] = "", ["INCLUDE"] = "", ["LIB"] = "" });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("Release architecture 'arm64'");
            result.Stderr.Should().Contain("Microsoft.VisualStudio.Component.VC.Tools.ARM64");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static (string RepoRoot, string PackagingScriptPath, string OutputRoot) CreateMinimalSkipBuildReleaseRepo(
        string tempRoot,
        string architecture)
    {
        var repoRoot = Path.Combine(tempRoot, "repo");
        var packagingRoot = Path.Combine(repoRoot, "scripts", "tools", "packaging");
        var installerRoot = Path.Combine(repoRoot, "scripts", "installer");
        var serverProjectRoot = Path.Combine(repoRoot, "src", "WpfDevTools.Mcp.Server");
        var inspectorProjectRoot = Path.Combine(repoRoot, "src", "WpfDevTools.Inspector");
        var inspectorSdkProjectRoot = Path.Combine(repoRoot, "src", "WpfDevTools.Inspector.Sdk");
        var bootstrapperProjectRoot = Path.Combine(repoRoot, "src", "WpfDevTools.Bootstrapper");
        Directory.CreateDirectory(packagingRoot);
        Directory.CreateDirectory(installerRoot);
        Directory.CreateDirectory(serverProjectRoot);
        Directory.CreateDirectory(inspectorProjectRoot);
        Directory.CreateDirectory(inspectorSdkProjectRoot);
        Directory.CreateDirectory(bootstrapperProjectRoot);
        CopyDirectory(
            ReleaseScriptTestHarness.GetRepoFilePath("packs/builtin"),
            Path.Combine(repoRoot, "packs", "builtin"));

        PublishReleaseScriptSource.CopyTo(packagingRoot);
        File.Copy(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/run-template.bat"),
            Path.Combine(packagingRoot, "run-template.bat"),
            overwrite: true);
        File.Copy(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseSidecars.ps1"),
            Path.Combine(packagingRoot, "Write-ReleaseSidecars.ps1"),
            overwrite: true);
        File.Copy(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseSbomDocuments.ps1"),
            Path.Combine(packagingRoot, "Write-ReleaseSbomDocuments.ps1"),
            overwrite: true);
        File.Copy(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
            Path.Combine(repoRoot, "scripts", "online-installer.ps1"),
            overwrite: true);

        var manifestSource = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json");
        File.Copy(manifestSource, Path.Combine(installerRoot, "installer-helpers.manifest.json"), overwrite: true);
        using var helperManifest = JsonDocument.Parse(File.ReadAllText(manifestSource));
        foreach (var helperFile in helperManifest.RootElement.GetProperty("helperFiles")
                     .EnumerateArray()
                     .Select(static entry => entry.ValueKind == JsonValueKind.Object
                         ? entry.GetProperty("path").GetString()
                         : entry.GetString())
                     .Where(static entry => !string.IsNullOrWhiteSpace(entry))
                     .Cast<string>())
        {
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath(Path.Combine("scripts", "installer", helperFile)),
                Path.Combine(installerRoot, helperFile),
                overwrite: true);
        }

        File.WriteAllText(
            Path.Combine(serverProjectRoot, "WpfDevTools.Mcp.Server.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Version>1.2.3</Version>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(inspectorProjectRoot, "WpfDevTools.Inspector.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(inspectorSdkProjectRoot, "WpfDevTools.Inspector.Sdk.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(bootstrapperProjectRoot, "WpfDevTools.Bootstrapper.vcxproj"), "<Project />");

        var runtimeId = architecture switch
        {
            "x64" => "win-x64",
            "x86" => "win-x86",
            "arm64" => "win-arm64",
            _ => throw new ArgumentOutOfRangeException(nameof(architecture))
        };

        var bootstrapperPlatform = architecture switch
        {
            "x64" => "x64",
            "x86" => "Win32",
            "arm64" => "ARM64",
            _ => throw new ArgumentOutOfRangeException(nameof(architecture))
        };

        var serverOutputDir = Path.Combine(serverProjectRoot, "bin", "Release", "net8.0", runtimeId);
        Directory.CreateDirectory(serverOutputDir);
        File.WriteAllText(Path.Combine(serverOutputDir, "WpfDevTools.Mcp.Server.exe"), "server");

        var inspectorNet8Dir = Path.Combine(inspectorProjectRoot, "bin", "Release", "net8.0-windows");
        var inspectorNet48Dir = Path.Combine(inspectorProjectRoot, "bin", "Release", "net48");
        Directory.CreateDirectory(inspectorNet8Dir);
        Directory.CreateDirectory(inspectorNet48Dir);
        File.WriteAllText(Path.Combine(inspectorNet8Dir, "WpfDevTools.Inspector.dll"), "net8");
        File.WriteAllText(Path.Combine(inspectorNet48Dir, "WpfDevTools.Inspector.dll"), "net48");
        var inspectorSdkDir = Path.Combine(inspectorSdkProjectRoot, "bin", "Release", "net8.0-windows");
        Directory.CreateDirectory(inspectorSdkDir);
        File.WriteAllText(Path.Combine(inspectorSdkDir, "WpfDevTools.Inspector.Sdk.dll"), "sdk");

        var bootstrapperOutputDir = Path.Combine(repoRoot, "artifacts", "bootstrapper", "Release", bootstrapperPlatform);
        Directory.CreateDirectory(bootstrapperOutputDir);
        File.WriteAllText(Path.Combine(bootstrapperOutputDir, $"WpfDevTools.Bootstrapper.{architecture}.dll"), "bootstrapper");

        return (
            repoRoot,
            Path.Combine(packagingRoot, "Publish-Release.ps1"),
            Path.Combine(tempRoot, "release-output"));
    }

}
