using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleasePreflightScriptTests
{
    [Fact]
    public void PreflightReleaseScript_PlanOnly_ShouldDescribeBuildTestPackageAndExportSteps()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "preflight-output");
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Preflight-Release.ps1"),
                new[]
                {
                    "-VersionTag", "v1.2.3",
                    "-OutputRoot", outputRoot,
                    "-PlanOnly",
                    "-OutputJson"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var document = JsonDocument.Parse(result.Stdout);
            var steps = document.RootElement.GetProperty("steps").EnumerateArray().Select(x => x.GetString()).ToArray();
            steps.Should().Contain(step => step!.Contains("dotnet build WpfDevTools.sln -c Release", StringComparison.Ordinal));
            steps.Should().Contain(step => step!.Contains("dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c Release --no-build", StringComparison.Ordinal));
            steps.Should().Contain(step => step!.Contains("Publish-Release.ps1", StringComparison.Ordinal));
            steps.Should().Contain(step => step!.Contains("Export-GitHubReleaseAssets.ps1", StringComparison.Ordinal));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PreflightReleaseScript_WithoutVersionTag_ShouldSkipAssetExportStep()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "preflight-output");
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Preflight-Release.ps1"),
                new[]
                {
                    "-OutputRoot", outputRoot,
                    "-PlanOnly",
                    "-OutputJson"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var document = JsonDocument.Parse(result.Stdout);
            document.RootElement.GetProperty("versionTagProvided").GetBoolean().Should().BeFalse();
            var steps = document.RootElement.GetProperty("steps").EnumerateArray().Select(x => x.GetString()).ToArray();
            steps.Should().NotContain(step => step!.Contains("Export-GitHubReleaseAssets.ps1", StringComparison.Ordinal));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PreflightReleaseScript_SkipBuildAndTest_ShouldInvokePublishScriptWithAllArchitectures()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fakePublishScript = Path.Combine(tempRoot, "fake-publish.ps1");
            var fakeExportScript = Path.Combine(tempRoot, "fake-export.ps1");
            var publishLog = Path.Combine(tempRoot, "publish-architectures.txt");
            var outputRoot = Path.Combine(tempRoot, "preflight-output");

            var fakePublishContent = string.Join(Environment.NewLine,
            [
                "param(",
                "    [string]$Configuration,",
                "    [string[]]$Architectures,",
                "    [string]$OutputRoot",
                ")",
                $"Set-Content -Path '{publishLog.Replace("'", "''")}' -Value ($Architectures -join ',') -Encoding UTF8",
                "foreach ($architecture in $Architectures) {",
                "    Set-Content -Path (Join-Path $OutputRoot ('release_1.2.3_win-' + $architecture + '.zip')) -Value $architecture -Encoding UTF8",
                "}"
            ]);
            File.WriteAllText(fakePublishScript, fakePublishContent);

            var fakeExportContent = string.Join(Environment.NewLine,
            [
                "param(",
                "    [string]$InputRoot,",
                "    [string]$OutputRoot,",
                "    [string]$Tag,",
                "    [switch]$OutputJson",
                ")",
                "New-Item -ItemType Directory -Force -Path (Join-Path $OutputRoot $Tag) | Out-Null",
                "Set-Content -Path (Join-Path (Join-Path $OutputRoot $Tag) 'release-assets.json') -Value '{}' -Encoding UTF8",
                "Set-Content -Path (Join-Path (Join-Path $OutputRoot $Tag) 'SHA256SUMS.txt') -Value 'ok' -Encoding UTF8",
                "Set-Content -Path (Join-Path (Join-Path $OutputRoot $Tag) 'upload-gh-release.ps1') -Value 'ok' -Encoding UTF8",
                "if ($OutputJson) { '{\"tag\":\"' + $Tag + '\"}' }"
            ]);
            File.WriteAllText(fakeExportScript, fakeExportContent);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Preflight-Release.ps1"),
                new[]
                {
                    "-VersionTag", "v1.2.3",
                    "-OutputRoot", outputRoot,
                    "-SkipBuild",
                    "-SkipTest",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_PREFLIGHT_PUBLISH_SCRIPT"] = fakePublishScript,
                    ["WPFDEVTOOLS_PREFLIGHT_EXPORT_SCRIPT"] = fakeExportScript
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var document = JsonDocument.Parse(result.Stdout);
            var steps = document.RootElement.GetProperty("steps").EnumerateArray().Select(x => x.GetString()).ToArray();
            steps.Should().NotContain(step => step!.Contains("dotnet build", StringComparison.Ordinal));
            steps.Should().NotContain(step => step!.Contains("dotnet test", StringComparison.Ordinal));
            File.ReadAllText(publishLog).Trim().Should().Be("x64,x86,arm64");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
