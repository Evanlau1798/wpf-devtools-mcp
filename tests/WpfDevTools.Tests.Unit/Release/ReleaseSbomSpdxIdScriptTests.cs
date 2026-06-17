using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseSbomSpdxIdScriptTests
{
    private static readonly string HelperPath = ReleaseScriptTestHarness.GetRepoFilePath(
        "scripts/tools/packaging/Write-ReleaseSbomDocuments.ps1");

    [Fact]
    public void ConvertToSpdxIdSuffix_ShouldReturnNonEmptyUniqueValuesForCollisions()
    {
        var result = RunHelperScript("""
            @('!!!', '---', 'a/b', 'a?b') |
                ForEach-Object { ConvertTo-SpdxIdSuffix -Value $_ } |
                ConvertTo-Json -Compress
            """);

        result.ExitCode.Should().Be(0, result.Output);
        var suffixes = JsonSerializer.Deserialize<string[]>(result.Output.Trim())!;

        suffixes.Should().OnlyContain(suffix => !string.IsNullOrWhiteSpace(suffix));
        suffixes.Should().OnlyContain(suffix => suffix.All(IsSpdxIdChar));
        suffixes.Distinct(StringComparer.Ordinal).Should().HaveCount(suffixes.Length);
    }

    [Fact]
    public void NewReleaseSbom_ShouldEmitUniqueNonEmptyPackageSpdxIds()
    {
        var result = RunHelperScript("""
            $assets = @(
                [pscustomobject]@{ name = '!!!'; sha256 = 'a' * 64 },
                [pscustomobject]@{ name = '---'; sha256 = 'b' * 64 },
                [pscustomobject]@{ name = 'a/b'; sha256 = 'c' * 64 },
                [pscustomobject]@{ name = 'a?b'; sha256 = 'd' * 64 }
            )
            $sbom = New-ReleaseSbom -ReleaseTag 'v-test' -ReleaseAssets $assets
            @($sbom.packages | ForEach-Object { [string]$_.SPDXID }) | ConvertTo-Json -Compress
            """);

        result.ExitCode.Should().Be(0, result.Output);
        var ids = JsonSerializer.Deserialize<string[]>(result.Output.Trim())!;

        ids.Should().OnlyContain(id => id.StartsWith("SPDXRef-Package-", StringComparison.Ordinal));
        ids.Should().OnlyContain(id => id.Length > "SPDXRef-Package-".Length);
        ids.Distinct(StringComparer.Ordinal).Should().HaveCount(ids.Length);
    }

    private static bool IsSpdxIdChar(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '.' or '-';

    private static ScriptRunResult RunHelperScript(string body)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptPath = Path.Combine(tempRoot, "sbom-spdx-probe.ps1");
            File.WriteAllText(scriptPath, CreateProbeScript(body));
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                [],
                timeout: TimeSpan.FromSeconds(30));

            return new ScriptRunResult(result.ExitCode, result.Stdout + result.Stderr);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string CreateProbeScript(string body) =>
        $$"""
        $ErrorActionPreference = 'Stop'
        . {{QuotePowerShellString(HelperPath)}}

        {{body}}
        """;

    private static string QuotePowerShellString(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private sealed record ScriptRunResult(int ExitCode, string Output);
}
