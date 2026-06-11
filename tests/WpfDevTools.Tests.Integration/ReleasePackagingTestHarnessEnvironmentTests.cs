using System.IO;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("PackagingIntegration")]
public sealed class ReleasePackagingTestHarnessEnvironmentTests
{
    private const string ExplicitSignerThumbprint = "EXPLICIT00000000000000000000000000000000";
    private const string ExplicitMsBuildPath = "explicit-msbuild.exe";

    private static readonly string[] ContaminatedVariables =
    [
        "WPFDEVTOOLS_INSTALLER_TEST_MODE",
        "WPFDEVTOOLS_TEST_SIGNATURE_STATUS",
        "WPFDEVTOOLS_SKIP_ELEVATION",
        "WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT",
        "WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"
    ];

    [Fact]
    public void RunPowerShellScript_ShouldScrubInheritedWpfDevToolsEnvironmentButKeepExplicitOverrides()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var scriptPath = Path.Combine(tempRoot, "echo-env.ps1");
            File.WriteAllText(scriptPath, EnvironmentProbeScript);

            WithContaminatedProcessEnvironment(() =>
            {
                var result = ReleasePackagingTestHarness.RunPowerShellScript(
                    scriptPath,
                    Array.Empty<string>(),
                    ExplicitOverrides,
                    timeout: TimeSpan.FromSeconds(5));

                result.ExitCode.Should().Be(0, result.Stderr);
                AssertScrubbedEnvironment(result.Stdout);
            });
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RunPowerShellCommand_ShouldScrubInheritedWpfDevToolsEnvironmentButKeepExplicitOverrides()
    {
        WithContaminatedProcessEnvironment(() =>
        {
            var result = ReleasePackagingTestHarness.RunPowerShellCommand(
                EnvironmentProbeCommand,
                ExplicitOverrides,
                timeout: TimeSpan.FromSeconds(5));

            result.ExitCode.Should().Be(0, result.Stderr);
            AssertScrubbedEnvironment(result.Stdout);
        });
    }

    private static IReadOnlyDictionary<string, string?> ExplicitOverrides => new Dictionary<string, string?>
    {
        ["WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT"] = ExplicitSignerThumbprint,
        ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = ExplicitMsBuildPath
    };

    private const string EnvironmentProbeScript =
        "[ordered]@{\n" +
        "  InstallerTestMode = $env:WPFDEVTOOLS_INSTALLER_TEST_MODE\n" +
        "  TrustLocalMetadata = $env:WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA\n" +
        "  SignatureStatus = $env:WPFDEVTOOLS_TEST_SIGNATURE_STATUS\n" +
        "  SkipElevation = $env:WPFDEVTOOLS_SKIP_ELEVATION\n" +
        "  SignerThumbprint = $env:WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT\n" +
        "  PublishMsBuildPath = $env:WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH\n" +
        "} | ConvertTo-Json -Compress\n";

    private const string EnvironmentProbeCommand =
        "[ordered]@{" +
        "InstallerTestMode=$env:WPFDEVTOOLS_INSTALLER_TEST_MODE;" +
        "TrustLocalMetadata=$env:WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA;" +
        "SignatureStatus=$env:WPFDEVTOOLS_TEST_SIGNATURE_STATUS;" +
        "SkipElevation=$env:WPFDEVTOOLS_SKIP_ELEVATION;" +
        "SignerThumbprint=$env:WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT;" +
        "PublishMsBuildPath=$env:WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH" +
        "} | ConvertTo-Json -Compress";

    private static void AssertScrubbedEnvironment(string stdout)
    {
        using var payload = JsonDocument.Parse(stdout);
        var root = payload.RootElement;

        root.GetProperty("InstallerTestMode").GetString().Should().Be("1");
        root.GetProperty("TrustLocalMetadata").GetString().Should().Be("1");
        root.GetProperty("SignatureStatus").GetString().Should().BeNullOrEmpty();
        root.GetProperty("SkipElevation").GetString().Should().BeNullOrEmpty();
        root.GetProperty("SignerThumbprint").GetString().Should().Be(ExplicitSignerThumbprint);
        root.GetProperty("PublishMsBuildPath").GetString().Should().Be(ExplicitMsBuildPath);
    }

    private static void WithContaminatedProcessEnvironment(Action action)
    {
        var previousValues = ContaminatedVariables.ToDictionary(
            static variableName => variableName,
            Environment.GetEnvironmentVariable,
            StringComparer.OrdinalIgnoreCase);
        try
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_INSTALLER_TEST_MODE", "0");
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_TEST_SIGNATURE_STATUS", "InheritedSignatureStatus");
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_SKIP_ELEVATION", "1");
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT", "INHERITED0000000000000000000000000000000");
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH", "inherited-msbuild.exe");
            action();
        }
        finally
        {
            foreach (var pair in previousValues)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}
