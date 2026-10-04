using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class AccessMcpToolsStatusTests
{
    [Fact]
    public void BuildStatus_ExplicitEnable_ShouldReportPreauthorized()
    {
        using var document = BuildStatus(new Dictionary<string, string?>
        {
            [McpServerConfiguration.AllowDestructiveToolsEnvVar] = "true"
        });

        GetCapability(document, SessionAccessCapabilities.ComposerPreview)
            .GetProperty("status").GetString().Should().Be("preauthorized");
        document.RootElement.GetProperty("missingCapabilities").EnumerateArray()
            .Should().NotContain(item => item.GetString() == SessionAccessCapabilities.ComposerPreview);
    }

    [Fact]
    public void BuildStatus_ExplicitDisable_ShouldReportHardDeniedWithoutSuggestion()
    {
        using var document = BuildStatus(new Dictionary<string, string?>
        {
            [McpServerConfiguration.AllowDestructiveToolsEnvVar] = "false"
        });

        GetCapability(document, SessionAccessCapabilities.ComposerPreview)
            .GetProperty("status").GetString().Should().Be("hard-denied");
        document.RootElement.GetProperty("unavailableCapabilities").EnumerateArray()
            .Should().Contain(item => item.GetString() == SessionAccessCapabilities.ComposerPreview);
        document.RootElement.GetProperty("suggestedRequests").EnumerateArray()
            .Should().NotContain(request => request.GetProperty("capabilities").EnumerateArray()
                .Any(item => item.GetString() == SessionAccessCapabilities.ComposerPreview));
    }

    [Fact]
    public void BuildStatus_ShouldSeparateRawInjectionAsOneTimeRequest()
    {
        using var document = BuildStatus(new Dictionary<string, string?>(), processId: 42);
        var requests = document.RootElement.GetProperty("suggestedRequests").EnumerateArray().ToArray();

        requests.Single(request => request.GetProperty("lifetime").GetString() == "once")
            .GetProperty("capabilities").EnumerateArray()
            .Should().ContainSingle(item => item.GetString() == SessionAccessCapabilities.RawInjection);
        requests.Single(request => request.GetProperty("lifetime").GetString() == "session")
            .GetProperty("capabilities").EnumerateArray()
            .Should().NotContain(item => item.GetString() == SessionAccessCapabilities.RawInjection);
    }

    private static JsonDocument BuildStatus(
        IReadOnlyDictionary<string, string?> environment,
        int? processId = null,
        bool supportsElicitation = true)
    {
        using var store = new SessionAccessGrantStore();
        var resolver = new SessionAccessScopeResolver(
            id => new TargetProcessIdentity(id, 123, $@"C:\Apps\Target{id}.exe"),
            () => null);
        var service = new SessionAccessRequestService(
            store,
            resolver,
            () => DateTimeOffset.UtcNow,
            name => environment.TryGetValue(name, out var value) ? value : null);
        var payload = AccessMcpTools.BuildStatus(
            service,
            processId,
            null,
            null,
            supportsElicitation);
        return JsonDocument.Parse(JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static JsonElement GetCapability(JsonDocument document, string capability)
        => document.RootElement.GetProperty("capabilities").EnumerateArray()
            .Single(item => item.GetProperty("capability").GetString() == capability);

    [Fact]
    public void BuildStatus_UnsupportedClient_ShouldDiscloseUnavailableConsentWithoutRequests()
    {
        using var document = BuildStatus(new Dictionary<string, string?>(), processId: 42,
            supportsElicitation: false);

        document.RootElement.GetProperty("interactiveConsentAvailable").GetBoolean().Should().BeFalse();
        GetCapability(document, SessionAccessCapabilities.Screenshot)
            .GetProperty("errorCode").GetString().Should().Be("InteractiveConsentUnavailable");
        document.RootElement.GetProperty("suggestedRequests").EnumerateArray().Should().BeEmpty();
        document.RootElement.GetProperty("requestableCapabilities").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public void BuildStatus_UnsupportedClient_ShouldKeepPreauthorizedCapabilityUsable()
    {
        using var document = BuildStatus(new Dictionary<string, string?>
        {
            [McpServerConfiguration.AllowScreenshotsEnvVar] = "true"
        }, processId: 42, supportsElicitation: false);

        GetCapability(document, SessionAccessCapabilities.Screenshot)
            .GetProperty("status").GetString().Should().Be("preauthorized");
    }
}
