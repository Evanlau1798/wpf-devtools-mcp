using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseAssetMetadataParserTests
{
    private const string AssetName = "release_0.1.0_win-x64.zip";
    private const string AssetSha =
        "2fee3fbb07b419e0c09df85ad3a32671103ebd7ea7b819a7a13ce6056529ca96";

    [Fact]
    public void InstallerReleaseParser_ShouldReadGitHubManifestReturnedAsTextAndBomChecksum()
    {
        var script = $$"""
            . {{Quote(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Release.ps1"))}}
            {{BuildParserAssertions("Get-ReleaseAssetRecordsFromManifestObject", "Get-ReleaseAssetRecordsFromChecksumContent")}}
            """;

        RunParserScript(script);
    }

    [Fact]
    public void StandaloneReleaseAssetParser_ShouldReadGitHubManifestReturnedAsTextAndBomChecksum()
    {
        var script = $$"""
            . {{Quote(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/online-installer.release-assets.ps1"))}}
            {{BuildParserAssertions("Get-TuiHelperReleaseAssetRecordsFromManifestObject", "Get-TuiHelperReleaseAssetRecordsFromChecksumContent")}}
            """;

        RunParserScript(script);
    }

    [Theory]
    [InlineData("scripts/installer/Installer.Release.ps1", "Get-ReleaseAssetRecordsFromGitHub")]
    [InlineData("scripts/installer/online-installer.release-assets.ps1", "Get-TuiHelperReleaseAssetRecordsFromGitHub")]
    public void GitHubChecksumLookup_ShouldReadPredictableReleaseAssetBeforeRestApi(
        string modulePath,
        string recordsFunction)
    {
        const string version = "1.0.0-beta.75";
        var manifestUri =
            $"https://github.com/Evanlau1798/wpf-devtools-mcp/releases/download/v{version}/release-assets.json";
        var script = $$"""
            . {{Quote(ReleaseScriptTestHarness.GetRepoFilePath(modulePath))}}
            $script:GitHubReleaseChecksumRecordCache = @{}
            $script:apiCalls = 0
            function Get-GitHubReleaseApiResponse {
                $script:apiCalls++
                throw 'API rate limit exceeded'
            }
            function Invoke-RestMethod {
                param([string]$Uri, [hashtable]$Headers, [int]$TimeoutSec)
                if ($Uri -ne '{{manifestUri}}') { throw "Unexpected URI: $Uri" }
                return '{"assets":[{"name":"{{AssetName}}","sha256":"{{AssetSha}}"}]}'
            }
            function Invoke-InstallerWebRequest { throw 'Checksum fallback should not run' }

            $records = @({{recordsFunction}} -ResolvedVersion '{{version}}')
            if ($records.Count -ne 1) { throw "records: $($records.Count)" }
            if ([string]$records[0].AssetName -ne '{{AssetName}}') { throw 'asset mismatch' }
            if ([string]$records[0].Sha256 -ne '{{AssetSha}}') { throw 'sha mismatch' }
            if ($script:apiCalls -ne 0) { throw "API calls: $script:apiCalls" }
            """;

        RunParserScript(script);
    }

    private static string BuildParserAssertions(string manifestFunction, string checksumFunction)
        => $$"""
            $sha = '{{AssetSha}}'
            $asset = '{{AssetName}}'
            $manifestJson = ([string][char]0xFEFF) + '{"assets":[{"name":"' + $asset + '","sha256":"' + $sha + '"}]}'
            $manifestRecords = @({{manifestFunction}} -ManifestObject $manifestJson)
            if ($manifestRecords.Count -ne 1) { throw "manifest records: $($manifestRecords.Count)" }
            if ([string]$manifestRecords[0].AssetName -ne $asset) { throw "manifest asset mismatch" }
            if ([string]$manifestRecords[0].Sha256 -ne $sha) { throw "manifest sha mismatch" }

            $mojibakeBom = -join ([char[]](0x00EF, 0x00BB, 0x00BF))
            $checksumRecords = @({{checksumFunction}} -Content ($mojibakeBom + $sha + '  ' + $asset))
            if ($checksumRecords.Count -ne 1) { throw "checksum records: $($checksumRecords.Count)" }
            if ([string]$checksumRecords[0].AssetName -ne $asset) { throw "checksum asset mismatch" }
            if ([string]$checksumRecords[0].Sha256 -ne $sha) { throw "checksum sha mismatch" }
            """;

    private static void RunParserScript(string script)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptPath = Path.Combine(tempRoot, "parser-test.ps1");
            File.WriteAllText(scriptPath, script);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(scriptPath, []);

            result.ExitCode.Should().Be(0, result.Stdout + Environment.NewLine + result.Stderr);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string Quote(string value)
        => "'" + value.Replace("'", "''") + "'";
}
