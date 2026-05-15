using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class InspectorHostStartContractTests
{
    [Fact]
    public void Start_ShouldDocumentBlockingUiThreadConstraint()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Inspector/Host/InspectorHost.cs"));
        var startDocumentation = ExtractStartDocumentation(content);

        startDocumentation.Should().Contain("startup completes or fails");
        startDocumentation.Should().Contain("must not be called from a WPF UI thread");
        startDocumentation.Should().Contain("background initialization path");
        content.Should().Contain("GetAwaiter().GetResult()",
            "the documentation should stay aligned with the current synchronous wait behavior");
    }

    private static string ExtractStartDocumentation(string content)
    {
        var summaryIndex = content.IndexOf("/// Start the Named Pipe server", StringComparison.Ordinal);
        summaryIndex.Should().BeGreaterThanOrEqualTo(0);

        var methodIndex = content.IndexOf("public void Start()", summaryIndex, StringComparison.Ordinal);
        methodIndex.Should().BeGreaterThan(summaryIndex);

        return content[summaryIndex..methodIndex];
    }

    private static string GetRepoFilePath(string relativePath)
        => TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}