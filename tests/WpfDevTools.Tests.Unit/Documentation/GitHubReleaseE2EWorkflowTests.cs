using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class GitHubReleaseE2EWorkflowTests
{
    [Fact]
    public void ReleaseWorkflow_ShouldSkipFormalPublishingForE2EValidationTags()
    {
        var lines = File.ReadAllLines(GetRepoFilePath(".github/workflows/release.yml"));
        var content = string.Join(Environment.NewLine, lines);

        content.Should().Contain("is-e2e-validation-release:",
            "downstream jobs need a workflow output that distinguishes E2E validation pre-releases from signed public releases");
        content.Should().Contain("is-e2e-validation-release=$($isE2EValidationRelease.ToString().ToLowerInvariant())",
            "the release metadata step should classify v*-e2e.* tags before any signing secret is exposed");
        content.Should().Contain("-e2e\\.",
            "E2E validation tags use the vX.Y.Z-e2e.timestamp shape");

        GetNamedStepBlock(lines, "Materialize signing certificate")
            .Should()
            .Contain(line => line.Contains("steps.release-metadata.outputs.is-e2e-validation-release != 'true'", StringComparison.Ordinal),
                "E2E validation pre-releases are already uploaded as Debug/dev assets and must not require production signing secrets");

        foreach (var jobName in new[]
                 {
                     "validate-x64-release-assets",
                     "validate-x86-release-assets",
                     "validate-arm64-release-assets",
                     "upload-release-assets"
                 })
        {
            GetWorkflowJobBlock(lines, jobName)
                .Should()
                .Contain(line => line.Contains("needs.publish-release-assets.outputs.is-e2e-validation-release != 'true'", StringComparison.Ordinal),
                    $"{jobName} must not run when the publish job intentionally skips formal signed release publishing");
        }
    }

    private static IReadOnlyList<string> GetNamedStepBlock(string[] lines, string stepName)
    {
        var start = Array.FindIndex(lines, line =>
            string.Equals(line.Trim(), $"- name: {stepName}", StringComparison.Ordinal));
        start.Should().BeGreaterThanOrEqualTo(0, $"step '{stepName}' should exist");

        var end = Array.FindIndex(lines, start + 1, line =>
            line.StartsWith("      - name:", StringComparison.Ordinal));

        return lines[start..(end < 0 ? lines.Length : end)];
    }

    private static IReadOnlyList<string> GetWorkflowJobBlock(string[] lines, string jobName)
    {
        var start = Array.FindIndex(lines, line =>
            string.Equals(line, $"  {jobName}:", StringComparison.Ordinal));
        start.Should().BeGreaterThanOrEqualTo(0, $"job '{jobName}' should exist");

        var end = Array.FindIndex(lines, start + 1, line =>
            line.StartsWith("  ", StringComparison.Ordinal) &&
            !line.StartsWith("    ", StringComparison.Ordinal) &&
            line.TrimEnd().EndsWith(':'));

        return lines[start..(end < 0 ? lines.Length : end)];
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.Combine(ResolveRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

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
