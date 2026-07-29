using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void AddDescendantProcessIds_ShouldStopAtCycles()
    {
        var childrenByParent = new Dictionary<int, int[]>
        {
            [1] = [2],
            [2] = [1, 3],
        };
        var processIds = new List<int>();

        AddDescendantProcessIds(1, childrenByParent, processIds);

        processIds.Should().BeEquivalentTo([2, 3]);
    }
}
