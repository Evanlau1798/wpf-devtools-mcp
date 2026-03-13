using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

/// <summary>
/// Serializes tests that use EventAnalyzer's static routed-event trace state.
/// </summary>
[CollectionDefinition("EventTrace", DisableParallelization = true)]
public sealed class EventTraceCollection
{
}
