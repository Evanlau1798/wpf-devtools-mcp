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
}
