using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolInteractiveAccessPolicyTests
{
    [Fact]
    public void EvaluateToolCall_WhenGateIsUnsetAndMatchingGrantExists_ShouldAllow()
    {
        SessionAccessRequest? observed = null;
        var policy = CreatePolicy(
            request =>
            {
                observed = request;
                return true;
            });
        var arguments = Arguments("""{"processId":123}""");

        var decision = policy.EvaluateToolCall("element_screenshot", arguments);

        decision.IsAllowed.Should().BeTrue();
        observed.Should().NotBeNull();
        observed!.Capabilities.Should().Equal(SessionAccessCapabilities.Screenshot);
        observed.ProcessId.Should().Be(123);
    }

    [Fact]
    public void EvaluateToolCall_WhenGateIsExplicitlyDisabled_ShouldNotAllowMatchingGrant()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: null,
            allowScreenshots: "false",
            allowViewModelInspection: null,
            sessionGrantChecker: _ => true);

        var decision = policy.EvaluateToolCall("element_screenshot");

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("SecurityError");
    }

    [Fact]
    public void EvaluateToolCall_WhenGateIsUnsetAndGrantIsMissing_ShouldRecommendExactRequest()
    {
        var policy = CreatePolicy(_ => false);
        var arguments = Arguments("""{"processId":123}""");

        var decision = policy.EvaluateToolCall("element_screenshot", arguments);

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("InteractiveConsentRequired");
        decision.SuggestedAction.Should().Contain("request_session_access");
        decision.SuggestedAction.Should().Contain("screenshot");
        decision.SuggestedAction.Should().Contain("123");
    }

    [Fact]
    public void EvaluateToolCall_WhenGrantedScopeDoesNotMatch_ShouldRemainDenied()
    {
        var policy = CreatePolicy(request => request.ProcessId == 456);

        var decision = policy.EvaluateToolCall(
            "get_ui_summary",
            Arguments("""{"processId":123}"""));

        decision.ErrorCode.Should().Be("InteractiveConsentRequired");
    }

    [Theory]
    [InlineData("preview_ui_blueprint", "composer-preview")]
    [InlineData("apply_ui_blueprint", "project-write")]
    [InlineData("click_element", "runtime-mutation")]
    public void EvaluateToolCall_ShouldMapMutatingWorkflowsToNarrowCapability(
        string toolName,
        string expectedCapability)
    {
        SessionAccessRequest? observed = null;
        var policy = CreatePolicy(request =>
        {
            observed = request;
            return true;
        });
        var arguments = toolName == "apply_ui_blueprint"
            ? Arguments("""{"dryRun":false,"projectRoot":"G:\\apps\\sample"}""")
            : null;

        policy.EvaluateToolCall(toolName, arguments);

        observed!.Capabilities.Should().Equal(expectedCapability);
        observed.ProjectRoot.Should().Be(
            toolName == "apply_ui_blueprint" ? @"G:\apps\sample" : null);
    }

    private static McpToolExecutionPolicy CreatePolicy(Func<SessionAccessRequest, bool> checker)
        => McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: null,
            allowScreenshots: null,
            allowViewModelInspection: null,
            allowSensitiveReads: null,
            allowComposerRuntimeApprovals: null,
            sessionGrantChecker: checker);

    private static Dictionary<string, JsonElement> Arguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}
