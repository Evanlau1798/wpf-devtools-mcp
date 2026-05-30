using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class SecurityScanScriptTests
{
    [Fact]
    public void HostedSandboxSecurityScanEquivalence_ShouldInvokeGitHubSecurityScanGates()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var logPath = Path.Combine(tempRoot, "sandbox-security-gates.log");
            var command = string.Join(Environment.NewLine, [
                "$ErrorActionPreference = 'Stop'",
                ". " + QuotePowerShellString(ReleaseScriptTestHarness.GetRepoFilePath("scripts/ci/SandboxCi.Process.ps1")),
                ". " + QuotePowerShellString(ReleaseScriptTestHarness.GetRepoFilePath("scripts/ci/SandboxCi.Native.ps1")),
                ". " + QuotePowerShellString(ReleaseScriptTestHarness.GetRepoFilePath("scripts/ci/SandboxCi.Hosted.ps1")),
                "function Invoke-External { param([string]$Name, [string]$FilePath, [string[]]$Arguments) " +
                "Add-Content -LiteralPath " + QuotePowerShellString(logPath) +
                " -Value ($Name + '|' + $FilePath + '|' + ($Arguments -join ' ')) }",
                "function Resolve-MSBuildPath { return 'MSBUILD-STUB' }",
                "function Resolve-DotNetNativeHostDirectory { param([string]$RuntimeId) return 'NATIVE-STUB' }",
                "function Get-HostedNativeBuildProperties { param([string]$Platform, [string]$WindowsSdkDirectory, " +
                "[string]$WindowsSdkVersion, [string]$NativeHostDirectory) return @('/p:NetHostIncludeDir=' + " +
                "$NativeHostDirectory, '/p:NetHostLibDir=' + $NativeHostDirectory) }",
                "$env:WindowsSDKDir = " + QuotePowerShellString(Path.Combine(tempRoot, "winsdk")),
                "New-Item -ItemType Directory -Force -Path (Join-Path $env:WindowsSDKDir 'Include\\10.0.0.0') | Out-Null",
                "Invoke-HostedSecurityScanEquivalence -DotNetPath 'DOTNET-STUB'"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(20));

            result.ExitCode.Should().Be(0, result.Stdout + result.Stderr);
            var log = File.ReadAllText(logPath);
            log.Should().Contain("Run .NET analyzer gate|DOTNET-STUB|format WpfDevTools.sln analyzers --verify-no-changes --severity error --no-restore");
            log.Should().Contain("Run PowerShell ScriptAnalyzer|powershell.exe|-NoProfile -ExecutionPolicy Bypass -File scripts\\tools\\security\\Invoke-PowerShellScriptAnalyzerGate.ps1 -Path scripts -Severity Error");
            log.Should().Contain("Run repository secret pattern scan|powershell.exe|-NoProfile -ExecutionPolicy Bypass -File scripts\\tools\\security\\Invoke-RepositorySecretScan.ps1");
            log.Should().Contain("Run native bootstrapper security analysis|MSBUILD-STUB|src\\WpfDevTools.Bootstrapper\\WpfDevTools.Bootstrapper.vcxproj");
            log.Should().Contain("/p:PreferredToolArchitecture=x64");
            log.Should().Contain("/p:RunCodeAnalysis=true");
            log.Should().Contain("/p:TreatWarningsAsErrors=true");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RepositorySecretScan_ShouldFailOnSyntheticSecretFixture()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fixturePath = Path.Combine(tempRoot, "synthetic-secret.txt");
            File.WriteAllText(
                fixturePath,
                "Synthetic fixture only: sk-proj_" + new string('A', 40));

            var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath(
                "scripts/tools/security/Invoke-RepositorySecretScan.ps1");
            File.Exists(scriptPath).Should().BeTrue("the secret scanner should be reusable outside GitHub Actions YAML");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                ["-Path", fixturePath]);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("openai-api-key").And.Contain("synthetic-secret.txt");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RepositorySecretScan_ShouldReportAllSyntheticFindingsBeforeFailing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fixturePath = Path.Combine(tempRoot, "synthetic-secrets.txt");
            File.WriteAllText(
                fixturePath,
                "Synthetic fixture only: ghp_" + new string('A', 40) + Environment.NewLine +
                "Synthetic fixture only: sk-proj_" + new string('B', 40));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/security/Invoke-RepositorySecretScan.ps1"),
                ["-Path", fixturePath]);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("github-token");
            result.Stderr.Should().Contain("openai-api-key");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PowerShellScriptAnalyzerGate_ShouldFailOnSyntheticAnalyzerViolation()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fixturePath = Path.Combine(tempRoot, "InvokeExpressionFixture.ps1");
            File.WriteAllText(fixturePath, "Invoke-Expression $input");

            var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath(
                "scripts/tools/security/Invoke-PowerShellScriptAnalyzerGate.ps1");
            File.Exists(scriptPath).Should().BeTrue("the ScriptAnalyzer gate should be reusable outside GitHub Actions YAML");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                ["-Path", tempRoot, "-Severity", "Warning"]);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("PSAvoidUsingInvokeExpression");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PowerShellScriptAnalyzerGate_ShouldReportAllSyntheticViolationsBeforeFailing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fixturePath = Path.Combine(tempRoot, "AnalyzerFindingsFixture.ps1");
            File.WriteAllText(
                fixturePath,
                "Invoke-Expression $input" + Environment.NewLine +
                "Write-Host 'synthetic fixture'");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/security/Invoke-PowerShellScriptAnalyzerGate.ps1"),
                ["-Path", tempRoot, "-Severity", "Warning"]);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("PSAvoidUsingInvokeExpression");
            result.Stderr.Should().Contain("PSAvoidUsingWriteHost");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string QuotePowerShellString(string value)
        => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
