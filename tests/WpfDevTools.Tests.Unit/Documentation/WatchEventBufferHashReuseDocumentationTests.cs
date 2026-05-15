using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class WatchEventBufferHashReuseDocumentationTests
{
    [Fact]
    public void PayloadTruncationBuilder_ShouldReuseHashAlgorithmWithinRecord()
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Inspector/Events/WatchEventBuffer.cs"));

        content.Should().Contain("using var truncation = new PayloadTruncationBuilder()");
        content.Should().Contain("private SHA256? _sha256");
        content.Should().Contain("_sha256 ??= SHA256.Create()");
        content.Should().NotContain("using var sha256 = SHA256.Create()");
    }
}