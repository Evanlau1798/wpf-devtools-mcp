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

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}