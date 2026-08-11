using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("ProcessEnvironment")]
public sealed class ReleasePackagingProcessEnvironmentTests
{
    [Fact]
    public void ReleaseScriptHarness_ShouldScrubInheritedReleaseCertificateThumbprint()
    {
        var originalThumbprint = Environment.GetEnvironmentVariable("WPFDEVTOOLS_RELEASE_CERTIFICATE_THUMBPRINT");
        try
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_CERTIFICATE_THUMBPRINT", "INHERITED_THUMBPRINT");

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                "if ([string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_RELEASE_CERTIFICATE_THUMBPRINT)) { 'EMPTY' } else { $env:WPFDEVTOOLS_RELEASE_CERTIFICATE_THUMBPRINT }");

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be("EMPTY");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_CERTIFICATE_THUMBPRINT", originalThumbprint);
        }
    }

    [Fact]
    public void ReleaseScriptHarness_ShouldDefaultInstallerTestProcessesToNonElevated()
    {
        var originalAssumeElevated = Environment.GetEnvironmentVariable("WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED");
        try
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED", "1");

            var result = ReleaseScriptTestHarness.RunPowerShellCommand("$env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED");

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be("0");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED", originalAssumeElevated);
        }
    }
}
