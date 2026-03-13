using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class InteractionToolMetadataTests : IDisposable
{
    public void Dispose()
    {
        ToolCallHelper.ResetCacheForTesting();
    }

    [Fact]
    public void ExecuteCommand_ShouldIncludeRequestedInputAndObservedEffectMetadata()
    {
        var result = InteractionMetadataProbe.Apply(
            new
            {
                success = true,
                commandName = "SaveCommand",
                executed = true,
                canExecute = true
            },
            new
            {
                elementId = "SaveButton",
                commandName = "SaveCommand",
                parameter = "Document-1"
            },
            null,
            "Triggers real application logic. Confirm the observedEffect before assuming navigation, save, or side effects completed.");

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("commandName").GetString().Should().Be("SaveCommand");
        json.GetProperty("effectiveInput").GetProperty("parameter").GetString().Should().Be("Document-1");
        json.GetProperty("observedEffect").GetProperty("executed").GetBoolean().Should().BeTrue();
        json.GetProperty("usedFallback").GetBoolean().Should().BeFalse();
        json.GetProperty("notes").GetString().Should().Contain("real application logic");
    }

    [Fact]
    public void ClickElement_ShouldIncludeRequestedInputAndObservedEffectMetadata()
    {
        var result = InteractionMetadataProbe.Apply(
            new
            {
                success = true,
                clicked = true
            },
            new
            {
                elementId = "SaveButton"
            },
            null,
            "Triggers real application logic through the control click pipeline. Verify the observedEffect before continuing the workflow.");

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("elementId").GetString().Should().Be("SaveButton");
        json.GetProperty("observedEffect").GetProperty("clicked").GetBoolean().Should().BeTrue();
        json.GetProperty("usedFallback").GetBoolean().Should().BeFalse();
        json.GetProperty("notes").GetString().Should().Contain("real application logic");
    }

    [Fact]
    public void FireRoutedEvent_ShouldIncludeRequestedInputAndFallbackMetadata()
    {
        var result = InteractionMetadataProbe.Apply(
            new
            {
                success = true,
                eventName = "Click",
                message = "Invoked OnClick path",
                usedOnClick = true
            },
            new
            {
                elementId = "SaveButton",
                eventName = "Click"
            },
            null,
            "Routed-event execution may use the ButtonBase OnClick path when applicable. Inspect usedFallback and observedEffect before assuming the event path used.",
            usedFallback: true);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("eventName").GetString().Should().Be("Click");
        json.GetProperty("observedEffect").GetProperty("usedOnClick").GetBoolean().Should().BeTrue();
        json.GetProperty("usedFallback").GetBoolean().Should().BeTrue();
        json.GetProperty("notes").GetString().Should().Contain("OnClick");
    }

    [Fact]
    public void ModifyViewModel_ShouldIncludeRequestedInputAndObservedEffectMetadata()
    {
        var result = InteractionMetadataProbe.Apply(
            new
            {
                success = true,
                propertyName = "Name",
                oldValue = "Alice",
                newValue = "Bob"
            },
            new
            {
                elementId = "NameTextBox",
                propertyName = "Name",
                value = "Bob"
            },
            null,
            "Runtime-only ViewModel mutation. UI refresh still depends on INotifyPropertyChanged and any binding-side validation.");

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("propertyName").GetString().Should().Be("Name");
        json.GetProperty("observedEffect").GetProperty("newValue").GetString().Should().Be("Bob");
        json.GetProperty("usedFallback").GetBoolean().Should().BeFalse();
        json.GetProperty("notes").GetString().Should().Contain("INotifyPropertyChanged");
    }

    [Fact]
    public void ClickElement_WithDetailCompact_ShouldOmitVerboseMetadataAndKeepCoreFields()
    {
        var result = InteractionMetadataProbe.Apply(
            new
            {
                success = true,
                clicked = true
            },
            new
            {
                elementId = "SaveButton"
            },
            ToJsonElement(new { detail = "compact" }),
            "Triggers real application logic through the control click pipeline. Verify the observedEffect before continuing the workflow.");

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("clicked").GetBoolean().Should().BeTrue();
        json.TryGetProperty("requestedInput", out _).Should().BeFalse();
        json.TryGetProperty("effectiveInput", out _).Should().BeFalse();
        json.TryGetProperty("observedEffect", out _).Should().BeFalse();
        json.TryGetProperty("notes", out _).Should().BeFalse();
        json.TryGetProperty("usedFallback", out _).Should().BeFalse();
    }

    [Fact]
    public void FireRoutedEvent_WithDetailCompact_ShouldKeepSemanticallyRelevantUsedFallback()
    {
        var result = InteractionMetadataProbe.Apply(
            new
            {
                success = true,
                eventName = "Click",
                message = "Invoked OnClick path",
                usedOnClick = true
            },
            new
            {
                elementId = "SaveButton",
                eventName = "Click"
            },
            ToJsonElement(new { detail = "compact" }),
            "Routed-event execution may use the ButtonBase OnClick path when applicable. Inspect usedFallback and observedEffect before assuming the event path used.",
            usedFallback: true);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("usedOnClick").GetBoolean().Should().BeTrue();
        json.GetProperty("usedFallback").GetBoolean().Should().BeTrue();
        json.TryGetProperty("requestedInput", out _).Should().BeFalse();
        json.TryGetProperty("effectiveInput", out _).Should().BeFalse();
        json.TryGetProperty("observedEffect", out _).Should().BeFalse();
        json.TryGetProperty("notes", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ClickElementTool_WithInvalidDetail_ShouldReturnStructuredError()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(51007);
        var tool = new ClickElementTool(sessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId = 51007,
            elementId = "SaveButton",
            detail = "verbose"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("detail");
    }

    [Fact]
    public async Task ClickElement_Navigation_ShouldRecommendUiSummaryVerification()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true, clicked = true }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "SaveButton")),
            CancellationToken.None,
            toolName: "click_element");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_ui_summary");
    }

    [Fact]
    public async Task ExecuteCommand_Navigation_ShouldRecommendUiSummaryAndNotTraceRoutedEvents()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                commandName = "SaveCommand",
                executed = true,
                canExecute = true
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "SaveButton"), ("commandName", "SaveCommand")),
            CancellationToken.None,
            toolName: "execute_command");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_ui_summary");
        nextSteps.EnumerateArray().Select(item => item.GetProperty("tool").GetString()).Should().NotContain("trace_routed_events");
    }

    private sealed class InteractionMetadataProbe : PipeConnectedToolBase
    {
        private InteractionMetadataProbe() : base(new SessionManager())
        {
        }

        public static object Apply(
            object result,
            object requestedInput,
            JsonElement? arguments,
            string notes,
            bool usedFallback = false)
        {
            var (_, error) = ParseMutationDetailMode(arguments);
            if (error != null)
            {
                return error;
            }

            var (mode, _) = ParseMutationDetailMode(arguments);
            return AddSuccessMetadata(result, requestedInput, notes, usedFallback, mode);
        }
    }
}
