using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerOnlineRuntimeDecompositionTests
{
    [Fact]
    public void OnlineInstallerSources_ShouldUseBoundedIntegrityTrackedRuntimeFragments()
    {
        var entryPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
        var helperRoot = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
        var runtimeFiles = Directory.GetFiles(helperRoot, "OnlineInstaller.Runtime.*.ps1")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        File.ReadLines(entryPath).Count().Should().BeLessThanOrEqualTo(500);
        runtimeFiles.Should().NotBeEmpty();
        runtimeFiles.Should().OnlyContain(path => File.ReadLines(path).Count() <= 500);

        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(helperRoot, "installer-helpers.manifest.json")));
        var manifestFiles = manifest.RootElement.GetProperty("helperFiles")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("path").GetString())
            .ToHashSet(StringComparer.Ordinal);
        manifestFiles.Should().Contain(runtimeFiles.Select(Path.GetFileName));

        var entry = File.ReadAllText(entryPath);
        entry.Should().Contain("Import-OnlineInstallerRuntime");
        foreach (var runtimeFile in runtimeFiles)
        {
            entry.Should().Contain(Path.GetFileName(runtimeFile));
        }
    }

    [Fact]
    public void OnlineInstallerRuntime_ShouldIncludeTuiHelperDownloadBaseUriResolver()
    {
        var entryPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
        var helperRoot = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
        var runtime = string.Join(
            Environment.NewLine,
            Directory.GetFiles(helperRoot, "OnlineInstaller.Runtime.*.ps1")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

        runtime.Should().Contain(
            "function Resolve-TuiHelperDownloadBaseUri",
            "a fresh online-installer process must be able to bootstrap helpers for uninstall actions");

        var command = $$"""
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
    "-Action full-uninstall -Version latest -Architecture x64 -Client other",
    entryPath)}}
Resolve-TuiHelperDownloadBaseUri
""";
        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Trim().Should().Be(
            "https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/installer");
    }
}
