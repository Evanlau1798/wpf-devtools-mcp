using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SandboxCiNativeLinkContractTests
{
    [Fact]
    public void NativeFullManualLink_ShouldIncludeBootstrapperImportLibraries()
    {
        var repoRoot = FindRepoRoot();
        var project = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "WpfDevTools.Bootstrapper",
            "WpfDevTools.Bootstrapper.vcxproj"));
        var nativeScript = File.ReadAllText(Path.Combine(
            repoRoot,
            "scripts",
            "ci",
            "SandboxCi.Native.ps1"));

        project.Should().Contain("Crypt32.lib");
        nativeScript.Should().Contain("'Crypt32.lib'",
            "NativeFull manually links the bootstrapper DLL and must keep DPAPI import libraries aligned with the MSBuild project");
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
