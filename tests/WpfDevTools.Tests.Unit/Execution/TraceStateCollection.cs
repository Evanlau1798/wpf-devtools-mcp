using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

/// <summary>
/// Serializes tests that mutate global trace listeners or static audit logger state.
/// </summary>
[CollectionDefinition("TraceState", DisableParallelization = true)]
public sealed class TraceStateCollection
{
}
