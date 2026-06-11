using Xunit;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Serializes live bootstrap/connect smoke tests because they launch real WPF
/// processes and exercise shared injector/bootstrapper runtime state.
/// </summary>
[CollectionDefinition("LiveBootstrapIntegration", DisableParallelization = true)]
public sealed class LiveBootstrapIntegrationCollection
{
}
