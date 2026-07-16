using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerPathNormalizationContractTests
{
    [Fact]
    public void InstallerPathNormalization_ShouldPreserveWindowsVolumeRoot()
    {
        var sharedPath = ReleaseScriptTestHarness
            .GetRepoFilePath("scripts/installer/Installer.Discovery.ps1")
            .Replace("'", "''");
        var standalonePath = ReleaseScriptTestHarness
            .GetRepoFilePath("scripts/installer/OnlineInstaller.Runtime.03.ps1")
            .Replace("'", "''");
        var command = string.Join(
            Environment.NewLine,
            [
                ". '" + sharedPath + "'",
                ". '" + standalonePath + "'",
                "$volumeRoot = [System.IO.Path]::GetPathRoot([System.IO.Path]::GetTempPath())",
                "$variants = @($volumeRoot, $volumeRoot.Replace('\\', '/'), ($volumeRoot + '\\\\'))",
                "[ordered]@{ volumeRoot=$volumeRoot; shared=@($variants | ForEach-Object { Normalize-InstallerPathCore -PathValue $_ }); standalone=@($variants | ForEach-Object { Normalize-StandaloneInstallerPath -PathValue $_ }) } | ConvertTo-Json -Compress"
            ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout);
        var expectedRoot = json.RootElement.GetProperty("volumeRoot").GetString();
        expectedRoot.Should().NotBeNullOrWhiteSpace();
        foreach (var propertyName in new[] { "shared", "standalone" })
        {
            json.RootElement.GetProperty(propertyName)
                .EnumerateArray()
                .Select(static value => value.GetString())
                .Should().AllBeEquivalentTo(expectedRoot,
                    "normalization must never collapse a drive root such as C:\\ to drive-relative C:");
        }
    }
}
