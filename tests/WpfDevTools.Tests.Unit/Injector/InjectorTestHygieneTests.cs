using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Injector;

public class InjectorTestHygieneTests
{
    [Theory]
    [InlineData("tests/WpfDevTools.Tests.Unit/Injector/InjectorTests.cs")]
    [InlineData("tests/WpfDevTools.Tests.Unit/Injector/PeArchitectureReaderTests.cs")]
    public void InjectorUnitTests_ShouldNotUseBareReturnAsSkip(string relativePath)
    {
        var path = TestRepositoryPaths.GetRepoFilePath(relativePath);
        var bareReturnLines = File.ReadLines(path)
            .Select((line, index) => new
            {
                LineNumber = index + 1,
                Text = line.Trim()
            })
            .Where(line => line.Text == "return;" || line.Text.StartsWith("return; ", StringComparison.Ordinal))
            .Select(line => $"{relativePath}:{line.LineNumber}: {line.Text}")
            .ToArray();

        bareReturnLines.Should().BeEmpty(
            "injector unit tests must fail loudly or build deterministic fixtures instead of silently skipping");
    }
}
