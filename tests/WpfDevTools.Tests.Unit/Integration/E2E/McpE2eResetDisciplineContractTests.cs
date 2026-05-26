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

    private static readonly string[] SelfContainedMcpE2eTestFiles =
    {
        // This test file starts its own MCP server and TestApp instead of mutating the shared McpE2E fixture.
        "NestedExecuteCommandPolicyE2eTests.cs"
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
                && !path.EndsWith("SharedStateMcpE2eTestBase.cs", StringComparison.Ordinal)
                && !SelfContainedMcpE2eTestFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            .SelectMany(GetMcpE2eClassContracts)
            .Where(static contract => contract.UsesSharedStateSensitiveTool)
            .Where(static contract => !contract.InheritsSharedResetBase)
            .Select(static contract => $"{Path.GetFileName(contract.Path)}::{contract.ClassName}")
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        violatingClasses.Should().BeEmpty(
            "shared McpE2E tests that mutate app/UI state or attach global monitoring must inherit SharedStateMcpE2eTestBase so each test resets the shared TestApp session before and after execution");
    }

    [Fact]
    public void SelfContainedLiveSecurityE2e_ShouldFailRuntimeSetupFailuresVisibly()
    {
        var path = TestRepositoryPaths.GetRepoFilePath(
            "tests/WpfDevTools.Tests.Integration/E2E/NestedExecuteCommandPolicyE2eTests.cs");

        var content = File.ReadAllText(path);
        var initializeBody = ExtractMethodBody(content, "InitializeAsync");
        var liveTry = Regex.Match(initializeBody, @"(?m)^\s*try\s*$", RegexOptions.CultureInvariant);
        liveTry.Success.Should().BeTrue("the live setup block should have an explicit top-level try before starting child processes");

        initializeBody.Should().NotContain("SkipException.ForSkip",
            "self-contained live security E2E prerequisites must fail visibly instead of producing skipped tests");

        initializeBody.Should().Contain("MCP Server executable not found");
        initializeBody.Should().Contain("TestApp executable not found");
        initializeBody.Should().Contain("Native bootstrapper DLLs not found");

        var catchBody = ExtractCatchBody(initializeBody, @"(?m)^\s*catch\s*\(\s*Exception\s+\w+\s*\)\s*$");
        catchBody.Should().NotContain("SkipException.ForSkip",
            "live setup/connect failures should fail visibly rather than being reported as skipped");

        var disposeIndex = catchBody.IndexOf("Dispose();", StringComparison.Ordinal);
        var visibleFailureIndex = catchBody.IndexOf("throw new InvalidOperationException", StringComparison.Ordinal);
        disposeIndex.Should().BeGreaterOrEqualTo(0, "live setup failure handling must clean up partial-start resources");
        visibleFailureIndex.Should().BeGreaterThan(disposeIndex,
            "cleanup should run before the visible setup failure is thrown");
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

    private static string ExtractMethodBody(string content, string methodName)
    {
        var methodIndex = content.IndexOf(methodName, StringComparison.Ordinal);
        methodIndex.Should().BeGreaterOrEqualTo(0);

        var bodyStartIndex = content.IndexOf('{', methodIndex);
        bodyStartIndex.Should().BeGreaterOrEqualTo(methodIndex);

        var bodyEndIndex = FindMatchingBrace(content, bodyStartIndex);
        bodyEndIndex.Should().BeGreaterThan(bodyStartIndex);

        return content.Substring(bodyStartIndex, bodyEndIndex - bodyStartIndex + 1);
    }

    private static string ExtractCatchBody(string content, string catchPattern)
    {
        var catchMatch = Regex.Match(content, catchPattern, RegexOptions.CultureInvariant);
        catchMatch.Success.Should().BeTrue();
        var catchIndex = catchMatch.Index;

        var bodyStartIndex = content.IndexOf('{', catchIndex);
        bodyStartIndex.Should().BeGreaterOrEqualTo(catchIndex);

        var bodyEndIndex = FindMatchingBrace(content, bodyStartIndex);
        bodyEndIndex.Should().BeGreaterThan(bodyStartIndex);

        return content.Substring(bodyStartIndex, bodyEndIndex - bodyStartIndex + 1);
    }

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
