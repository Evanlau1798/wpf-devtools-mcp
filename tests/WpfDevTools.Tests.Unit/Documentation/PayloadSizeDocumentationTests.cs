using FluentAssertions;
using WpfDevTools.Shared.Serialization;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PayloadSizeDocumentationTests
{
    [Fact]
    public void MessageFraming_ShouldDocumentTenMegabyteHardFrameLimitStrategy()
    {
        MessageFraming.MaxMessageSizeBytes.Should().Be(10 * 1024 * 1024);

        var content = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Shared/Serialization/MessageFraming.cs"));

        content.Should().Contain("hard per-frame limit");
        content.Should().Contain("tool-level caps");
        content.Should().Contain("resource handles");
        content.Should().Contain("streaming or chunking");
    }

    [Theory]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    public void ProductionSecurityDocs_ShouldDocumentIpcPayloadSizeStrategy(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("10 MB");
        content.Should().Contain("MessageFraming.MaxMessageSizeBytes");
        content.Should().Contain("tool-level caps");
        content.Should().Contain("resource handles");
        content.Should().Contain("streaming or chunking");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
