using System.Text.RegularExpressions;
using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class GitHubActionsWorkflowSecurityTests
{
    private static readonly Regex UsesActionRegex = new(
        @"^\s*uses:\s+(?<action>[^@\s#]+)@(?<ref>[^\s#]+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex GitShaRegex = new(
        "^[0-9a-f]{40}$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    [Fact]
    public void GitHubActions_ShouldPinReusableActionsToImmutableShas()
    {
        var workflowDirectory = TestRepositoryPaths.GetRepoFilePath(".github/workflows");

        var unpinnedActions = Directory
            .EnumerateFiles(workflowDirectory, "*.yml", SearchOption.TopDirectoryOnly)
            .SelectMany(EnumerateUnpinnedActionReferences)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        unpinnedActions.Should().BeEmpty(
            "workflow dependencies should be pinned to immutable commit SHAs rather than mutable tags");
    }

    private static IEnumerable<string> EnumerateUnpinnedActionReferences(string workflowPath)
    {
        var relativePath = Path.GetRelativePath(
            TestRepositoryPaths.GetRepoFilePath("."),
            workflowPath).Replace('\\', '/');
        var lines = File.ReadLines(workflowPath).Select((text, index) => new { Text = text, Number = index + 1 });

        foreach (var line in lines)
        {
            var match = UsesActionRegex.Match(line.Text);
            if (!match.Success)
            {
                continue;
            }

            var actionRef = match.Groups["ref"].Value;
            if (!GitShaRegex.IsMatch(actionRef))
            {
                yield return $"{relativePath}:{line.Number}: {line.Text.Trim()}";
            }
        }
    }
}
