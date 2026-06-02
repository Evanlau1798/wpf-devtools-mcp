using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release.Release;

public sealed class PublishReleaseNativeBootstrapperContractTests
{
    [Fact]
    public void PublishReleaseScript_ShouldDisableNativeBootstrapperIncrementalLinking()
    {
        var script = PublishReleaseScriptSource.ReadAll();

        script.Should().Contain("/p:LinkIncremental=false",
            "CI and Windows Sandbox release packaging should not use Debug incremental native linking");
    }

    [Fact]
    public void PublishReleaseScript_ShouldPassNativeToolchainEnvironmentToBootstrapperMsBuild()
    {
        var script = PublishReleaseScriptSource.ReadAll();

        script.Should().Contain("ConvertTo-MSBuildPropertyValue");
        script.Should().Contain("/p:WindowsSDKDir=$windowsSdkDirectory");
        script.Should().Contain("/p:WindowsTargetPlatformVersion=$windowsSdkVersion");
        script.Should().Contain("/p:IncludePath=$includePath");
        script.Should().Contain("/p:LibraryPath=$libraryPath");
        script.Should().Contain("/p:ExecutablePath=$executablePath");
    }

    [Fact]
    public void ResolveWindowsSdkVersion_ShouldIgnoreWdfIncludeDirectory()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var sdkDirectory = Path.Combine(tempRoot, "Windows Kits", "10");
            Directory.CreateDirectory(Path.Combine(sdkDirectory, "Include", "10.0.22621.0"));
            Directory.CreateDirectory(Path.Combine(sdkDirectory, "Include", "10.0.26100.0"));
            Directory.CreateDirectory(Path.Combine(sdkDirectory, "Include", "wdf"));
            var functionOnlyScript = CreateFunctionOnlyPublishReleaseScript(tempRoot);
            var command = $$"""
            . '{{EscapePowerShellPath(functionOnlyScript)}}'
            $actual = Resolve-WindowsSdkVersion -WindowsSdkDirectory '{{EscapePowerShellPath(sdkDirectory)}}'
            if ($actual -ne '10.0.26100.0') {
                throw "Expected numeric SDK version 10.0.26100.0 but resolved '$actual'."
            }
            """;

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr + result.Stdout);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PublishReleaseScript_ShouldPassNativeToolchainPropertiesForEveryBootstrapperBuild()
    {
        var script = PublishReleaseScriptSource.ReadAll();

        script.Should().NotContain("if ($bootstrapperPlatform -in @('x64', 'Win32'))",
            "ARM64 packaging also needs the resolved target-specific SDK include, library, and executable paths");
        script.Should().Contain("/p:LibraryPath=$libraryPath");
        script.Should().Contain("/p:ExecutablePath=$executablePath");
    }

    [Fact]
    public void GetNativeBootstrapperBuildProperties_ShouldResolveHostedWin32SdkAndVCToolchainPaths()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var sdkDirectory = CreateFakeWindowsSdk(tempRoot, "10.0.26100.0", "x86");
            var msbuildPath = CreateFakeVisualStudioToolchain(tempRoot, "14.44.35207", "x86");
            var functionOnlyScript = CreateFunctionOnlyPublishReleaseScript(tempRoot);
            var command = $$"""
            {{ResetNativeToolchainEnvironmentCommand()}}
            . '{{EscapePowerShellPath(functionOnlyScript)}}'
            $actual = Get-NativeBootstrapperBuildProperties -BootstrapperPlatform 'Win32' -ResolvedMsBuildPath '{{EscapePowerShellPath(msbuildPath)}}' -WindowsSdkDirectory '{{EscapePowerShellPath(sdkDirectory)}}' -WindowsSdkVersion '10.0.26100.0'
            if ($actual.IncludePath -notlike '*Windows Kits\10\Include\10.0.26100.0\um*') {
                throw "Expected Win32 IncludePath to contain the Windows SDK um include directory but was '$($actual.IncludePath)'."
            }
            if ($actual.LibraryPath -notlike '*Windows Kits\10\Lib\10.0.26100.0\um\x86*') {
                throw "Expected Win32 LibraryPath to contain the Windows SDK um x86 library directory but was '$($actual.LibraryPath)'."
            }
            if ($actual.ExecutablePath -notlike '*VC\Tools\MSVC\14.44.35207\bin\HostX64\x86*') {
                throw "Expected Win32 ExecutablePath to contain the x86 VC compiler directory but was '$($actual.ExecutablePath)'."
            }
            if ($actual.ExecutablePath -notlike '*Windows Kits\10\bin\10.0.26100.0\x64*') {
                throw "Expected Win32 ExecutablePath to contain the x64 Windows SDK tool directory for rc.exe but was '$($actual.ExecutablePath)'."
            }
            """;

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr + result.Stdout);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GetNativeBootstrapperBuildProperties_Win32_ShouldNotInheritX64LibraryPath()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var sdkDirectory = CreateFakeWindowsSdk(tempRoot, "10.0.26100.0", "x86");
            var msbuildPath = CreateFakeVisualStudioToolchain(tempRoot, "14.44.35207", "x86");
            var inheritedX64LibraryPath = Path.Combine(tempRoot, "VS", "VC", "Tools", "MSVC", "14.44.35207", "lib", "x64");
            Directory.CreateDirectory(inheritedX64LibraryPath);
            var functionOnlyScript = CreateFunctionOnlyPublishReleaseScript(tempRoot);
            var command = $$"""
            {{ResetNativeToolchainEnvironmentCommand()}}
            $env:LIB = '{{EscapePowerShellPath(inheritedX64LibraryPath)}}'
            . '{{EscapePowerShellPath(functionOnlyScript)}}'
            $actual = Get-NativeBootstrapperBuildProperties -BootstrapperPlatform 'Win32' -ResolvedMsBuildPath '{{EscapePowerShellPath(msbuildPath)}}' -WindowsSdkDirectory '{{EscapePowerShellPath(sdkDirectory)}}' -WindowsSdkVersion '10.0.26100.0'
            if ($actual.LibraryPath -like '*\lib\x64*') {
                throw "Expected Win32 LibraryPath not to inherit x64 LIB entries but was '$($actual.LibraryPath)'."
            }
            if ($actual.LibraryPath -notlike '*\lib\x86*') {
                throw "Expected Win32 LibraryPath to retain x86 library entries but was '$($actual.LibraryPath)'."
            }
            """;

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr + result.Stdout);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GetNativeBootstrapperBuildProperties_ShouldResolveVisualStudioRootFromAmd64MSBuildPath()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var sdkDirectory = CreateFakeWindowsSdk(tempRoot, "10.0.26100.0", "x64");
            var msbuildPath = CreateFakeVisualStudioToolchain(tempRoot, "14.44.35207", "x64", useAmd64MsBuildPath: true);
            var functionOnlyScript = CreateFunctionOnlyPublishReleaseScript(tempRoot);
            var command = $$"""
            {{ResetNativeToolchainEnvironmentCommand()}}
            . '{{EscapePowerShellPath(functionOnlyScript)}}'
            $actual = Get-NativeBootstrapperBuildProperties -BootstrapperPlatform 'x64' -ResolvedMsBuildPath '{{EscapePowerShellPath(msbuildPath)}}' -WindowsSdkDirectory '{{EscapePowerShellPath(sdkDirectory)}}' -WindowsSdkVersion '10.0.26100.0'
            if ($actual.ExecutablePath -notlike '*VC\Tools\MSVC\14.44.35207\bin\HostX64\x64*') {
                throw "Expected x64 ExecutablePath to contain the VC compiler directory but was '$($actual.ExecutablePath)'."
            }
            """;

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr + result.Stdout);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GetNativeBootstrapperBuildProperties_ShouldFailBeforeMSBuildWhenCompilerToolIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var sdkDirectory = CreateFakeWindowsSdk(tempRoot, "10.0.26100.0", "x86");
            var msbuildPath = CreateFakeVisualStudioToolchain(
                tempRoot,
                "14.44.35207",
                "x86",
                createCompiler: false);
            var functionOnlyScript = CreateFunctionOnlyPublishReleaseScript(tempRoot);
            var command = $$"""
            {{ResetNativeToolchainEnvironmentCommand()}}
            . '{{EscapePowerShellPath(functionOnlyScript)}}'
            Get-NativeBootstrapperBuildProperties -BootstrapperPlatform 'Win32' -ResolvedMsBuildPath '{{EscapePowerShellPath(msbuildPath)}}' -WindowsSdkDirectory '{{EscapePowerShellPath(sdkDirectory)}}' -WindowsSdkVersion '10.0.26100.0'
            """;

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("cl.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GetNativeBootstrapperBuildProperties_ShouldFailBeforeMSBuildWhenLinkerToolIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var sdkDirectory = CreateFakeWindowsSdk(tempRoot, "10.0.26100.0", "x64");
            var msbuildPath = CreateFakeVisualStudioToolchain(
                tempRoot,
                "14.44.35207",
                "x64",
                createLinker: false);
            var functionOnlyScript = CreateFunctionOnlyPublishReleaseScript(tempRoot);
            var command = $$"""
            {{ResetNativeToolchainEnvironmentCommand()}}
            . '{{EscapePowerShellPath(functionOnlyScript)}}'
            Get-NativeBootstrapperBuildProperties -BootstrapperPlatform 'x64' -ResolvedMsBuildPath '{{EscapePowerShellPath(msbuildPath)}}' -WindowsSdkDirectory '{{EscapePowerShellPath(sdkDirectory)}}' -WindowsSdkVersion '10.0.26100.0'
            """;

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("link.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GetNativeBootstrapperBuildProperties_ShouldFailBeforeMSBuildWhenArm64LinkerToolIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var sdkDirectory = CreateFakeWindowsSdk(tempRoot, "10.0.26100.0", "arm64");
            var msbuildPath = CreateFakeVisualStudioToolchain(
                tempRoot,
                "14.44.35207",
                "arm64",
                createLinker: false);
            var functionOnlyScript = CreateFunctionOnlyPublishReleaseScript(tempRoot);
            var command = $$"""
            {{ResetNativeToolchainEnvironmentCommand()}}
            . '{{EscapePowerShellPath(functionOnlyScript)}}'
            Get-NativeBootstrapperBuildProperties -BootstrapperPlatform 'ARM64' -ResolvedMsBuildPath '{{EscapePowerShellPath(msbuildPath)}}' -WindowsSdkDirectory '{{EscapePowerShellPath(sdkDirectory)}}' -WindowsSdkVersion '10.0.26100.0'
            """;

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("link.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GetNativeBootstrapperBuildProperties_ShouldFailBeforeMSBuildWhenArm64ResourceCompilerToolIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var sdkDirectory = CreateFakeWindowsSdk(
                tempRoot,
                "10.0.26100.0",
                "arm64",
                createResourceCompiler: false);
            var msbuildPath = CreateFakeVisualStudioToolchain(tempRoot, "14.44.35207", "arm64");
            var functionOnlyScript = CreateFunctionOnlyPublishReleaseScript(tempRoot);
            var command = $$"""
            {{ResetNativeToolchainEnvironmentCommand()}}
            . '{{EscapePowerShellPath(functionOnlyScript)}}'
            Get-NativeBootstrapperBuildProperties -BootstrapperPlatform 'ARM64' -ResolvedMsBuildPath '{{EscapePowerShellPath(msbuildPath)}}' -WindowsSdkDirectory '{{EscapePowerShellPath(sdkDirectory)}}' -WindowsSdkVersion '10.0.26100.0'
            """;

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("rc.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GetNativeBootstrapperBuildProperties_ShouldFailBeforeMSBuildWhenArm64SdkLibraryIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var sdkDirectory = CreateFakeWindowsSdk(
                tempRoot,
                "10.0.26100.0",
                "arm64",
                createUmLibrary: false);
            var msbuildPath = CreateFakeVisualStudioToolchain(tempRoot, "14.44.35207", "arm64");
            var functionOnlyScript = CreateFunctionOnlyPublishReleaseScript(tempRoot);
            var command = $$"""
            {{ResetNativeToolchainEnvironmentCommand()}}
            . '{{EscapePowerShellPath(functionOnlyScript)}}'
            Get-NativeBootstrapperBuildProperties -BootstrapperPlatform 'ARM64' -ResolvedMsBuildPath '{{EscapePowerShellPath(msbuildPath)}}' -WindowsSdkDirectory '{{EscapePowerShellPath(sdkDirectory)}}' -WindowsSdkVersion '10.0.26100.0'
            """;

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("Windows SDK UM library path");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string CreateFunctionOnlyPublishReleaseScript(string tempRoot)
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1");
        var functionLines = File.ReadLines(scriptPath)
            .TakeWhile(line => !line.StartsWith("$repoRoot =", StringComparison.Ordinal))
            .ToArray();
        var functionOnlyScript = Path.Combine(tempRoot, "Publish-Release.Functions.ps1");
        File.WriteAllLines(functionOnlyScript, functionLines);
        PublishReleaseScriptSource.CopyTo(tempRoot);
        return functionOnlyScript;
    }

    private static string CreateFakeVisualStudioToolchain(
        string tempRoot,
        string toolsVersion,
        string architecture,
        bool useAmd64MsBuildPath = false,
        bool createCompiler = true,
        bool createLinker = true)
    {
        var visualStudioRoot = Path.Combine(tempRoot, "VS");
        var msbuildDirectory = Path.Combine(visualStudioRoot, "MSBuild", "Current", "Bin");
        if (useAmd64MsBuildPath)
        {
            msbuildDirectory = Path.Combine(msbuildDirectory, "amd64");
        }

        var toolsDirectory = Path.Combine(visualStudioRoot, "VC", "Tools", "MSVC", toolsVersion);
        Directory.CreateDirectory(msbuildDirectory);
        Directory.CreateDirectory(Path.Combine(toolsDirectory, "include"));
        Directory.CreateDirectory(Path.Combine(toolsDirectory, "lib", architecture));
        var compilerDirectory = Path.Combine(toolsDirectory, "bin", "HostX64", architecture);
        Directory.CreateDirectory(compilerDirectory);
        if (createCompiler)
        {
            File.WriteAllText(Path.Combine(compilerDirectory, "cl.exe"), string.Empty);
        }

        if (createLinker)
        {
            File.WriteAllText(Path.Combine(compilerDirectory, "link.exe"), string.Empty);
        }

        return Path.Combine(msbuildDirectory, "MSBuild.exe");
    }

    private static string CreateFakeWindowsSdk(
        string tempRoot,
        string sdkVersion,
        string architecture,
        bool createResourceCompiler = true,
        bool createUmLibrary = true)
    {
        var sdkDirectory = Path.Combine(tempRoot, "Windows Kits", "10");
        foreach (var includeName in new[] { "ucrt", "shared", "um", "winrt", "cppwinrt" })
        {
            Directory.CreateDirectory(Path.Combine(sdkDirectory, "Include", sdkVersion, includeName));
        }

        Directory.CreateDirectory(Path.Combine(sdkDirectory, "Lib", sdkVersion, "ucrt", architecture));
        if (createUmLibrary)
        {
            Directory.CreateDirectory(Path.Combine(sdkDirectory, "Lib", sdkVersion, "um", architecture));
        }

        var sdkHostToolsDirectory = Path.Combine(sdkDirectory, "bin", sdkVersion, "x64");
        Directory.CreateDirectory(sdkHostToolsDirectory);
        Directory.CreateDirectory(Path.Combine(sdkDirectory, "bin", sdkVersion, architecture));
        if (createResourceCompiler)
        {
            File.WriteAllText(Path.Combine(sdkHostToolsDirectory, "rc.exe"), string.Empty);
        }

        return sdkDirectory;
    }

    private static string EscapePowerShellPath(string path)
        => path.Replace("'", "''", StringComparison.Ordinal);

    private static string ResetNativeToolchainEnvironmentCommand()
        => """
        $env:VCToolsInstallDir = ''
        $env:INCLUDE = ''
        $env:LIB = ''
        $env:PATH = ''
        """;
}
