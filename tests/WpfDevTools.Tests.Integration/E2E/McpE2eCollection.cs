using Xunit;

namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// xUnit collection that shares a single McpE2eFixture across all E2E tests.
/// All tests in this collection run sequentially with a shared TestApp + MCP Server.
/// </summary>
[CollectionDefinition("McpE2E")]
public class McpE2eCollection : ICollectionFixture<McpE2eFixture>
{
}
