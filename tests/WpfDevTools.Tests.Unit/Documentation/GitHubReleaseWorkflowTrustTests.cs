using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class GitHubReleaseWorkflowTrustTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();

    [Fact]
    public void ReleaseValidation_ShouldInstallStagedArchivesThroughTrustedMetadata()
    {
        var lines = File.ReadAllLines(GetRepoFilePath(".github/workflows/release.yml"));

        foreach (var (jobName, architecture) in ReleaseValidationJobs)
        {
            var job = GetWorkflowJobBlock(lines, jobName);
            var step = GetNamedStepBlock(job, $"Install staged {architecture} package smoke test");
            var checksumBranchIndex = Array.FindIndex(step, line =>
                line.Contains("if ($env:RELEASE_TRUST_MODE -eq 'ReleaseChecksumOnly')", StringComparison.Ordinal));
            var onlineInstallerIndex = Array.FindIndex(step, line =>
                line.Contains(
                    "scripts/online-installer.ps1 -PackageArchivePath $packageArchive.FullName -TrustedReleaseMetadataDirectory $stagingRoot -InstallRoot './tmp-release-install-smoke'",
                    StringComparison.Ordinal));
            var signedBranchIndex = Array.FindIndex(step, line =>
                line.Trim().Equals("} else {", StringComparison.Ordinal));
            var packageLocalInstallerIndex = Array.FindIndex(step, line =>
                line.Contains("Join-Path $packageDir 'bin/install.ps1'", StringComparison.Ordinal));

            checksumBranchIndex.Should().BeGreaterThanOrEqualTo(0,
                $"the {architecture} release validation package smoke must branch on checksum-only trust mode");
            onlineInstallerIndex.Should().BeGreaterThan(checksumBranchIndex,
                $"the {architecture} checksum-only branch must use the staged archive sidecars as its trust root");
            signedBranchIndex.Should().BeGreaterThan(onlineInstallerIndex,
                $"the {architecture} signed branch should be separate from the checksum-only archive install path");
            packageLocalInstallerIndex.Should().BeGreaterThan(signedBranchIndex,
                $"the {architecture} package-local installer should only run when independent signer trust is available");
        }
    }

    [Fact]
    public void ReleaseValidation_ShouldUseProtocolSmokeForChecksumOnlyArchives()
    {
        var lines = File.ReadAllLines(GetRepoFilePath(".github/workflows/release.yml"));

        foreach (var (jobName, stepArchitecture, architecture) in ReleaseValidationRuntimeJobs)
        {
            var job = GetWorkflowJobBlock(lines, jobName);
            foreach (var (stepName, smokeInstallMode) in RuntimeSmokeSteps(stepArchitecture))
            {
                var step = GetNamedStepBlock(job, stepName);
                var checksumBranchIndex = Array.FindIndex(step, line =>
                    line.Contains("if ($env:RELEASE_TRUST_MODE -eq 'ReleaseChecksumOnly')", StringComparison.Ordinal));
                var protocolSmokeIndex = Array.FindIndex(step, line =>
                    line.Contains(
                        $"Test-PackagedServerRuntime.ps1 -ServerPath $serverPath -Architecture '{architecture}' -SmokeInstallMode '{smokeInstallMode}'",
                        StringComparison.Ordinal));
                var signedBranchIndex = Array.FindIndex(step, line =>
                    line.Trim().Equals("} else {", StringComparison.Ordinal));
                var liveSmokeIndex = Array.FindIndex(step, line =>
                    line.Contains("Invoke-PackagedRuntimeLiveSmoke.ps1", StringComparison.Ordinal));

                checksumBranchIndex.Should().BeGreaterThanOrEqualTo(0,
                    $"the {stepName} step must branch for unsigned checksum-only beta archives");
                protocolSmokeIndex.Should().BeGreaterThan(checksumBranchIndex,
                    $"the {stepName} checksum-only branch should validate protocol/tools without raw injection");
                signedBranchIndex.Should().BeGreaterThan(protocolSmokeIndex,
                    $"the {stepName} signed branch should remain the raw-injection gate");
                liveSmokeIndex.Should().BeGreaterThan(signedBranchIndex,
                    $"the {stepName} signed branch should keep target-aware live smoke coverage");
            }
        }
    }

    [Fact]
    public void ReleaseValidation_ShouldUseX86SetupDotnetActionWithArchitectureSupport()
    {
        var lines = File.ReadAllLines(GetRepoFilePath(".github/workflows/release.yml"));
        var job = GetWorkflowJobBlock(lines, "validate-x86-release-assets");
        var step = GetNamedStepBlock(job, "Setup .NET");

        step.Should().Contain(line => line.Contains("actions/setup-dotnet@c2fa09f4bde5ebb9d1777cf28262a3eb3db3ced7 # v5.2.0", StringComparison.Ordinal),
            "x86 validation needs a setup-dotnet version that accepts the architecture input");
        step.Should().Contain(line => line.Trim().Equals("architecture: x86", StringComparison.Ordinal),
            "x86 packaged executables need an x86 dotnet host on hosted runners");
        step.Should().NotContain(line => line.Contains("actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4", StringComparison.Ordinal),
            "setup-dotnet v4 ignores architecture: x86 and leaves the x86 package using the x64 hostfxr");
    }

    [Fact]
    public void ReleaseValidation_ShouldIsolateInstallerStateByInstallPath()
    {
        var lines = File.ReadAllLines(GetRepoFilePath(".github/workflows/release.yml"));

        foreach (var (_, stepArchitecture, _) in ReleaseValidationRuntimeJobs)
        {
            var job = GetWorkflowJobBlock(lines, $"validate-{stepArchitecture.ToLowerInvariant()}-release-assets");

            foreach (var stepName in PackageLocalInstallerStateSteps(stepArchitecture))
            {
                var step = GetNamedStepBlock(job, stepName);
                step.ContainInstallerState("install", stepName);
            }

            foreach (var stepName in OnlineInstallerStateSteps(stepArchitecture))
            {
                var step = GetNamedStepBlock(job, stepName);
                step.ContainInstallerState("bootstrap", stepName);
            }
        }
    }

    [Fact]
    public void ReleaseUpload_ShouldPassTrustModeToEvidenceWriter()
    {
        var lines = File.ReadAllLines(GetRepoFilePath(".github/workflows/release.yml"));
        var job = GetWorkflowJobBlock(lines, "upload-release-assets");
        var step = GetNamedStepBlock(job, "Write release evidence summary");

        step.Should().Contain(line =>
                line.Contains("RELEASE_TRUST_MODE: ${{ needs.publish-release-assets.outputs.release-trust-mode }}", StringComparison.Ordinal),
            "the evidence writer needs the same trust mode used to package and validate release assets");
        step.Should().Contain(line =>
                line.Contains("-ReleaseTrustMode $env:RELEASE_TRUST_MODE", StringComparison.Ordinal),
            "checksum-only prereleases should not be evaluated as signed raw-injection releases");
    }

    private static (string JobName, string Architecture)[] ReleaseValidationJobs
        =>
        [
            ("validate-x64-release-assets", "x64"),
            ("validate-x86-release-assets", "x86"),
            ("validate-arm64-release-assets", "ARM64"),
        ];

    private static (string JobName, string StepArchitecture, string Architecture)[] ReleaseValidationRuntimeJobs
        =>
        [
            ("validate-x64-release-assets", "x64", "x64"),
            ("validate-x86-release-assets", "x86", "x86"),
            ("validate-arm64-release-assets", "ARM64", "arm64"),
        ];

    private static (string StepName, string SmokeInstallMode)[] RuntimeSmokeSteps(string stepArchitecture)
    {
        var infix = string.Equals(stepArchitecture, "ARM64", StringComparison.Ordinal)
            ? "runtime smoke test"
            : "runtime live smoke test";

        return
        [
            ($"Start staged installed {stepArchitecture} {infix}", "package-local"),
            ($"Start staged online-installed {stepArchitecture} {infix}", "online-installer"),
        ];
    }

    private static string[] PackageLocalInstallerStateSteps(string stepArchitecture)
        =>
        [
            $"Install staged {stepArchitecture} package smoke test",
            $"Uninstall staged installed {stepArchitecture} package smoke test",
            $"Full uninstall staged installed {stepArchitecture} package residue test",
        ];

    private static string[] OnlineInstallerStateSteps(string stepArchitecture)
        =>
        [
            $"{stepArchitecture} online installer smoke test",
            $"Uninstall staged {stepArchitecture} online installer smoke test",
            $"Full uninstall staged {stepArchitecture} online installer residue test",
        ];

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(RepoRoot, relativePath));

    private static string[] GetNamedStepBlock(string[] lines, string stepName)
    {
        var stepHeader = $"      - name: {stepName}";
        var stepIndex = Array.FindIndex(lines, line => string.Equals(line, stepHeader, StringComparison.Ordinal));
        if (stepIndex < 0)
        {
            return [];
        }

        return lines
            .Skip(stepIndex)
            .TakeWhile((line, index) => index == 0 || !line.StartsWith("      - name:", StringComparison.Ordinal))
            .ToArray();
    }

    private static string[] GetWorkflowJobBlock(string[] lines, string jobName)
    {
        var jobHeader = $"  {jobName}:";
        var jobIndex = Array.FindIndex(lines, line => string.Equals(line, jobHeader, StringComparison.Ordinal));
        if (jobIndex < 0)
        {
            return [];
        }

        return lines
            .Skip(jobIndex)
            .TakeWhile((line, index) => index == 0 ||
                !Regex.IsMatch(line, @"^  [A-Za-z0-9_-]+:\s*$", RegexOptions.CultureInvariant))
            .ToArray();
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, ".git")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}

file static class ReleaseWorkflowTrustAssertionExtensions
{
    public static void ContainInstallerState(this IEnumerable<string> lines, string stateScope, string stepName)
    {
        lines.Should().Contain(line =>
                line.Trim().Equals(
                    $@"APPDATA: ${{{{ github.workspace }}}}\tmp-release-user-smoke\{stateScope}\AppData\Roaming",
                    StringComparison.Ordinal),
            $"{stepName} should use isolated {stateScope} installer state");
        lines.Should().Contain(line =>
                line.Trim().Equals(
                    $@"LOCALAPPDATA: ${{{{ github.workspace }}}}\tmp-release-user-smoke\{stateScope}\AppData\Local",
                    StringComparison.Ordinal),
            $"{stepName} should use isolated {stateScope} installer state");
    }
}
