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
    public void EnumerateDescendantsAndSelf_WithLargeNodeBudget_ShouldNotPreallocateRemainingBudgetPerNode()
    {
        var root = new CountingLogicalElement(new Button());
        DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 50, maxNodes: 2)
            .ToList();

        var before = GC.GetAllocatedBytesForCurrentThread();

        var traversal = DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 50, maxNodes: 10_000);
        var result = traversal.ToList();

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        result.Should().HaveCount(2);
        traversal.Truncated.Should().BeFalse();
        allocatedBytes.Should().BeLessThan(64 * 1024,
            "traversing a tiny tree with a large budget should not allocate arrays sized to the remaining node budget");
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

    [StaFact]
    public void EnumerateDescendantsAndSelf_WithDepthPrunedDescendant_ShouldReportTruncation()
    {
        var grandchild = new Button();
        var child = new CountingLogicalElement(grandchild);
        var root = new CountingLogicalElement(child);

        var traversal = DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 1);
        var result = traversal.ToList();

        result.Should().HaveCount(2);
        result[0].Should().BeSameAs(root);
        result[1].Should().BeSameAs(child);
        traversal.Truncated.Should().BeTrue();
    }

    [StaFact]
    public void EnumerateDescendantsAndSelf_WithDuplicateRouteBeyondDepth_ShouldNotReportTruncation()
    {
        var shared = new Button();
        var parent = new CountingLogicalElement(shared);
        var root = new CountingLogicalElement([shared, parent]);

        var traversal = DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 1);
        var result = traversal.ToList();

        result.Should().ContainInOrder(root, shared, parent);
        result.Should().HaveCount(3);
        traversal.Truncated.Should().BeFalse();
    }

    [StaFact]
    public void EnumerateDescendantsAndSelf_WithOverDepthDuplicateBeforeShallowRoute_ShouldNotReportTruncation()
    {
        var shared = new Button();
        var parent = new CountingLogicalElement(shared);
        var root = new CountingLogicalElement([parent, shared]);

        var traversal = DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 1);
        var result = traversal.ToList();

        result.Should().ContainInOrder(root, parent, shared);
        result.Should().HaveCount(3);
        traversal.Truncated.Should().BeFalse();
    }

    [StaFact]
    public void EnumerateDescendantsAndSelf_WithBudgetEndingAfterDepthPrunedCandidate_ShouldReportTruncation()
    {
        var deepUnique = new Button();
        var parent = new CountingLogicalElement(deepUnique);
        var sibling = new Button();
        var root = new CountingLogicalElement([parent, sibling]);

        var traversal = DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 1, maxNodes: 3);
        var result = traversal.ToList();

        result.Should().ContainInOrder(root, parent, sibling);
        result.Should().HaveCount(3);
        traversal.Truncated.Should().BeTrue();
    }

    [StaFact]
    public void EnumerateDescendantsAndSelf_WithExactBudgetAndDuplicateChild_ShouldNotReportTruncation()
    {
        var shared = new Button();
        var parent = new CountingLogicalElement(shared);
        var root = new CountingLogicalElement([shared, parent]);

        var traversal = DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 1, maxNodes: 3);
        var result = traversal.ToList();

        result.Should().ContainInOrder(root, shared, parent);
        result.Should().HaveCount(3);
        traversal.Truncated.Should().BeFalse();
    }

    [StaFact]
    public void EnumerateDescendantsAndSelf_WithDuplicateChildAfterLastUniqueBudgetNode_ShouldNotReportTruncation()
    {
        var shared = new Button();
        var unique = new Button();
        var parent = new CountingLogicalElement([unique, shared]);
        var root = new CountingLogicalElement([shared, parent]);

        var traversal = DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 2, maxNodes: 4);
        var result = traversal.ToList();

        result.Should().ContainInOrder(root, shared, parent, unique);
        result.Should().HaveCount(4);
        traversal.Truncated.Should().BeFalse();
    }

    [StaFact]
    public void EnumerateDescendantsAndSelf_WithDuplicateChildBeforeUniqueBudgetNode_ShouldStillYieldUniqueNode()
    {
        var shared = new Button();
        var unique = new Button();
        var parent = new CountingLogicalElement([shared, unique]);
        var root = new CountingLogicalElement([shared, parent]);

        var traversal = DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 2, maxNodes: 4);
        var result = traversal.ToList();

        result.Should().ContainInOrder(root, shared, parent, unique);
        result.Should().HaveCount(4);
        traversal.Truncated.Should().BeFalse();
    }

    [StaFact]
    public void EnumerateDescendantsAndSelf_WithDeeperDuplicateBeforeShallowRoute_ShouldExpandShallowRoute()
    {
        var allowedGrandchild = new Button();
        var shared = new CountingLogicalElement(allowedGrandchild);
        var parent = new CountingLogicalElement(shared);
        var root = new CountingLogicalElement([parent, shared]);

        var traversal = DependencyObjectTraversal
            .EnumerateDescendantsAndSelfWithMetadata(root, maxDepth: 2);
        var result = traversal.ToList();

        result.Should().ContainInOrder(root, parent, shared, allowedGrandchild);
        result.Should().HaveCount(4);
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
