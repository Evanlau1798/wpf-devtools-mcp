using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

/// <summary>
/// Serializes tests that mutate ToolCallHelper's shared cache or metrics collector.
/// </summary>
[CollectionDefinition("ToolCallHelperState", DisableParallelization = true)]
public sealed class ToolCallHelperStateCollection
{
}
