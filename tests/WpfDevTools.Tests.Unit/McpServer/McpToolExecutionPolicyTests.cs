using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolExecutionPolicyTests
{
    private static readonly HashSet<string> DestructivePolicyExceptions = new(StringComparer.Ordinal)
    {
        "connect",
        "select_active_process"
    };

    [Theory]
    [InlineData("click_element")]
    [InlineData("set_dp_value")]
    [InlineData("batch_mutate")]
    [InlineData("measure_element_render_time")]
    public void EvaluateToolCall_WhenDestructiveToolsAreDisabled_ShouldDenyDestructiveTool(string toolName)
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "false",
            allowScreenshots: null,
            allowViewModelInspection: null);

        var decision = policy.EvaluateToolCall(toolName);

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("SecurityError");
        decision.PolicyCategory.Should().Be("destructive-tools");
        decision.Hint.Should().Contain(McpServerConfiguration.AllowDestructiveToolsEnvVar);
    }

    [Fact]
    public void EvaluateToolCall_WhenModifyViewModelIsAllowedByViewModelGateButDestructiveToolsAreDisabled_ShouldDeny()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "false",
            allowScreenshots: null,
            allowViewModelInspection: "true");

        var decision = policy.EvaluateToolCall("modify_viewmodel");

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("SecurityError");
        decision.PolicyCategory.Should().Be("destructive-tools");
    }

    [Fact]
    public void EvaluateToolCall_WhenScreenshotsAreDisabled_ShouldDenyElementScreenshot()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: null,
            allowScreenshots: "0",
            allowViewModelInspection: null);

        var decision = policy.EvaluateToolCall("element_screenshot");

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("SecurityError");
        decision.PolicyCategory.Should().Be("screenshots");
        decision.Hint.Should().Contain(McpServerConfiguration.AllowScreenshotsEnvVar);
    }

    [Theory]
    [InlineData("get_viewmodel")]
    [InlineData("get_commands")]
    [InlineData("execute_command")]
    [InlineData("modify_viewmodel")]
    public void EvaluateToolCall_WhenViewModelInspectionIsDisabled_ShouldDenyViewModelTool(string toolName)
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: null,
            allowScreenshots: null,
            allowViewModelInspection: "no");

        var decision = policy.EvaluateToolCall(toolName);

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("SecurityError");
        decision.PolicyCategory.Should().Be("viewmodel-inspection");
        decision.Hint.Should().Contain(McpServerConfiguration.AllowViewModelInspectionEnvVar);
    }

    [Fact]
    public void EvaluateToolCall_WhenPolicyValuesAreUnset_ShouldFailClosedForHighRiskToolSurface()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: null,
            allowScreenshots: null,
            allowViewModelInspection: null);

        policy.EvaluateToolCall("click_element").IsAllowed.Should().BeFalse();
        policy.EvaluateToolCall("element_screenshot").IsAllowed.Should().BeFalse();
        policy.EvaluateToolCall("get_viewmodel").IsAllowed.Should().BeFalse();
        policy.EvaluateToolCall("get_visual_tree").IsAllowed.Should().BeFalse();
    }

    [Theory]
    [InlineData("get_visual_tree")]
    [InlineData("capture_state_snapshot")]
    [InlineData("get_ui_summary")]
    [InlineData("get_bindings")]
    [InlineData("get_dp_value_source")]
    [InlineData("wait_for_dp_change")]
    [InlineData("get_state_diff")]
    [InlineData("restore_state_snapshot")]
    [InlineData("set_dp_value")]
    [InlineData("clear_dp_value")]
    [InlineData("override_style_setter")]
    public void EvaluateToolCall_WhenSensitiveReadsAreDisabled_ShouldDenySensitiveReadTool(string toolName)
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "true",
            allowViewModelInspection: "true");

        var decision = policy.EvaluateToolCall(toolName);

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("SecurityError");
        decision.PolicyCategory.Should().Be("sensitive-reads");
        decision.Hint.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS");
    }

    [Theory]
    [InlineData("connect")]
    [InlineData("get_processes")]
    [InlineData("get_active_process")]
    [InlineData("ping")]
    public void EvaluateToolCall_WhenSensitiveReadsAreDisabled_ShouldAllowProcessLifecycleTool(string toolName)
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "true",
            allowViewModelInspection: "true");

        policy.EvaluateToolCall(toolName).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void EvaluateToolCall_WhenSensitiveReadsAreEnabled_ShouldAllowSensitiveReadTool()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "true",
            allowViewModelInspection: "true",
            allowSensitiveReads: "true");

        policy.EvaluateToolCall("get_ui_summary").IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void FromEnvironment_ShouldMapSensitiveReadAndViewModelGatesIndependently()
    {
        var variables = new[]
        {
            McpServerConfiguration.AllowDestructiveToolsEnvVar,
            McpServerConfiguration.AllowScreenshotsEnvVar,
            McpServerConfiguration.AllowSensitiveReadsEnvVar,
            McpServerConfiguration.AllowViewModelInspectionEnvVar
        };
        var originalValues = variables.ToDictionary(
            variable => variable,
            Environment.GetEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(McpServerConfiguration.AllowDestructiveToolsEnvVar, "true");
            Environment.SetEnvironmentVariable(McpServerConfiguration.AllowScreenshotsEnvVar, "true");
            Environment.SetEnvironmentVariable(McpServerConfiguration.AllowSensitiveReadsEnvVar, "true");
            Environment.SetEnvironmentVariable(McpServerConfiguration.AllowViewModelInspectionEnvVar, "false");

            var policy = McpToolExecutionPolicy.FromEnvironment();

            policy.EvaluateToolCall("get_ui_summary").IsAllowed.Should().BeTrue();
            policy.EvaluateToolCall("get_viewmodel").PolicyCategory.Should().Be("viewmodel-inspection");
        }
        finally
        {
            foreach (var (variable, originalValue) in originalValues)
            {
                Environment.SetEnvironmentVariable(variable, originalValue);
            }
        }
    }

    [Fact]
    public void EvaluateToolCall_WhenBatchMutateRequestsStringifiedSnapshotAndSensitiveReadsAreDisabled_ShouldDeny()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "true",
            allowViewModelInspection: "true");
        using var document = JsonDocument.Parse("{\"captureSnapshot\":\"{\\\"propertyNames\\\":[\\\"Text\\\"]}\"}");
        var arguments = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        var decision = policy.EvaluateToolCall("batch_mutate", arguments);

        decision.IsAllowed.Should().BeFalse();
        decision.PolicyCategory.Should().Be("sensitive-reads");
    }

    [Theory]
    [InlineData("{\"mutations\":[{\"tool\":\"set_dp_value\",\"args\":{\"propertyName\":\"Text\",\"value\":\"redacted\"}}]}")]
    [InlineData("{\"mutations\":[{\"tool\":\"clear_dp_value\",\"args\":{\"propertyName\":\"Text\"}}]}")]
    [InlineData("{\"mutations\":[{\"tool\":\"override_style_setter\",\"args\":{\"propertyName\":\"Foreground\",\"value\":\"Red\"}}]}")]
    [InlineData("{\"mutations\":\"[{\\\"tool\\\":\\\"set_dp_value\\\",\\\"args\\\":{\\\"propertyName\\\":\\\"Text\\\",\\\"value\\\":\\\"redacted\\\"}}]\"}")]
    public void EvaluateToolCall_WhenBatchMutationCanReturnPreviousRuntimeValuesAndSensitiveReadsAreDisabled_ShouldDeny(string argumentsJson)
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "true",
            allowViewModelInspection: "true");
        using var document = JsonDocument.Parse(argumentsJson);
        var arguments = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        var decision = policy.EvaluateToolCall("batch_mutate", arguments);

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("SecurityError");
        decision.PolicyCategory.Should().Be("sensitive-reads");
    }

    [Fact]
    public void EvaluateToolCall_WhenGateValueIsInvalid_ShouldFailClosedForThatCategory()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "sometimes",
            allowScreenshots: null,
            allowViewModelInspection: null);

        var decision = policy.EvaluateToolCall("click_element");

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("InvalidPolicyConfiguration");
        decision.Hint.Should().Contain("true or false");
    }

    [Theory]
    [InlineData("batch_mutate", "{\"mutations\":[{\"tool\":\"modify_viewmodel\",\"args\":{\"propertyName\":\"Name\",\"value\":\"Alice\"}}]}")]
    [InlineData("batch_mutate", "{\"mutations\":[{\"tool\":\"execute_command\",\"args\":{\"commandName\":\"Save\"}}]}")]
    [InlineData("batch_mutate", "{\"mutations\":\"[{\\\"tool\\\":\\\"modify_viewmodel\\\",\\\"args\\\":{\\\"propertyName\\\":\\\"Name\\\",\\\"value\\\":\\\"Alice\\\"}}]\"}")]
    [InlineData("batch_mutate", "{\"mutations\":\"[{\\\"tool\\\":\\\"execute_command\\\",\\\"args\\\":{\\\"commandName\\\":\\\"Save\\\"}}]\"}")]
    [InlineData("batch_mutate", "{\"captureSnapshot\":\"{\\\"viewModelPropertyNames\\\":[\\\"Name\\\"]}\"}")]
    [InlineData("wait_for_dp_change_after_mutation", "{\"triggerMutation\":{\"tool\":\"modify_viewmodel\",\"args\":{\"propertyName\":\"Name\",\"value\":\"Alice\"}}}")]
    [InlineData("wait_for_dp_change_after_mutation", "{\"triggerMutation\":{\"tool\":\"execute_command\",\"args\":{\"commandName\":\"Refresh\"}}}")]
    [InlineData("wait_for_dp_change_after_mutation", "{\"triggerMutation\":\"{\\\"tool\\\":\\\"modify_viewmodel\\\",\\\"args\\\":{\\\"propertyName\\\":\\\"Name\\\",\\\"value\\\":\\\"Alice\\\"}}\"}")]
    [InlineData("wait_for_dp_change_after_mutation", "{\"triggerMutation\":\"{\\\"tool\\\":\\\"execute_command\\\",\\\"args\\\":{\\\"commandName\\\":\\\"Refresh\\\"}}\"}")]
    [InlineData("capture_state_snapshot", "{\"viewModelPropertyNames\":[\"Name\"]}")]
    [InlineData("capture_state_snapshot", "{\"viewModelPropertyNames\":\"[\\\"Name\\\"]\"}")]
    public void EvaluateToolCall_WhenNestedViewModelAccessIsDisabled_ShouldDeny(string toolName, string argumentsJson)
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: null,
            allowViewModelInspection: "false");

        using var document = JsonDocument.Parse(argumentsJson);
        var arguments = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        var decision = policy.EvaluateToolCall(toolName, arguments);

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("SecurityError");
        decision.PolicyCategory.Should().Be("viewmodel-inspection");
    }

    [Fact]
    public void EvaluateToolCall_WhenNestedExecuteCommandAllowsViewModelButDestructiveToolsAreDisabled_ShouldDenyDestructiveGate()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "false",
            allowScreenshots: null,
            allowViewModelInspection: "true");

        using var document = JsonDocument.Parse("{\"mutations\":[{\"tool\":\"execute_command\",\"args\":{\"commandName\":\"Save\"}}]}");
        var arguments = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        var decision = policy.EvaluateToolCall("batch_mutate", arguments);

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("SecurityError");
        decision.PolicyCategory.Should().Be("destructive-tools");
    }

    [Fact]
    public void DestructivePolicy_ShouldCoverDestructiveMcpMetadataExceptSessionLifecycleTools()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "false",
            allowScreenshots: "true",
            allowViewModelInspection: "true");

        var destructiveTools = GetExplicitDestructiveMcpToolNames();

        destructiveTools.Should().Contain(DestructivePolicyExceptions,
            "session lifecycle tools are intentionally governed by target/session policy instead of the destructive mutation gate");

        foreach (var toolName in destructiveTools.Except(DestructivePolicyExceptions, StringComparer.Ordinal))
        {
            var decision = policy.EvaluateToolCall(toolName);

            decision.IsAllowed.Should().BeFalse($"{toolName} is marked Destructive=true and should be gated");
            decision.PolicyCategory.Should().Be("destructive-tools");
        }
    }

    [Theory]
    [InlineData("screenshot", "screenshots")]
    [InlineData("sensitive-read", "sensitive-reads")]
    [InlineData("viewmodel", "viewmodel-inspection")]
    public void PolicyGates_ShouldCoverCanonicalCapabilityTags(string capabilityTag, string expectedPolicyCategory)
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "false",
            allowViewModelInspection: "false",
            allowSensitiveReads: "false");
        var taggedTools = GetManifestToolNamesWithTag(capabilityTag);

        taggedTools.Should().NotBeEmpty($"{capabilityTag} should be represented in the canonical manifest");

        foreach (var toolName in taggedTools)
        {
            var decision = policy.EvaluateToolCall(toolName);

            decision.IsAllowed.Should().BeFalse(
                $"{toolName} is tagged {capabilityTag} in the canonical manifest and should be policy gated");
            decision.PolicyCategory.Should().Be(expectedPolicyCategory);
        }
    }

    private static IReadOnlyCollection<string> GetExplicitDestructiveMcpToolNames()
        => typeof(ServerInstructions).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => new
            {
                Attribute = method.GetCustomAttribute<McpServerToolAttribute>(),
                AttributeData = method.GetCustomAttributesData()
                    .FirstOrDefault(attribute => attribute.AttributeType == typeof(McpServerToolAttribute))
            })
            .Where(tool => tool.Attribute != null && IsExplicitlyDestructive(tool.AttributeData))
            .Select(tool => tool.Attribute!.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();

    private static bool IsExplicitlyDestructive(CustomAttributeData? attributeData)
        => attributeData?.NamedArguments.Any(argument =>
            string.Equals(argument.MemberName, "Destructive", StringComparison.Ordinal)
            && argument.TypedValue.Value is true) == true;

    private static string[] GetManifestToolNamesWithTag(string capabilityTag)
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());
        return document.RootElement.GetProperty("tools")
            .EnumerateArray()
            .Where(tool => tool.GetProperty("capabilityTags").EnumerateArray()
                .Any(tag => string.Equals(tag.GetString(), capabilityTag, StringComparison.Ordinal)))
            .Select(tool => tool.GetProperty("name").GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
    }
}
