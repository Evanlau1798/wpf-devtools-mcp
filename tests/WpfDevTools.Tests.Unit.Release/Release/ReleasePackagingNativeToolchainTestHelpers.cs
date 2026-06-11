namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class ReleasePackagingContractTests
{
    private sealed record FakeNativeToolchain(
        string MSBuildPath,
        string WindowsSdkDirectory,
        string VCToolsDirectory);

    private static FakeNativeToolchain CreateFakeNativeToolchain(string tempRoot, string architecture)
    {
        var targetArchitecture = architecture switch
        {
            "x64" => "x64",
            "x86" => "x86",
            _ => throw new ArgumentOutOfRangeException(nameof(architecture))
        };

        var visualStudioRoot = Path.Combine(tempRoot, "fake-vs");
        var msbuildDirectory = Path.Combine(visualStudioRoot, "MSBuild", "Current", "Bin");
        var vcToolsDirectory = Path.Combine(visualStudioRoot, "VC", "Tools", "MSVC", "14.44.35207");
        var vcCompilerDirectory = Path.Combine(vcToolsDirectory, "bin", "HostX64", targetArchitecture);
        Directory.CreateDirectory(msbuildDirectory);
        Directory.CreateDirectory(Path.Combine(vcToolsDirectory, "include"));
        Directory.CreateDirectory(Path.Combine(vcToolsDirectory, "lib", targetArchitecture));
        Directory.CreateDirectory(vcCompilerDirectory);
        File.WriteAllText(Path.Combine(vcCompilerDirectory, "cl.exe"), string.Empty);
        File.WriteAllText(Path.Combine(vcCompilerDirectory, "link.exe"), string.Empty);

        var msbuildPath = Path.Combine(msbuildDirectory, "MSBuild.cmd");
        File.WriteAllText(msbuildPath, "@echo off\r\nexit /b 0\r\n");

        var sdkVersion = "10.0.26100.0";
        var windowsSdkDirectory = Path.Combine(tempRoot, "Windows Kits", "10");
        foreach (var includeName in new[] { "ucrt", "shared", "um", "winrt", "cppwinrt" })
        {
            Directory.CreateDirectory(Path.Combine(windowsSdkDirectory, "Include", sdkVersion, includeName));
        }

        Directory.CreateDirectory(Path.Combine(windowsSdkDirectory, "Lib", sdkVersion, "ucrt", targetArchitecture));
        Directory.CreateDirectory(Path.Combine(windowsSdkDirectory, "Lib", sdkVersion, "um", targetArchitecture));
        var sdkHostToolDirectory = Path.Combine(windowsSdkDirectory, "bin", sdkVersion, "x64");
        Directory.CreateDirectory(sdkHostToolDirectory);
        Directory.CreateDirectory(Path.Combine(windowsSdkDirectory, "bin", sdkVersion, targetArchitecture));
        File.WriteAllText(Path.Combine(sdkHostToolDirectory, "rc.exe"), string.Empty);

        return new FakeNativeToolchain(msbuildPath, windowsSdkDirectory, vcToolsDirectory);
    }
}
