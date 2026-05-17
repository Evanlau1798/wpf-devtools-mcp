using System.Collections;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

public sealed class DependencyObjectTraversalTests
{
    [StaFact]
    public void EnumerateDescendantsAndSelf_WithMaxNodes_ShouldNotExpandChildrenAfterBudgetExhausted()
    {
        var root = new CountingLogicalElement(new Button());

        var traversal = DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 50, maxNodes: 1);
        var result = traversal.ToList();

        result.Should().ContainSingle().Which.Should().BeSameAs(root);
        root.YieldedLogicalChildrenCount.Should().Be(1);
        traversal.Truncated.Should().BeTrue();
    }

    [StaFact]
    public void EnumerateDescendantsAndSelf_WithChainBeyondBudget_ShouldReportTruncation()
    {
        var grandchild = new Button();
        var child = new CountingLogicalElement(grandchild);
        var root = new CountingLogicalElement(child);

        var traversal = DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 50, maxNodes: 2);
        var result = traversal.ToList();

        result.Should().HaveCount(2);
        result[0].Should().BeSameAs(root);
        result[1].Should().BeSameAs(child);
        child.YieldedLogicalChildrenCount.Should().Be(1);
        traversal.Truncated.Should().BeTrue();
    }

    [StaFact]
    public void EnumerateDescendantsAndSelf_WithRemainingBudget_ShouldNotEnumerateAllChildren()
    {
        var root = new CountingLogicalElement(Enumerable
            .Range(0, 10)
            .Select(_ => new Button())
            .Cast<DependencyObject>());

        var traversal = DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 50, maxNodes: 2);
        var result = traversal.ToList();

        result.Should().HaveCount(2);
        root.YieldedLogicalChildrenCount.Should().BeLessThan(10);
        traversal.Truncated.Should().BeTrue();
    }

    [StaFact]
    public void EnumerateDescendantsAndSelf_WithTreeExactlyAtBudget_ShouldNotReportTruncation()
    {
        var root = new CountingLogicalElement(new Button());

        var traversal = DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 50, maxNodes: 2);
        var result = traversal.ToList();

        result.Should().HaveCount(2);
        traversal.Truncated.Should().BeFalse();
    }

    private sealed class CountingLogicalElement : FrameworkElement
    {
        public CountingLogicalElement(DependencyObject child)
            : this(new[] { child })
        {
        }

        public CountingLogicalElement(IEnumerable<DependencyObject> children)
        {
            Children = children.ToArray();
        }

        private DependencyObject[] Children { get; }

        public int LogicalChildrenEnumerationCount { get; private set; }

        public int YieldedLogicalChildrenCount { get; private set; }

        protected override IEnumerator LogicalChildren
        {
            get
            {
                LogicalChildrenEnumerationCount++;
                foreach (var child in Children)
                {
                    YieldedLogicalChildrenCount++;
                    yield return child;
                }
            }
        }
    }
}
