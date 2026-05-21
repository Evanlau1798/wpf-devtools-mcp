using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class RunTemplatePowerShellOverrideTests
{
    [Fact]
    public void RunTemplate_ShouldRejectUnsafePowerShellOverrideShapesBeforeLaunchingOrElevating()
    {
        var batchTemplatePath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/run-template.bat");
        var content = File.ReadAllText(batchTemplatePath);

        content.Should().Contain("WPFDEVTOOLS_POWERSHELL_EXE cannot contain quote characters.");
        content.Should().Contain("WPFDEVTOOLS_POWERSHELL_EXE must point to a .exe host.");
        content.Should().Contain("WPFDEVTOOLS_POWERSHELL_EXE must be an absolute path.");
        content.Should().Contain("WPFDEVTOOLS_POWERSHELL_EXE must be a local drive path.");
        content.Should().Contain("WPFDEVTOOLS_POWERSHELL_EXE must point to powershell.exe or pwsh.exe.");
        content.Should().Contain("if not exist \"%WPFDEVTOOLS_POWERSHELL_EXE%\"");
        content.Should().Contain("Get-Item -LiteralPath $env:WPFDEVTOOLS_POWERSHELL_EXE");
        content.Should().Contain("ReparsePoint");

        var validationIndex = content.IndexOf("call :validate_powershell_override", StringComparison.Ordinal);
        var launchIndex = content.IndexOf(":launch_install", StringComparison.Ordinal);
        var elevationIndex = content.IndexOf(":elevate", StringComparison.Ordinal);

        validationIndex.Should().BeGreaterThanOrEqualTo(0);
        validationIndex.Should().BeLessThan(launchIndex);
        validationIndex.Should().BeLessThan(elevationIndex);
    }

    [Fact]
    public void RunTemplate_ShouldVerifyPowerShellOverrideSignerBeforeLaunchingOrElevating()
    {
        var batchTemplatePath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/run-template.bat");
        var content = File.ReadAllText(batchTemplatePath);

        content.Should().Contain("Get-AuthenticodeSignature");
        content.Should().Contain("WPFDEVTOOLS_POWERSHELL_EXE must be signed by Microsoft.");
        content.Should().Contain("O=Microsoft Corporation");

        var signatureIndex = content.IndexOf("call :validate_powershell_override", StringComparison.Ordinal);
        var launchIndex = content.IndexOf(":launch_install", StringComparison.Ordinal);
        var elevationIndex = content.IndexOf(":elevate", StringComparison.Ordinal);

        signatureIndex.Should().BeGreaterThanOrEqualTo(0);
        signatureIndex.Should().BeLessThan(launchIndex);
        signatureIndex.Should().BeLessThan(elevationIndex);
    }
}
