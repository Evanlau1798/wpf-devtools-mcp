using Xunit;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Collection definition to ensure all WPF integration tests share the same Application instance
/// and stay off the collection-parallel scheduler because they can still contend with
/// live bootstrap/injection lanes through shared WPF and runtime resources.
/// </summary>
[CollectionDefinition("WpfIntegration", DisableParallelization = true)]
public class WpfIntegrationCollection : ICollectionFixture<WpfApplicationFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
