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
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Preflight-Release.ps1"),
                new[]
                {
                    "-VersionTag", "v1.2.3",
                    "-OutputRoot", outputRoot,
                    "-PlanOnly",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT"] = "ABCDEF1234567890ABCDEF1234567890ABCDEF12"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var document = JsonDocument.Parse(result.Stdout);
            var steps = document.RootElement.GetProperty("steps").EnumerateArray().Select(x => x.GetString()).ToArray();
            steps.Should().Contain(step => step!.Contains("dotnet build WpfDevTools.sln -c Release", StringComparison.Ordinal));
            steps.Should().Contain(step => step!.Contains("dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c Release --no-build", StringComparison.Ordinal));
            steps.Should().Contain(step => step!.Contains("Publish-Release.ps1", StringComparison.Ordinal));
            steps.Should().Contain(step => step!.Contains("-ExpectedReleaseTag v1.2.3", StringComparison.Ordinal));
            steps.Should().Contain(step => step!.Contains("Export-GitHubReleaseAssets.ps1", StringComparison.Ordinal));
            steps.Should().Contain(step => step!.Contains("-TrustedSignerThumbprint $env:WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT", StringComparison.Ordinal));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PreflightReleaseScript_WithVersionTagAndMissingSignerTrust_ShouldFailFast()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "preflight-output");
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Preflight-Release.ps1"),
                new[]
                {
                    "-VersionTag", "v1.2.3",
                    "-OutputRoot", outputRoot,
                    "-PlanOnly",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT"] = null
                });

            result.ExitCode.Should().NotBe(0, result.Stdout);
            result.Stderr.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
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
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Preflight-Release.ps1"),
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
    public void PreflightReleaseScript_SkipBuildAndTest_ShouldInvokePublishScriptWithStableArchitectures()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fakePublishScript = Path.Combine(tempRoot, "fake-publish.ps1");
            var fakeExportScript = Path.Combine(tempRoot, "fake-export.ps1");
            var publishLog = Path.Combine(tempRoot, "publish-architectures.txt");
            var exportLog = Path.Combine(tempRoot, "export-signer.txt");
            var outputRoot = Path.Combine(tempRoot, "preflight-output");

            var fakePublishContent = string.Join(Environment.NewLine,
            [
                "param(",
                "    [string]$Configuration,",
                "    [string[]]$Architectures,",
                "    [string]$ExpectedReleaseTag,",
                "    [string]$OutputRoot",
                ")",
                $"Set-Content -Path '{publishLog.Replace("'", "''")}' -Value (($Architectures -join ',') + '|' + $ExpectedReleaseTag) -Encoding UTF8",
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
                "    [string]$TrustedSignerThumbprint,",
                "    [switch]$OutputJson",
                ")",
                $"Set-Content -Path '{exportLog.Replace("'", "''")}' -Value $TrustedSignerThumbprint -Encoding UTF8",
                "New-Item -ItemType Directory -Force -Path (Join-Path $OutputRoot $Tag) | Out-Null",
                "Set-Content -Path (Join-Path (Join-Path $OutputRoot $Tag) 'release-assets.json') -Value '{}' -Encoding UTF8",
                "Set-Content -Path (Join-Path (Join-Path $OutputRoot $Tag) 'SHA256SUMS.txt') -Value 'ok' -Encoding UTF8",
                "Set-Content -Path (Join-Path (Join-Path $OutputRoot $Tag) 'upload-gh-release.ps1') -Value 'ok' -Encoding UTF8",
                "if ($OutputJson) { '{\"tag\":\"' + $Tag + '\"}' }"
            ]);
            File.WriteAllText(fakeExportScript, fakeExportContent);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Preflight-Release.ps1"),
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
                    ["WPFDEVTOOLS_PREFLIGHT_EXPORT_SCRIPT"] = fakeExportScript,
                    ["WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT"] = "ABCDEF1234567890ABCDEF1234567890ABCDEF12"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var document = JsonDocument.Parse(result.Stdout);
            var steps = document.RootElement.GetProperty("steps").EnumerateArray().Select(x => x.GetString()).ToArray();
            steps.Should().NotContain(step => step!.Contains("dotnet build", StringComparison.Ordinal));
            steps.Should().NotContain(step => step!.Contains("dotnet test", StringComparison.Ordinal));
            File.ReadAllText(publishLog).Trim().Should().Be("x64,x86|v1.2.3");
            File.ReadAllText(exportLog).Trim().Should().Be("ABCDEF1234567890ABCDEF1234567890ABCDEF12");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PreflightReleaseScript_PrereleasePlan_ShouldIncludeArm64PreviewArchitecture()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "preflight-output");
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Preflight-Release.ps1"),
                new[]
                {
                    "-VersionTag", "v1.2.3-beta.1",
                    "-OutputRoot", outputRoot,
                    "-PlanOnly",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT"] = "ABCDEF1234567890ABCDEF1234567890ABCDEF12"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var document = JsonDocument.Parse(result.Stdout);
            var steps = document.RootElement.GetProperty("steps").EnumerateArray().Select(x => x.GetString()).ToArray();
            steps.Should().Contain(step => step!.Contains("-Architectures x64,x86,arm64", StringComparison.Ordinal));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PreflightReleaseScript_WhenPublishScriptSetsNativeExitCode_ShouldFail()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fakePublishScript = Path.Combine(tempRoot, "fake-publish.ps1");
            var outputRoot = Path.Combine(tempRoot, "preflight-output");

            File.WriteAllText(
                fakePublishScript,
                string.Join(Environment.NewLine,
                [
                    "param(",
                    "    [string]$Configuration,",
                    "    [string[]]$Architectures,",
                    "    [string]$OutputRoot",
                    ")",
                    "cmd /c exit 17"
                ]));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Preflight-Release.ps1"),
                new[]
                {
                    "-OutputRoot", outputRoot,
                    "-SkipBuild",
                    "-SkipTest",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_PREFLIGHT_PUBLISH_SCRIPT"] = fakePublishScript
                });

            result.ExitCode.Should().NotBe(0, result.Stdout);
            result.Stderr.Should().Contain("17").And.Contain("fake-publish.ps1");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
