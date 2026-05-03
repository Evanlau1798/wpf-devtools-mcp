using System.Text.RegularExpressions;
using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Integration.E2E;

public sealed class McpE2eResetDisciplineContractTests
{
    private static readonly Regex McpE2eClassRegex = new(
        "\\[Collection\\(\\\"McpE2E\\\"\\)\\](?<declaration>[\\s\\S]*?public\\s+(?:sealed\\s+)?class\\s+(?<name>\\w+)(?<bases>\\s*:\\s*[^\\{]+)?\\s*\\{)",
        RegexOptions.CultureInvariant);

    private static readonly string[] SharedStateSensitiveCallPatterns =
    {
        "CallToolAsync\\(\\s*\"modify_viewmodel\"",
        "CallToolAsync\\(\\s*\"batch_mutate\"",
        "CallToolAsync\\(\\s*\"capture_state_snapshot\"",
        "CallToolAsync\\(\\s*\"restore_state_snapshot\"",
        "CallToolAsync\\(\\s*\"click_element\"",
        "CallToolAsync\\(\\s*\"fire_routed_event\"",
        "CallToolAsync\\(\\s*\"set_dp_value\"",
        "CallToolAsync\\(\\s*\"clear_dp_value\"",
        "CallToolAsync\\(\\s*\"watch_dp_changes\"",
        "CallToolAsync\\(\\s*\"execute_command\"",
        "CallToolAsync\\(\\s*\"simulate_keyboard\"",
        "CallToolAsync\\(\\s*\"focus_element\"",
        "CallToolAsync\\(\\s*\"drag_and_drop\"",
        "CallToolAsync\\(\\s*\"get_render_stats\""
    };

    private static readonly string[] MutationPayloadPatterns =
    {
        "triggerMutation\\s*="
    };

    [Fact]
    public void SharedMcpE2eTests_WithSharedStateSensitiveTools_ShouldInheritSharedStateResetBase()
    {
        var e2eDirectory = Path.GetDirectoryName(
            TestRepositoryPaths.GetRepoFilePath("tests/WpfDevTools.Tests.Integration/E2E/McpE2eCollection.cs"));
        e2eDirectory.Should().NotBeNull();

        var violatingClasses = Directory.EnumerateFiles(e2eDirectory!, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith("McpE2eCollection.cs", StringComparison.Ordinal)
                && !path.EndsWith("McpE2eFixture.cs", StringComparison.Ordinal)
                && !path.EndsWith("McpStdioClient.cs", StringComparison.Ordinal)
                && !path.EndsWith("SharedStateMcpE2eTestBase.cs", StringComparison.Ordinal))
            .SelectMany(GetMcpE2eClassContracts)
            .Where(static contract => contract.UsesSharedStateSensitiveTool)
            .Where(static contract => !contract.InheritsSharedResetBase)
            .Select(static contract => $"{Path.GetFileName(contract.Path)}::{contract.ClassName}")
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        violatingClasses.Should().BeEmpty(
            "shared McpE2E tests that mutate app/UI state or attach global monitoring must inherit SharedStateMcpE2eTestBase so each test resets the shared TestApp session before and after execution");
    }

    private static IEnumerable<McpE2eClassContract> GetMcpE2eClassContracts(string path)
    {
        var content = File.ReadAllText(path);

        foreach (Match match in McpE2eClassRegex.Matches(content))
        {
            var bodyStartIndex = content.IndexOf('{', match.Index + match.Length - 1);
            bodyStartIndex.Should().BeGreaterOrEqualTo(0, "each matched McpE2E class declaration should contain an opening brace");

            var bodyEndIndex = FindMatchingBrace(content, bodyStartIndex);
            bodyEndIndex.Should().BeGreaterOrEqualTo(bodyStartIndex, "each matched McpE2E class declaration should contain a balanced body");

            var classBody = content.Substring(bodyStartIndex, bodyEndIndex - bodyStartIndex + 1);
            var baseList = match.Groups["bases"].Value;

            yield return new McpE2eClassContract(
                path,
                match.Groups["name"].Value,
                baseList,
                classBody,
                UsesSharedStateSensitiveTool(classBody),
                InheritsSharedResetBase(baseList));
        }
    }

    private static bool UsesSharedStateSensitiveTool(string classBody)
        => SharedStateSensitiveCallPatterns.Any(pattern => Regex.IsMatch(classBody, pattern, RegexOptions.CultureInvariant))
            || MutationPayloadPatterns.Any(pattern => Regex.IsMatch(classBody, pattern, RegexOptions.CultureInvariant));

    private static bool InheritsSharedResetBase(string baseList)
        => baseList.Contains("SharedStateMcpE2eTestBase", StringComparison.Ordinal);

    private static int FindMatchingBrace(string content, int openingBraceIndex)
    {
        var depth = 0;

        for (var index = openingBraceIndex; index < content.Length; index++)
        {
            var current = content[index];
            if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private sealed record McpE2eClassContract(
        string Path,
        string ClassName,
        string BaseList,
        string ClassBody,
        bool UsesSharedStateSensitiveTool,
        bool InheritsSharedResetBase);
}
