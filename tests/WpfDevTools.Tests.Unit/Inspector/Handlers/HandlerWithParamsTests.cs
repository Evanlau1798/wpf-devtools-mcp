using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

/// <summary>
/// Tests that exercise handler methods with valid parameters so that execution
/// reaches the Task.Run lambdas and invokes the underlying analyzers.
/// Dispatcher-dependent analyzers should return structured unavailable payloads
/// instead of executing WPF object access without a usable UI dispatcher.
/// </summary>
public class HandlerWithParamsTests
{
    // ---- DependencyPropertyHandlers ----

    [Fact]
    public async Task DependencyPropertyHandlers_GetDpValueSource_WithPropertyName_ShouldReturnResult()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { propertyName = "Width" });

        var result = await handler.HandleAsync("get_dp_value_source", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DependencyPropertyHandlers_GetDpValueSource_WithElementIdAndPropertyName_ShouldReturnResult()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", propertyName = "Width" });

        var result = await handler.HandleAsync("get_dp_value_source", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DependencyPropertyHandlers_GetDpMetadata_WithPropertyName_ShouldReturnResult()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { propertyName = "Height" });

        var result = await handler.HandleAsync("get_dp_metadata", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DependencyPropertyHandlers_GetDpMetadata_WithElementIdAndPropertyName_ShouldReturnResult()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", propertyName = "Height" });

        var result = await handler.HandleAsync("get_dp_metadata", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DependencyPropertyHandlers_SetDpValue_WithPropertyNameAndValue_ShouldReturnResult()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { propertyName = "Width", value = 100.0 });

        var result = await handler.HandleAsync("set_dp_value", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DependencyPropertyHandlers_SetDpValue_WithElementIdPropertyNameAndValue_ShouldReturnResult()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", propertyName = "Width", value = 200.0 });

        var result = await handler.HandleAsync("set_dp_value", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DependencyPropertyHandlers_ClearDpValue_WithPropertyName_ShouldReturnResult()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { propertyName = "Width" });

        var result = await handler.HandleAsync("clear_dp_value", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DependencyPropertyHandlers_ClearDpValue_WithElementIdAndPropertyName_ShouldReturnResult()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", propertyName = "Height" });

        var result = await handler.HandleAsync("clear_dp_value", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DependencyPropertyHandlers_WatchDpChanges_WithPropertyName_ShouldReturnResult()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { propertyName = "Visibility" });

        var result = await handler.HandleAsync("watch_dp_changes", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DependencyPropertyHandlers_WatchDpChanges_WithElementIdAndPropertyName_ShouldReturnResult()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", propertyName = "Visibility" });

        var result = await handler.HandleAsync("watch_dp_changes", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DependencyPropertyHandlers_WaitForDpChange_WithPropertyNameAndTimeout_ShouldReturnResult()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { propertyName = "Visibility", timeoutMs = 100, pollIntervalMs = 50 });

        var result = await handler.HandleAsync("wait_for_dp_change", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    // ---- MvvmHandlers ----

    [Fact]
    public async Task MvvmHandlers_ExecuteCommand_WithCommandName_ShouldReturnResult()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { commandName = "SaveCommand" });

        var result = await handler.HandleAsync("execute_command", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task MvvmHandlers_ExecuteCommand_WithElementIdAndCommandName_ShouldReturnResult()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", commandName = "LoadCommand" });

        var result = await handler.HandleAsync("execute_command", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task MvvmHandlers_ExecuteCommand_WithCommandNameAndParameter_ShouldReturnResult()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { commandName = "DeleteCommand", parameter = "item1" });

        var result = await handler.HandleAsync("execute_command", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task MvvmHandlers_ModifyViewModel_WithPropertyNameAndValue_ShouldReturnResult()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { propertyName = "Title", value = "NewTitle" });

        var result = await handler.HandleAsync("modify_viewmodel", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task MvvmHandlers_ModifyViewModel_WithElementIdPropertyNameAndValue_ShouldReturnResult()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", propertyName = "IsEnabled", value = true });

        var result = await handler.HandleAsync("modify_viewmodel", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task MvvmHandlers_ModifyViewModel_WithJsonNullValue_ShouldForwardValue()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { propertyName = "Title", value = (string?)null });

        var result = await handler.HandleAsync("modify_viewmodel", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    // ---- StyleHandlers ----

    [Fact]
    public async Task StyleHandlers_OverrideStyleSetter_WithPropertyNameAndValue_ShouldReturnResult()
    {
        var handler = new StyleHandlers(new StyleAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { propertyName = "Background", value = "Red" });

        var result = await handler.HandleAsync("override_style_setter", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task StyleHandlers_OverrideStyleSetter_WithElementIdPropertyNameAndValue_ShouldReturnResult()
    {
        var handler = new StyleHandlers(new StyleAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", propertyName = "Foreground", value = "Blue" });

        var result = await handler.HandleAsync("override_style_setter", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task StyleHandlers_GetResourceChain_WithResourceKeyAndElementId_ShouldReturnResult()
    {
        var handler = new StyleHandlers(new StyleAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", resourceKey = "PrimaryButtonStyle" });

        var result = await handler.HandleAsync("get_resource_chain", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    // ---- EventHandlers ----

    [Fact]
    public async Task EventHandlers_TraceRoutedEvents_WithEventNameAndElementId_ShouldReturnResult()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", eventName = "Click" });

        var result = await handler.HandleAsync("trace_routed_events", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task EventHandlers_TraceRoutedEvents_WithEventNameAndDuration_ShouldReturnResult()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { eventName = "MouseMove", duration = 500 });

        var result = await handler.HandleAsync("trace_routed_events", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [StaFact]
    public async Task EventHandlers_TraceRoutedEvents_WithValidElement_ShouldReturnTracePayloadShape()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);
        var @params = JsonSerializer.SerializeToElement(new { elementId, eventName = "Click", duration = 25 });

        var result = await handler.HandleAsync("trace_routed_events", @params, CancellationToken.None);

        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.TryGetProperty("eventCount", out _).Should().BeTrue();
        doc.TryGetProperty("events", out _).Should().BeTrue();
    }

    [Fact]
    public async Task EventHandlers_GetEventHandlers_WithEventNameAndElementId_ShouldReturnResult()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", eventName = "Click" });

        var result = await handler.HandleAsync("get_event_handlers", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task EventHandlers_FireRoutedEvent_WithEventNameAndEventArgs_ShouldReturnResult()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { eventName = "Click", eventArgs = (object?)null });

        var result = await handler.HandleAsync("fire_routed_event", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task EventHandlers_FireRoutedEvent_WithElementIdAndEventName_ShouldReturnResult()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", eventName = "MouseDown" });

        var result = await handler.HandleAsync("fire_routed_event", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    // ---- InteractionHandlers ----

    [Fact]
    public async Task InteractionHandlers_SimulateKeyboard_WithKeyAndElementId_ShouldReturnResult()
    {
        var handler = new InteractionHandlers(new InteractionAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", key = "Enter" });

        var result = await handler.HandleAsync("simulate_keyboard", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task InteractionHandlers_SimulateKeyboard_WithKeyAndEventType_ShouldReturnResult()
    {
        var handler = new InteractionHandlers(new InteractionAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { key = "Tab", eventType = "KeyUp" });

        var result = await handler.HandleAsync("simulate_keyboard", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task InteractionHandlers_SimulateKeyboard_WithKeyAndKeyDownEventType_ShouldReturnResult()
    {
        var handler = new InteractionHandlers(new InteractionAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { key = "Escape", eventType = "KeyDown" });

        var result = await handler.HandleAsync("simulate_keyboard", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    // ---- BindingHandlers ----

    [Fact]
    public async Task BindingHandlers_GetBindingValueChain_WithPropertyNameAndElementId_ShouldReturnResult()
    {
        var handler = new BindingHandlers(new BindingAnalyzer(new ElementFinder()), new ElementFinder());
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", propertyName = "Text" });

        var result = await handler.HandleAsync("get_binding_value_chain", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task BindingHandlers_ForceBindingUpdate_WithPropertyNameAndElementId_ShouldReturnResult()
    {
        var handler = new BindingHandlers(new BindingAnalyzer(new ElementFinder()), new ElementFinder());
        var @params = JsonSerializer.SerializeToElement(new { elementId = "root", propertyName = "Text" });

        var result = await handler.HandleAsync("force_binding_update", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task BindingHandlers_ForceBindingUpdate_WithPropertyNameAndDirection_ShouldReturnResult()
    {
        var handler = new BindingHandlers(new BindingAnalyzer(new ElementFinder()), new ElementFinder());
        var @params = JsonSerializer.SerializeToElement(new { propertyName = "Content", direction = "Target" });

        var result = await handler.HandleAsync("force_binding_update", @params, CancellationToken.None);

        result.Should().NotBeNull();
    }
}
