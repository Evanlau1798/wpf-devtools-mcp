using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

/// <summary>
/// Serializes tests that manipulate shared WPF focus and keyboard interaction state.
/// </summary>
[CollectionDefinition("InteractionState", DisableParallelization = true)]
public sealed class InteractionStateCollection
{
}
