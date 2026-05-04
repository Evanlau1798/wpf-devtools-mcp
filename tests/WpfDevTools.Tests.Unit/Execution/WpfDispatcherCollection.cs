using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

/// <summary>
/// Serializes tests that create real WPF UI elements which require STA thread affinity
/// and share WPF dispatcher resources. Parallel execution of these tests would cause
/// cross-thread access violations or dispatcher conflicts.
/// </summary>
[CollectionDefinition("WPF", DisableParallelization = true)]
public sealed class WpfDispatcherCollection
{
}
