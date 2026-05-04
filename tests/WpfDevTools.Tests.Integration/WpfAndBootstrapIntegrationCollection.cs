using Xunit;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Merged serialization lane for WpfIntegration and LiveBootstrapIntegration.
/// These two test families share WPF runtime and named-pipe resources and must
/// not execute concurrently, but they can run in parallel with the isolated
/// PackagingIntegration and McpE2E lanes.
/// </summary>
[CollectionDefinition("WpfAndBootstrapIntegration", DisableParallelization = true)]
public class WpfAndBootstrapIntegrationCollection : ICollectionFixture<WpfApplicationFixture>
{
}
