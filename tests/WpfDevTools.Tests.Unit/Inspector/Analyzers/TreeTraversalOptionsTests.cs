using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class TreeTraversalOptionsTests
{
    private const int ExpectedDefaultMaxNodes = 1000;
    private const int ExpectedDefaultMaxChildrenPerNode = 200;

    [Fact]
    public void Create_WhenCapsAreOmitted_ShouldApplySafeDefaultCaps()
    {
        var options = TreeTraversalOptions.Create(
            depth: null,
            compact: null,
            summaryOnly: null,
            maxNodes: null,
            maxChildrenPerNode: null);

        options.MaxNodes.Should().Be(ExpectedDefaultMaxNodes);
        options.MaxChildrenPerNode.Should().Be(ExpectedDefaultMaxChildrenPerNode);
    }
}
