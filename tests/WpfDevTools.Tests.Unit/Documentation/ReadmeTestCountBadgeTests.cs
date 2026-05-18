using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ReadmeTestCountBadgeTests
{
    [Fact]
    public void ReadmeTestBadge_ShouldStayCoarseLowerBound()
    {
        var readme = ReadRepoFile("README.md");

        readme.Should().Contain(
            "https://img.shields.io/badge/tests-3600%2B-brightgreen",
            "the public badge should stay a conservative lower-bound claim instead of mirroring exact discovery output");
    }

    [Fact]
    public void TestingGuides_ShouldDescribeDiscoveryDrivenCounts()
    {
        var english = ReadRepoFile("docfx/contributors/testing-and-tdd.md");
        var zhTw = ReadRepoFile("docfx/zh-tw/contributors/testing-and-tdd.md");

        english.Should().Contain("dotnet test --no-build --list-tests");
        english.Should().Contain("Do not update this page with exact test counts");
        english.Should().Contain("unit, release-unit, and integration suites");

        zhTw.Should().Contain("dotnet test --no-build --list-tests");
        zhTw.Should().Contain("不要在此頁寫入精確測試數量");
        zhTw.Should().Contain("unit、release-unit 與 integration suites");
    }

    [Fact]
    public void PublicReleaseChecklist_ShouldUseDiscoveryCommandInsteadOfExactSnapshot()
    {
        var checklist = ReadRepoFile("PUBLIC_RELEASE_READINESS_CHECKLIST.md");

        checklist.Should().Contain("tests-3600+");
        checklist.Should().Contain("dotnet test --no-build --list-tests");
    }

    [Theory]
    [MemberData(nameof(ExactCountDocumentationFiles))]
    public void PublicCountDocs_ShouldNotReintroduceExactTestCountSnapshots(string relativePath)
    {
        var content = ReadRepoFile(relativePath);

        foreach (var forbiddenToken in ExactCountSnapshotTokens())
        {
            content.Should().NotContain(forbiddenToken,
                $"{relativePath} should stay discovery-driven instead of restoring stale exact test-count snapshots");
        }
    }

    [Fact]
    public void TopLevelContributingDocs_ShouldNotClaimCurrentCoveragePercentages()
    {
        var content = ReadRepoFile("CONTRIBUTING.md");

        content.Should().NotMatchRegex(
            @"(?im)^\s*-\s*Current Status:\s*\d+(?:\.\d+)?%",
            "top-level contributor guidance should not publish stale current coverage percentages without a generated source");
    }

    public static IEnumerable<object[]> ExactCountDocumentationFiles()
    {
        yield return new object[] { "docfx/contributors/testing-and-tdd.md" };
        yield return new object[] { "docfx/zh-tw/contributors/testing-and-tdd.md" };
        yield return new object[] { "PUBLIC_RELEASE_READINESS_CHECKLIST.md" };
    }

    private static IEnumerable<string> ExactCountSnapshotTokens()
    {
        yield return "3301";
        yield return "2978";
        yield return "323";
        yield return "315";
        yield return "3616";
        yield return "3462";
        yield return "tests-3400+";
        yield return "currently discovered";
        yield return "discover 到 3301";
        yield return "302/302";
        yield return "311/311";
        yield return "15/15";
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
        return File.ReadAllText(path);
    }
}
