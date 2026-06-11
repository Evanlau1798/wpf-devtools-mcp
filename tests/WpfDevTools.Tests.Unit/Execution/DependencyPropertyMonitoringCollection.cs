using Xunit;

namespace WpfDevTools.Tests.Unit.Execution;

/// <summary>
/// Serializes tests that mutate DependencyPropertyAnalyzer's shared watcher registry and change log.
/// </summary>
[CollectionDefinition("DependencyPropertyMonitoring", DisableParallelization = true)]
public sealed class DependencyPropertyMonitoringCollection
{
}