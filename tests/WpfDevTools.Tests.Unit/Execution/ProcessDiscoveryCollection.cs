using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

/// <summary>
/// Serializes tests that perform live workstation process discovery.
/// </summary>
[CollectionDefinition("ProcessDiscovery", DisableParallelization = true)]
public sealed class ProcessDiscoveryCollection
{
}
