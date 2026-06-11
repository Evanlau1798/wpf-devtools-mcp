using FluentAssertions;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ToolCallHelperPayloadPipelineTests
{
    [Fact]
    public void ExecuteAndWrapAsync_ShouldUseSinglePayloadNormalizationPipeline()
    {
        var sourceRoot = FindSourceRoot();
        var wrapperSource = File.ReadAllText(Path.Combine(
            sourceRoot,
            "src",
            "WpfDevTools.Mcp.Server",
            "McpTools",
            "ToolCallHelper.cs"));
        var pipelineSource = File.ReadAllText(Path.Combine(
            sourceRoot,
            "src",
            "WpfDevTools.Mcp.Server",
            "McpTools",
            "ToolCallHelper.PayloadRewrite.cs"));

        wrapperSource.Should().Contain("NormalizeToolPayload(");
        wrapperSource.Should().NotContain("EnsureNavigation(");
        wrapperSource.Should().NotContain("ApplyToolSpecificContracts(");
        wrapperSource.Should().NotContain("NormalizePendingEventsContract(");
        wrapperSource.Should().NotContain("NormalizeErrorContract(");

        CountOccurrences(pipelineSource, "JsonDocument.Parse(buffer.WrittenMemory)")
            .Should()
            .Be(1, "the payload wrapper should parse the rewritten payload once after a single writer pass");
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string FindSourceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
