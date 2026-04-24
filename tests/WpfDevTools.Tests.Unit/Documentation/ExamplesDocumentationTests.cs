using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ExamplesDocumentationTests
{
    [Fact]
    public void Examples_ShouldUseValidCaptureStateSnapshotArguments()
    {
        var content = File.ReadAllText(GetRepoFilePath("EXAMPLES.md"));

        var startIndex = content.IndexOf("\"name\": \"capture_state_snapshot\"", StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, "EXAMPLES.md should include a capture_state_snapshot example");

        var exampleWindow = content.Substring(startIndex, Math.Min(220, content.Length - startIndex));
        exampleWindow.Should().Contain("\"includeFocus\": true",
            "capture_state_snapshot requires at least one explicit snapshot scope argument in the public example");
    }

    [Fact]
    public void Examples_ShouldUseSupportedGetProcessesFilters()
    {
        var content = File.ReadAllText(GetRepoFilePath("EXAMPLES.md"));

        content.Should().NotContain("\"windowFilter\": \"MyApp\"",
            "windowFilter only accepts visible/all/foreground and should not be documented as a free-text name filter");
        content.Should().Contain("\"nameFilter\": \"MyApp\"",
            "examples should demonstrate process name narrowing with nameFilter instead of an invalid windowFilter value");
    }

    [Fact]
    public void Examples_ShouldContainOnlyCurrentSceneFirstEdition()
    {
        var content = File.ReadAllText(GetRepoFilePath("EXAMPLES.md"));

        Regex.Matches(content, "^# WPF DevTools MCP Server - Usage Examples\r?$", RegexOptions.Multiline)
            .Should().HaveCount(1, "EXAMPLES.md should not append a stale second edition below the current scene-first guidance");
        content.Should().NotContain("// 1. List running WPF processes",
            "examples should not regress to a list-first workflow as the default entry path");
        content.Should().NotContain("// 1. Get the Visual Tree to find elements with DataContext",
            "examples should keep scene-first guidance ahead of tree-heavy workflows");
        content.Should().NotContain("`TIMEOUT` ->",
            "examples should not keep stale uppercase legacy error-code snippets from an appended older edition");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}