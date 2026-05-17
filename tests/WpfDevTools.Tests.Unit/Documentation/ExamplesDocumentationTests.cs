using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ExamplesDocumentationTests
{
    private const int MaxDocumentLineCount = 500;

    [Fact]
    public void Examples_ShouldUseValidCaptureStateSnapshotArguments()
    {
        var content = ReadAllExamplesMarkdown();

        var startIndex = content.IndexOf("\"name\": \"capture_state_snapshot\"", StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, "the examples should include a capture_state_snapshot example");

        var exampleWindow = content.Substring(startIndex, Math.Min(220, content.Length - startIndex));
        exampleWindow.Should().Contain("\"includeFocus\": true",
            "capture_state_snapshot requires at least one explicit snapshot scope argument in the public example");
    }

    [Fact]
    public void Examples_ShouldUseSupportedGetProcessesFilters()
    {
        var content = ReadAllExamplesMarkdown();

        content.Should().NotContain("\"windowFilter\": \"MyApp\"",
            "windowFilter only accepts visible/all/foreground and should not be documented as a free-text name filter");
        content.Should().Contain("\"nameFilter\": \"MyApp\"",
            "examples should demonstrate process name narrowing with nameFilter instead of an invalid windowFilter value");
    }

    [Fact]
    public void Examples_ShouldContainOnlyCurrentSceneFirstEdition()
    {
        var content = ReadAllExamplesMarkdown();

        Regex.Matches(content, "^# WPF DevTools MCP Server - Usage Examples\r?$", RegexOptions.Multiline)
            .Should().HaveCount(1, "EXAMPLES.md should not append a stale second edition below the current scene-first guidance");
        content.Should().NotContain("// 1. List running WPF processes",
            "examples should not regress to a list-first workflow as the default entry path");
        content.Should().NotContain("// 1. Get the Visual Tree to find elements with DataContext",
            "examples should keep scene-first guidance ahead of tree-heavy workflows");
        content.Should().NotContain("`TIMEOUT` ->",
            "examples should not keep stale uppercase legacy error-code snippets from an appended older edition");
    }

    [Fact]
    public void ExamplesMarkdown_ShouldStayUnderRepositoryLineLimit()
    {
        foreach (var relativePath in GetExampleMarkdownRelativePaths())
        {
            var lineCount = File.ReadLines(GetRepoFilePath(relativePath)).Count();

            lineCount.Should().BeLessThanOrEqualTo(MaxDocumentLineCount,
                $"{relativePath} should stay under the repository's single-file line limit");
        }
    }

    [Fact]
    public void ExamplesMarkdown_ShouldExposeSplitFilesFromRootEntryPoint()
    {
        var rootContent = File.ReadAllText(GetRepoFilePath("EXAMPLES.md"));
        var linkedExamplePaths = Regex.Matches(rootContent, @"\[[^\]]+\]\((?<href>examples/[^)#?]+\.md)(?:#[^)]+)?\)")
            .Select(match => match.Groups["href"].Value.Replace('/', Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        linkedExamplePaths.Should().NotBeEmpty("root EXAMPLES.md should remain an entry point to the split examples");
        foreach (var relativePath in linkedExamplePaths)
        {
            File.Exists(GetRepoFilePath(relativePath)).Should().BeTrue($"{relativePath} should be a valid example link");
        }
    }

    [Theory]
    [InlineData("examples/state-and-interaction.md", "\"name\": \"click_element\"", "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS", null)]
    [InlineData("examples/state-and-interaction.md", "\"name\": \"element_screenshot\"", "WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS", null)]
    [InlineData("examples/state-and-interaction.md", "\"name\": \"set_dp_value\"", "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS", null)]
    [InlineData("examples/state-and-interaction.md", "\"name\": \"restore_state_snapshot\"", "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS", null)]
    [InlineData("examples/scene-inspection.md", "\"name\": \"get_viewmodel\"", "WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION", null)]
    [InlineData("examples/scene-inspection.md", "\"name\": \"get_commands\"", "WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION", null)]
    [InlineData("examples/scene-inspection.md", "\"name\": \"execute_command\"", "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS", "WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION")]
    [InlineData("docfx/guides/ai-agent-guide.md", "`click_element`", "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS", null)]
    [InlineData("docfx/guides/ai-agent-guide.md", "`element_screenshot`", "WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS", null)]
    [InlineData("docfx/guides/ai-agent-guide.md", "`get_viewmodel`", "WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION", null)]
    [InlineData("docfx/guides/ai-agent-guide.md", "`execute_command`", "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS", "WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION")]
    [InlineData("docfx/zh-tw/guides/ai-agent-guide.md", "`click_element`", "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS", null)]
    [InlineData("docfx/zh-tw/guides/ai-agent-guide.md", "`element_screenshot`", "WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS", null)]
    [InlineData("docfx/zh-tw/guides/ai-agent-guide.md", "`get_viewmodel`", "WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION", null)]
    [InlineData("docfx/zh-tw/guides/ai-agent-guide.md", "`execute_command`", "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS", "WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION")]
    public void HighRiskToolExamples_ShouldDocumentLocalPolicyGatesNearUsage(
        string relativePath,
        string marker,
        string expectedGate,
        string? secondExpectedGate)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));
        var markerIndex = content.IndexOf(marker, StringComparison.Ordinal);

        markerIndex.Should().BeGreaterThanOrEqualTo(0, $"{relativePath} should contain {marker}");
        var contextStart = Math.Max(0, markerIndex - 2_500);
        var context = content.Substring(contextStart, markerIndex - contextStart);

        context.Should().Contain("WPFDEVTOOLS_MCP_ALLOWED_TARGETS",
            $"{relativePath} should keep the connect allowlist prerequisite close to {marker}");
        context.Should().Contain(expectedGate,
            $"{relativePath} should keep the local policy gate for {marker} close to the example that uses it");
        if (!string.IsNullOrWhiteSpace(secondExpectedGate))
        {
            context.Should().Contain(secondExpectedGate,
                $"{relativePath} should keep every local policy gate for {marker} close to the example that uses it");
        }
    }

    [Theory]
    [InlineData("examples/state-and-interaction.md", "\"name\": \"set_dp_value\"", "`set_dp_value`")]
    [InlineData("examples/state-and-interaction.md", "\"name\": \"restore_state_snapshot\"", "`restore_state_snapshot`")]
    [InlineData("examples/state-and-interaction.md", "\"name\": \"click_element\"", "`click_element`")]
    [InlineData("examples/state-and-interaction.md", "\"name\": \"element_screenshot\"", "`element_screenshot`")]
    [InlineData("examples/scene-inspection.md", "\"name\": \"get_viewmodel\"", "`get_viewmodel`")]
    [InlineData("examples/scene-inspection.md", "\"name\": \"get_commands\"", "`get_commands`")]
    [InlineData("examples/scene-inspection.md", "\"name\": \"execute_command\"", "`execute_command`")]
    public void HighRiskJsonExamples_ShouldNameEachGatedToolInLocalPrerequisites(
        string relativePath,
        string marker,
        string expectedToolReference)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));
        var markerIndex = content.IndexOf(marker, StringComparison.Ordinal);

        markerIndex.Should().BeGreaterThanOrEqualTo(0, $"{relativePath} should contain {marker}");
        var contextStart = Math.Max(0, markerIndex - 2_500);
        var context = content.Substring(contextStart, markerIndex - contextStart);

        context.Should().Contain("### Local prerequisites",
            $"{relativePath} should keep a local prerequisites block close to {marker}");
        context.Should().Contain(expectedToolReference,
            $"{relativePath} should explicitly name {expectedToolReference} in the local prerequisites near the example");
    }

    [Theory]
    [InlineData("docfx/guides/ai-agent-guide.md", "click it", "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS")]
    [InlineData("docfx/guides/ai-agent-guide.md", "ordered batch_mutate sequence", "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS")]
    [InlineData("docfx/zh-tw/guides/ai-agent-guide.md", "確認 command metadata、點擊", "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS")]
    [InlineData("docfx/zh-tw/guides/ai-agent-guide.md", "用 `batch_mutate` 做有順序", "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS")]
    public void AiGuidePromptBlocks_ShouldDocumentRequiredLocalPolicyGatesInsidePrompt(
        string relativePath,
        string marker,
        string expectedGate)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));
        var markerIndex = content.IndexOf(marker, StringComparison.Ordinal);

        markerIndex.Should().BeGreaterThanOrEqualTo(0, $"{relativePath} should contain {marker}");
        var fenceStart = content.LastIndexOf("```text", markerIndex, StringComparison.Ordinal);
        var fenceEnd = content.IndexOf("```", markerIndex, StringComparison.Ordinal);

        fenceStart.Should().BeGreaterThanOrEqualTo(0, $"{relativePath} should put {marker} inside a text prompt block");
        fenceEnd.Should().BeGreaterThan(markerIndex, $"{relativePath} should close the prompt block containing {marker}");
        var prompt = content.Substring(fenceStart, fenceEnd - fenceStart);

        prompt.Should().Contain("WPFDEVTOOLS_MCP_ALLOWED_TARGETS",
            $"{relativePath} prompt containing {marker} should keep the allowlist prerequisite inline");
        prompt.Should().Contain(expectedGate,
            $"{relativePath} prompt containing {marker} should keep the required local policy gate inline");
    }

    [Fact]
    public void ExamplesMarkdown_ShouldUseParserSafeFencedCodeBlocks()
    {
        var fences = GetExampleMarkdownRelativePaths()
            .SelectMany(GetFencedCodeBlocks)
            .ToArray();

        fences.Should().NotBeEmpty("example documentation should keep machine-checkable fenced examples");
        foreach (var fence in fences)
        {
            var language = NormalizeFenceLanguage(fence.Language);
            switch (language)
            {
                case "json":
                    using (JsonDocument.Parse(fence.Body))
                    {
                    }

                    break;
                case "powershell":
                case "pwsh":
                case "ps1":
                case "bash":
                case "sh":
                case "shell":
                    AssertCopyPasteSafeShellSnippet(fence);
                    break;
                default:
                    language.Should().BeOneOf("text", string.Empty,
                        $"{fence.RelativePath}:{fence.StartLine} should use a parser-safe fence language");
                    break;
            }
        }
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static string ReadAllExamplesMarkdown()
        => string.Join(
            Environment.NewLine,
            GetExampleMarkdownRelativePaths().Select(relativePath => File.ReadAllText(GetRepoFilePath(relativePath))));

    private static IReadOnlyList<string> GetExampleMarkdownRelativePaths()
    {
        var relativePaths = new List<string> { "EXAMPLES.md" };
        var examplesDirectory = GetRepoFilePath("examples");
        if (!Directory.Exists(examplesDirectory))
        {
            return relativePaths;
        }

        relativePaths.AddRange(
            Directory.GetFiles(examplesDirectory, "*.md", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetRelativePath(GetRepoFilePath("."), path))
                .Order(StringComparer.OrdinalIgnoreCase));

        return relativePaths;
    }

    private static IEnumerable<FencedCodeBlock> GetFencedCodeBlocks(string relativePath)
    {
        var lines = File.ReadAllLines(GetRepoFilePath(relativePath));
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!line.StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            var language = line[3..].Trim();
            var startLine = index + 1;
            var body = new List<string>();
            index++;

            while (index < lines.Length && !lines[index].StartsWith("```", StringComparison.Ordinal))
            {
                body.Add(lines[index]);
                index++;
            }

            index.Should().BeLessThan(lines.Length, $"{relativePath}:{startLine} should close its fenced code block");
            yield return new FencedCodeBlock(relativePath, startLine, language, string.Join(Environment.NewLine, body));
        }
    }

    private static string NormalizeFenceLanguage(string language)
        => language.Trim().ToLowerInvariant();

    private static void AssertCopyPasteSafeShellSnippet(FencedCodeBlock fence)
    {
        fence.Body.Should().NotContain("$ ", $"{fence.RelativePath}:{fence.StartLine} should omit shell prompts");
        fence.Body.Should().NotContain("PS> ", $"{fence.RelativePath}:{fence.StartLine} should omit PowerShell prompts");
        fence.Body.Should().NotContain("...", $"{fence.RelativePath}:{fence.StartLine} should not rely on ellipsis placeholders");
    }

    private sealed record FencedCodeBlock(string RelativePath, int StartLine, string Language, string Body);
}
