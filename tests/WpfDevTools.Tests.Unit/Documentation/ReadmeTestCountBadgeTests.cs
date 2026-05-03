using System.Text.RegularExpressions;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ReadmeTestCountBadgeTests
{
    [Fact]
    public void ReadmeTestBadge_ShouldTrackDocumentedCombinedTestBaseline()
    {
        var readme = ReadRepoFile("README.md");
        var testingGuide = ReadRepoFile("docfx/contributors/testing-and-tdd.md");

        var documentedTotal = ExtractEnglishCombinedTotal(testingGuide);
        var expectedBadgeFloor = documentedTotal / 100 * 100;

        readme.Should().Contain(
            $"https://img.shields.io/badge/tests-{expectedBadgeFloor}%2B-brightgreen",
            "the README badge should be a rounded lower bound of the documented combined test baseline");
    }

    [Fact]
    public void LocalizedTestingGuides_ShouldAgreeOnTestBaselineCounts()
    {
        var english = ReadRepoFile("docfx/contributors/testing-and-tdd.md");
        var zhTw = ReadRepoFile("docfx/zh-tw/contributors/testing-and-tdd.md");

        ExtractEnglishCounts(english).Should().BeEquivalentTo(ExtractTraditionalChineseCounts(zhTw));
    }

    private static (int Unit, int Integration, int Total) ExtractEnglishCounts(string content) =>
        (
            ExtractFirstInt(content, @"Unit tests:\s+(\d+) total"),
            ExtractFirstInt(content, @"Integration tests:\s+(\d+) total"),
            ExtractEnglishCombinedTotal(content)
        );

    private static (int Unit, int Integration, int Total) ExtractTraditionalChineseCounts(string content) =>
        (
            ExtractFirstInt(content, @"Unit tests：最近一次完整 unit-suite run 總數\s+(\d+)"),
            ExtractFirstInt(content, @"Integration tests：最近一次完整 integration-suite baseline 總數\s+(\d+)"),
            ExtractFirstInt(content, @"合計基準：(\d+)")
        );

    private static int ExtractEnglishCombinedTotal(string content) =>
        ExtractFirstInt(content, @"Verified combined total:\s+(\d+) tests");

    private static int ExtractFirstInt(string content, string pattern)
    {
        var match = Regex.Match(content, pattern, RegexOptions.CultureInvariant);
        match.Success.Should().BeTrue($"documentation should contain a count matching `{pattern}`");
        return int.Parse(match.Groups[1].Value);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
        return File.ReadAllText(path);
    }
}
