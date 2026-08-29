using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Tests.Integration.TestSupport;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "Integration")]
public sealed class SessionAccessElicitationE2eTests
{
    [Fact]
    public async Task SameSession_ShouldDenyElicitGrantAndPassTheOriginalPolicyGate()
    {
        using var client = new McpStdioClient();
        var elicitationCount = 0;
        await client.StartWithElicitationAsync(
            FindServerExecutable(),
            new Dictionary<string, string>
            {
                [McpServerConfiguration.AllowDestructiveToolsEnvVar] = string.Empty
            },
            request =>
            {
                request.GetProperty("method").GetString().Should().Be("elicitation/create");
                request.GetProperty("params").GetProperty("message").GetString()
                    .Should().Contain("Agent-provided reason (untrusted)");
                elicitationCount++;
                return Task.FromResult<object>(new
                {
                    action = "accept",
                    content = new { confirm = true }
                });
            });

        var previewArguments = new { blueprintJson = "{}", restoreEnabled = false };
        var denied = await client.CallToolAsync("preview_ui_blueprint", previewArguments);
        denied.GetProperty("errorCode").GetString().Should().Be("InteractiveConsentRequired");

        var grant = await client.CallToolAsync("request_session_access", new
        {
            capabilities = new[] { "composer-preview" },
            reason = "Verify the isolated Composer preview flow.",
            lifetime = "session"
        });
        grant.GetProperty("success").GetBoolean().Should().BeTrue(grant.GetRawText());
        grant.GetProperty("status").GetString().Should().Be("granted");
        elicitationCount.Should().Be(1);

        var retried = await client.CallToolAsync("preview_ui_blueprint", previewArguments);
        retried.TryGetProperty("success", out _).Should().BeTrue(retried.GetRawText());
        if (retried.TryGetProperty("errorCode", out var retriedErrorCode))
        {
            retriedErrorCode.GetString().Should().NotBe("InteractiveConsentRequired");
        }

        var status = await client.CallToolAsync("get_access_status");
        status.GetProperty("capabilities").EnumerateArray()
            .Single(item => item.GetProperty("capability").GetString() == "composer-preview")
            .GetProperty("status").GetString().Should().Be("granted");
    }

    private static string FindServerExecutable()
        => IntegrationExecutableLocator.FindExecutable(
               AppContext.BaseDirectory,
               "src",
               "WpfDevTools.Mcp.Server",
               "net8.0",
               "WpfDevTools.Mcp.Server.exe")
           ?? throw new InvalidOperationException("Build the MCP server before this test.");
}
