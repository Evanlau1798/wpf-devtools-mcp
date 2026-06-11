using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

/// <summary>
/// Serializes tests that mutate process-wide environment variables.
/// </summary>
[CollectionDefinition("ProcessEnvironment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
}
