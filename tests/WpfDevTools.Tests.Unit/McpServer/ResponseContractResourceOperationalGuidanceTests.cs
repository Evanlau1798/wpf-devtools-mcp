using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ResponseContractResourceOperationalGuidanceTests
{
    [Fact]
    public void ResponseContractResource_ShouldExposeMillisecondRateLimitBackoff()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var errorPayload = document.RootElement.GetProperty("errorPayload");

        errorPayload.GetProperty("compatibilityProjectionFields")
            .EnumerateArray()
            .Select(field => field.GetString())
            .Should()
            .Contain("retryAfterMs");

        var retryAfterMs = errorPayload
            .GetProperty("recovery")
            .GetProperty("properties")
            .GetProperty("retryAfterMs");
        retryAfterMs.GetProperty("type").GetString().Should().Be("integer");
        retryAfterMs.GetProperty("optional").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ResponseContractResource_ShouldExposePolicyProfilesForCommonAgentWorkflows()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var profiles = document.RootElement.GetProperty("policyProfiles");

        profiles.GetArrayLength().Should().BeGreaterThanOrEqualTo(4);
        AssertPolicyProfile(
            profiles,
            "inspect-only",
            McpServerConfiguration.AllowedTargetsEnvVar,
            McpServerConfiguration.AllowSensitiveReadsEnvVar);
        AssertPolicyProfile(
            profiles,
            "screenshot-evidence",
            McpServerConfiguration.AllowedTargetsEnvVar,
            McpServerConfiguration.AllowScreenshotsEnvVar);
        AssertPolicyProfile(
            profiles,
            "mutation-safe",
            McpServerConfiguration.AllowedTargetsEnvVar,
            McpServerConfiguration.AllowDestructiveToolsEnvVar);
        AssertPolicyProfile(
            profiles,
            "mvvm-inspection",
            McpServerConfiguration.AllowedTargetsEnvVar,
            McpServerConfiguration.AllowViewModelInspectionEnvVar);
    }

    [Fact]
    public void ServerInstructions_ShouldPointAgentsToPolicyProfiles()
    {
        ServerInstructions.Value.Should().Contain("policyProfiles");
        ServerInstructions.Value.Should().Contain("wpf://contracts/response");
    }

    private static void AssertPolicyProfile(JsonElement profiles, string name, params string[] envVars)
    {
        var profile = profiles
            .EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == name);

        var requiredEnvVars = profile.GetProperty("requiredEnvVars")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("name").GetString())
            .ToArray();
        requiredEnvVars.Should().Contain(envVars);
        profile.GetProperty("agentUse").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
