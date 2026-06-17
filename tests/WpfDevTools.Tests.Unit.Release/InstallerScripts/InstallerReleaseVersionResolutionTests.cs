using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerReleaseVersionResolutionTests
{
    [Fact]
    public void ExplicitVPrefixedPrereleaseVersion_ShouldResolvePackageAssetWithoutLeadingV()
    {
        var requestedVersion = "v0.1.0-e2e.20260618011846";
        var expectedVersion = "0.1.0-e2e.20260618011846";
        var command = string.Join(" ; ",
        [
            OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                "-Action install -Version " + requestedVersion + " -Prerelease -Architecture x64 -Client other -NonInteractive"),
            "$resolved = Resolve-RequestedReleaseVersion -RequestedVersion '" + requestedVersion + "'",
            "$assetName = Get-ReleaseAssetName -ResolvedVersion $resolved -ResolvedArchitecture 'x64'",
            "$downloadUri = Get-ReleaseDownloadUri -ResolvedVersion $resolved -ResolvedArchitecture 'x64'",
            "[ordered]@{ ResolvedVersion=$resolved; AssetName=$assetName; DownloadUri=$downloadUri } | ConvertTo-Json -Compress"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("ResolvedVersion").GetString().Should().Be(expectedVersion);
        payload.RootElement.GetProperty("AssetName").GetString()
            .Should().Be("release_0.1.0-e2e.20260618011846_win-x64.zip");
        payload.RootElement.GetProperty("DownloadUri").GetString()
            .Should().Contain("/releases/download/v0.1.0-e2e.20260618011846/")
            .And.EndWith("/release_0.1.0-e2e.20260618011846_win-x64.zip");
    }
}
