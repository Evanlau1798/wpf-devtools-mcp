using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

/// <summary>
/// Serializes InspectorHost lifecycle tests that swap global cleanup hooks.
/// </summary>
[CollectionDefinition("InspectorHostLifecycle", DisableParallelization = true)]
public sealed class InspectorHostLifecycleCollection
{
}