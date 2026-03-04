using Xunit;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Collection definition to ensure all WPF integration tests share the same Application instance
/// and run sequentially (not in parallel)
/// </summary>
[CollectionDefinition("WpfIntegration")]
public class WpfIntegrationCollection : ICollectionFixture<WpfApplicationFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
