using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestCollectionOrderer(
    "WpfDevTools.Tests.Integration.LiveBootstrapFirstCollectionOrderer",
    "WpfDevTools.Tests.Integration")]

namespace WpfDevTools.Tests.Integration;

public sealed class LiveBootstrapFirstCollectionOrderer : ITestCollectionOrderer
{
    private const string LiveBootstrapCollectionName = "LiveBootstrapIntegration";

    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
    {
        return testCollections
            .OrderBy(static collection => IsLiveBootstrapCollection(collection) ? 0 : 1)
            .ThenBy(static collection => collection.DisplayName, StringComparer.Ordinal);
    }

    private static bool IsLiveBootstrapCollection(ITestCollection collection)
        => collection.DisplayName.Contains(LiveBootstrapCollectionName, StringComparison.Ordinal);
}
