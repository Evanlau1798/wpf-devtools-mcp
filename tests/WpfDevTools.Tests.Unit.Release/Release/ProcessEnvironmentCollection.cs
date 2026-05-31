using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

/// <summary>
/// Serializes release tests that mutate process-wide environment variables.
/// </summary>
[CollectionDefinition("ProcessEnvironment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
}
