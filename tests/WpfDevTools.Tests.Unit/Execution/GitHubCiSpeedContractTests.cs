using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

public sealed class GitHubCiSpeedContractTests
{
    [Fact]
    public void ReleaseUnitTests_ShouldRunOnlyOnceOnReleaseX64Matrix()
    {
        var ciLines = ReadRepoFile(".github/workflows/ci-cd.yml")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var releaseTestStep = GetNamedStepBlock(ciLines, "Run release unit tests");

        releaseTestStep.Should().Contain(
            "      if: matrix.configuration == 'Release' && matrix.platform == 'x64'",
            "release unit tests are configuration-sensitive release packaging contracts and should not be repeated in the Debug x64 lane");
        releaseTestStep.Should().NotContain(
            "      if: matrix.platform == 'x64'",
            "the old condition ran the same release/installer shard suite in both Debug x64 and Release x64 CI jobs");
    }

    private static string ReadRepoFile(string relativePath)
        => File.ReadAllText(WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath));

    private static string[] GetNamedStepBlock(string[] lines, string stepName)
    {
        var normalizedLines = lines.Select(line => line.TrimEnd('\r')).ToArray();
        var start = Array.FindIndex(normalizedLines, line => line == $"    - name: {stepName}");
        start.Should().BeGreaterThanOrEqualTo(0, $"workflow should define step {stepName}");

        var end = Array.FindIndex(normalizedLines, start + 1, line => line.StartsWith("    - name: ", StringComparison.Ordinal));
        return normalizedLines[start..(end < 0 ? normalizedLines.Length : end)];
    }
}
