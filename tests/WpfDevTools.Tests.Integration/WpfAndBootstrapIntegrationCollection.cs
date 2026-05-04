using Xunit;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Merged serialization lane for WpfIntegration and LiveBootstrapIntegration.
/// These two test families share WPF runtime and named-pipe resources and must
/// not execute concurrently, but this lane itself should remain eligible for
/// collection-level scheduling when other collections also allow it.
/// </summary>
[CollectionDefinition("WpfAndBootstrapIntegration")]
public class WpfAndBootstrapIntegrationCollection : ICollectionFixture<WpfApplicationFixture>
{
}
