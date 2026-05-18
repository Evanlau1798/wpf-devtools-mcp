using FluentAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class ReleaseReadinessDocumentationTests
{
    [Fact]
    public void ReleasingGuide_ShouldSeparatePreflightValidationFromBuildReleasePackaging()
    {
        var content = File.ReadAllText(GetRepoFilePath("RELEASING.md"));

        content.Should().Contain("Preflight-Release.ps1 builds, tests, packages",
            "release validation must point at the preflight entrypoint that actually runs build and test steps");
        content.Should().Contain("build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1",
            "build-release.ps1 should be documented as the packaging wrapper it is today");
        GetBuildReleaseValidationClaims(content).Should().BeEmpty(
            "the build-release.ps1 section must not claim that the packaging wrapper runs release validation");
    }

    [Fact]
    public void BuildReleaseValidationGuard_ShouldRejectLfValidationClaimsInsideBuildReleaseSection()
    {
        var guide = string.Join("\n",
            "# Releasing",
            "",
            "Preflight-Release.ps1 builds, tests, packages, and optionally stages release sidecars.",
            "",
            "To generate release zip packages locally without running the preflight validation steps or uploading anything, use:",
            "",
            "```powershell",
            "powershell -ExecutionPolicy Bypass -File scripts/tools/build-release.ps1 -Configuration Release",
            "```",
            "",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            "1. Builds `WpfDevTools.sln` in `Release`",
            "2. Runs `dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj --no-build`",
            "3. Produces release packages");

        GetBuildReleaseValidationClaims(guide).Should().NotBeEmpty(
            "the guard must reject build/test validation claims inside the build-release.ps1 packaging-wrapper section even when the file uses LF line endings");
    }

    [Fact]
    public void BuildReleaseValidationGuard_ShouldPreserveDottedSolutionFilenames()
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            "1. Builds `WpfDevTools.sln` in `Release`.");

        GetBuildReleaseValidationClaims(guide).Should().NotBeEmpty(
            "sentence splitting must not break dotted filenames before the validation claim scanner runs");
    }

    [Fact]
    public void BuildReleaseValidationGuard_ShouldRejectPositiveClaimsAfterNoSpaceSentenceBoundary()
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            "1. Stops after package generation; it does not run the preflight validation.Runs the full test suite.");

        GetBuildReleaseValidationClaims(guide).Should().NotBeEmpty(
            "positive validation claims after a no-space sentence boundary must not be hidden by the preceding negated sentence");
    }

    [Fact]
    public void BuildReleaseValidationGuard_ShouldRejectLowercasePositiveClaimsAfterMalformedNoSpaceSentenceBoundary()
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            "1. Stops after package generation; it does not run the preflight validation.runs the full test suite.");

        GetBuildReleaseValidationClaims(guide).Should().NotBeEmpty(
            "malformed no-space lowercase sentence boundaries must not let a negated validation phrase hide a later positive claim");
    }

    [Fact]
    public void BuildReleaseValidationGuard_ShouldRejectTitleCasePositiveClaimsAfterMalformedNoSpaceSentenceBoundary()
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            "1. Stops after package generation; it does not run the preflight validation.Full Test Suite passes.");

        GetBuildReleaseValidationClaims(guide).Should().NotBeEmpty(
            "malformed no-space title-case sentence boundaries must not let a negated validation phrase hide a later positive claim");
    }

    [Fact]
    public void BuildReleaseValidationGuard_ShouldAllowNegatedDottedProjectPathClaims()
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            "1. Produces packages and does not run tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj --no-build.");

        GetBuildReleaseValidationClaims(guide).Should().BeEmpty(
            "negated precise test project paths should remain attached to the negation instead of being split into a false positive validation claim");
    }

    [Fact]
    public void BuildReleaseValidationGuard_ShouldRejectCommaSeparatedPositiveClaimsAfterNegation()
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            "1. Does not run signing, runs the full test suite.");

        GetBuildReleaseValidationClaims(guide).Should().NotBeEmpty(
            "a negated non-validation phrase before a comma must not hide a later positive validation claim");
    }

    [Fact]
    public void BuildReleaseValidationGuard_ShouldRejectCommaSeparatedPositiveClaimsWithSubjectAfterNegation()
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            "1. Does not run signing, it runs the full test suite.");

        GetBuildReleaseValidationClaims(guide).Should().NotBeEmpty(
            "a comma-separated positive validation claim with an explicit subject must not be hidden by a preceding negated phrase");
    }

    [Fact]
    public void BuildReleaseValidationGuard_ShouldAllowSubjectPrefixedActiveNoNegationClaims()
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            "1. Does not run signing, it runs no unit tests.");

        GetBuildReleaseValidationClaims(guide).Should().BeEmpty(
            "subject-prefixed active no-negation clauses should not become false positive validation claims after comma splitting");
    }

    [Fact]
    public void BuildReleaseValidationGuard_ShouldRejectPositiveClaimsAfterSubjectPrefixedActiveNoNegation()
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            "1. Does not run signing, it runs no unit tests before executing release validation.");

        GetBuildReleaseValidationClaims(guide).Should().NotBeEmpty(
            "subject-prefixed active no-negation must not hide later positive validation claims in the same clause");
    }

    [Theory]
    [InlineData("1. Produces packages and does not execute release validation.")]
    [InlineData("1. Produces packages and does not run release validation.")]
    [InlineData("1. Produces packages and does not perform release validation.")]
    [InlineData("1. Produces packages and does not run the unit tests.")]
    [InlineData("1. Produces packages and does not run `dotnet test`.")]
    public void BuildReleaseValidationGuard_ShouldAllowNegatedValidationAndTestCommandPhrases(string negatedClaim)
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            negatedClaim);

        GetBuildReleaseValidationClaims(guide).Should().BeEmpty(
            "valid negated validation and test-command phrases should not become false positive build-release claims");
    }

    [Theory]
    [InlineData("1. Does not run signing, dotnet test is not run.")]
    [InlineData("1. Does not run signing, unit tests are not run.")]
    public void BuildReleaseValidationGuard_ShouldAllowPassiveNegatedValidationClaimsAfterComma(string negatedClaim)
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            negatedClaim);

        GetBuildReleaseValidationClaims(guide).Should().BeEmpty(
            "passive negated validation clauses after comma splitting should not become false positive build-release claims");
    }

    [Theory]
    [InlineData("1. build-release.ps1 does not only package; it builds WpfDevTools.sln and runs tests.")]
    [InlineData("1. Produces packages, runs the full test suite, and executes release validation.")]
    [InlineData("1. build-release.ps1 builds, tests, and packages the release.")]
    [InlineData("1. Stops after package generation; it does not run the preflight build/test validation, but it runs the full test suite.")]
    [InlineData("1. Stops after package generation; it does not run the preflight build/test validation but it runs the full test suite.")]
    [InlineData("1. Stops after package generation; it does not run the preflight build/test validation and runs the full test suite.")]
    [InlineData("1. Stops after package generation; it does not run the preflight build/test validation then runs the full test suite.")]
    [InlineData("1. Stops after package generation; it does not run the preflight build/test validation while running the full test suite.")]
    public void BuildReleaseValidationGuard_ShouldRejectRewordedPositiveValidationClaims(string staleClaim)
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            staleClaim);

        GetBuildReleaseValidationClaims(guide).Should().NotBeEmpty(
            "reworded positive build/test validation claims should not be hidden by negation words or narrow keyword matching");
    }

    [Fact]
    public void BuildReleaseValidationGuard_ShouldStopAtEndOfWhatThisDoesList()
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            "1. Produces release packages",
            "2. Stops after package generation; it does not run the preflight build/test validation",
            "3. Does not upload anything",
            "",
            "After package generation, Preflight-Release.ps1 can run dotnet test and release validation.");

        GetBuildReleaseValidationClaims(guide).Should().BeEmpty(
            "the build-release wrapper guard should inspect only the What this does list and not drift into later Preflight guidance");
    }

    [Fact]
    public void BuildReleaseValidationGuard_ShouldRejectValidationClaimsOnOrderedListContinuationLines()
    {
        var guide = string.Join("\n",
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1. What this does:",
            "",
            "1. Produces release packages",
            "   and runs the full test suite.",
            "",
            "After package generation, Preflight-Release.ps1 can run dotnet test and release validation.");

        GetBuildReleaseValidationClaims(guide).Should().NotBeEmpty(
            "wrapped Markdown ordered-list lines still belong to the build-release wrapper list and should be scanned");
    }

    private static IReadOnlyList<string> GetBuildReleaseValidationClaims(string releaseGuide)
    {
        var section = GetBuildReleaseSection(releaseGuide);
        if (string.IsNullOrWhiteSpace(section))
        {
            return [];
        }

        return section
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .SelectMany(GetBuildReleaseValidationClauses)
            .Select(RemoveNegatedBuildReleaseValidationPhrases)
            .Where(IsBuildReleaseValidationClaim)
            .ToList();
    }

    private static string GetBuildReleaseSection(string releaseGuide)
    {
        var normalized = releaseGuide.Replace("\r\n", "\n").Replace('\r', '\n');
        const string sectionStartMarker =
            "build-release.ps1 delegates directly to scripts/tools/packaging/Publish-Release.ps1";
        var sectionStart = normalized.IndexOf(sectionStartMarker, StringComparison.Ordinal);
        if (sectionStart < 0)
        {
            return string.Empty;
        }

        var listStart = normalized.IndexOf("\n1.", sectionStart, StringComparison.Ordinal);
        if (listStart < 0)
        {
            return normalized[sectionStart..];
        }

        var lines = normalized[listStart..]
            .Split('\n')
            .TakeWhile(IsOrderedListBlankOrContinuationLine)
            .ToArray();

        return string.Join("\n", lines);
    }

    private static IEnumerable<string> GetBuildReleaseValidationClauses(string line)
    {
        return Regex.Split(
            line,
            @";|,(?=\s*(?:(?:it|this|build-release\.ps1|the\s+wrapper)\s+)?(?:runs?|builds?|executes?|release|preflight|unit\s+tests?|integration\s+tests?|full\s+test\s+suite|dotnet)\b)|\.(?=\s|$|(?-i:Builds?\b|Runs?\b|Executes?\b|Release\b|Preflight\b|Unit\s+tests?\b|Integration\s+tests?\b|Full\s+test\s+suite\b|Dotnet\b))|\b(?:but|however|yet|although|though|and|then|while)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(clause => clause.Trim())
            .Where(clause => clause.Length > 0);
    }

    private static string RemoveNegatedBuildReleaseValidationPhrases(string clause)
    {
        var scrubbed = clause;
        scrubbed = Regex.Replace(scrubbed,
            @"\b(?:does\s+not|do\s+not|will\s+not)\s+(?:run|execute|perform|validate)\s+(?:the\s+)?(?:preflight|release|build/test)?\s*validation(?:\s+steps?)?\b",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        scrubbed = Regex.Replace(scrubbed,
            @"\b(?:does\s+not|do\s+not|will\s+not)\s+(?:run|execute)\s+(?:the\s+)?(?:`?dotnet\s+test`?|[^\s,;]+\.csproj|unit\s+tests?|integration\s+tests?|tests?|full\s+test\s+suite)(?:\s+--no-build)?\b",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        scrubbed = Regex.Replace(scrubbed,
            @"\b(?:does\s+not|do\s+not|will\s+not)\s+(?:build|test|validate)\s+(?:`?WpfDevTools\.sln`?|unit\s+tests?|integration\s+tests?|tests?|full\s+test\s+suite|release\s+validation|preflight\s+validation)\b",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        scrubbed = Regex.Replace(scrubbed,
            @"\bwithout\s+(?:running|executing|performing|building|testing|validating)\s+(?:`?WpfDevTools\.sln`?|dotnet\s+test|[^\s,;]+\.csproj|unit\s+tests?|integration\s+tests?|tests?|full\s+test\s+suite|release\s+validation|preflight\s+validation)\b",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        scrubbed = Regex.Replace(scrubbed,
            @"\b(?:it|this|build-release\.ps1|the\s+wrapper)\s+(?:runs?|executes?)\s+no\s+(?:unit\s+tests?|integration\s+tests?|tests?|full\s+test\s+suite|release\s+validation|preflight\s+validation)\b",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        scrubbed = Regex.Replace(scrubbed,
            @"\b(?:`?dotnet\s+test`?|unit\s+tests?|integration\s+tests?|tests?|full\s+test\s+suite|release\s+validation|preflight\s+validation)\s+(?:is|are)\s+not\s+(?:run|executed|performed|validated)\b",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return scrubbed;
    }

    private static bool IsBuildReleaseValidationClaim(string line)
    {
        return Regex.IsMatch(line,
            @"\bBuilds?\s+`?WpfDevTools\.sln`?|\bdotnet\s+build\b|\bdotnet\s+test\b|--no-build|\bunit\s+tests?\b|\bintegration\s+tests?\b|\bruns?\s+tests?\b|\bfull\s+test\s+suite\b|\bbuilds?\s*,\s*tests?\b|\bexecutes?\s+release\s+validation\b|\brelease\s+validation\b|\bpreflight\s+validation\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsOrderedListBlankOrContinuationLine(string line)
    {
        return string.IsNullOrWhiteSpace(line)
            || Regex.IsMatch(line, @"^\d+\.\s+", RegexOptions.CultureInvariant)
            || Regex.IsMatch(line, @"^\s{2,}\S", RegexOptions.CultureInvariant);
    }
}
