namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// Dedicated xUnit collection for visibility diagnosis E2E tests.
/// Uses an isolated MCP fixture to avoid shared-session rate limiting from the broader E2E suite.
/// </summary>
[CollectionDefinition("VisibilityMcpE2E")]
public sealed class VisibilityDiagnosisE2eCollection : ICollectionFixture<McpE2eFixture>
{
}
