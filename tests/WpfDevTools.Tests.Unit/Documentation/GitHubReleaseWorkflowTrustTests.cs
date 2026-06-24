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

    private static (string JobName, string Architecture)[] ReleaseValidationJobs
        =>
        [
            ("validate-x64-release-assets", "x64"),
            ("validate-x86-release-assets", "x86"),
            ("validate-arm64-release-assets", "ARM64"),
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
