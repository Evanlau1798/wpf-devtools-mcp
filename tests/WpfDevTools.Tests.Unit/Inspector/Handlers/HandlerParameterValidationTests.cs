using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public class HandlerParameterValidationTests
{
    // ---- BindingHandlers parameter validation ----

    [Fact]
    public async Task BindingHandlers_GetBindingValueChain_WithoutPropertyName_ShouldThrowArgumentException()
    {
        var handler = new BindingHandlers(new BindingAnalyzer(new ElementFinder()), new ElementFinder());

        var act = () => handler.HandleAsync("get_binding_value_chain", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*propertyName*");
    }

    [Fact]
    public async Task BindingHandlers_ForceBindingUpdate_WithoutPropertyName_ShouldThrowArgumentException()
    {
        var handler = new BindingHandlers(new BindingAnalyzer(new ElementFinder()), new ElementFinder());

        var act = () => handler.HandleAsync("force_binding_update", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*propertyName*");
    }

    [Fact]
    public async Task BindingHandlers_UnsupportedMethod_ShouldThrowInvalidOperationException()
    {
        var handler = new BindingHandlers(new BindingAnalyzer(new ElementFinder()), new ElementFinder());

        var act = () => handler.HandleAsync("unknown_method", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported method*");
    }

    // ---- TreeHandlers unsupported method ----

    [Fact]
    public async Task TreeHandlers_UnsupportedMethod_ShouldThrowInvalidOperationException()
    {
        var handler = new TreeHandlers(
            new VisualTreeAnalyzer(new ElementFinder()),
            new LogicalTreeAnalyzer(new ElementFinder()),
            new XamlSerializer(),
            new ElementFinder());

        var act = () => handler.HandleAsync("unknown_method", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported method*");
    }

    // ---- DependencyPropertyHandlers parameter validation ----

    [Fact]
    public async Task DependencyPropertyHandlers_GetDpValueSource_WithoutPropertyName_ShouldThrowArgumentException()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("get_dp_value_source", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*propertyName*");
    }

    [Fact]
    public async Task DependencyPropertyHandlers_GetDpMetadata_WithoutPropertyName_ShouldThrowArgumentException()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("get_dp_metadata", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*propertyName*");
    }

    [Fact]
    public async Task DependencyPropertyHandlers_SetDpValue_WithoutPropertyName_ShouldThrowArgumentException()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("set_dp_value", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*propertyName*");
    }

    [Fact]
    public async Task DependencyPropertyHandlers_SetDpValue_WithPropertyNameButNoValue_ShouldThrowArgumentException()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { propertyName = "Width" });

        var act = () => handler.HandleAsync("set_dp_value", @params, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*value*");
    }

    [Fact]
    public async Task DependencyPropertyHandlers_SetDpValue_WithExplicitJsonNull_ShouldReachAnalyzer()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new
        {
            elementId = "missing-element",
            propertyName = "Tag",
            value = (string?)null
        });

        var result = JsonSerializer.SerializeToElement(await handler.HandleAsync(
            "set_dp_value",
            @params,
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("ElementNotFound");
    }

    [Fact]
    public async Task DependencyPropertyHandlers_ClearDpValue_WithoutPropertyName_ShouldThrowArgumentException()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("clear_dp_value", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*propertyName*");
    }

    [Fact]
    public async Task DependencyPropertyHandlers_WatchDpChanges_WithoutPropertyName_ShouldThrowArgumentException()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("watch_dp_changes", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*propertyName*");
    }

    [Fact]
    public async Task DependencyPropertyHandlers_WaitForDpChange_WithoutPropertyName_ShouldThrowArgumentException()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("wait_for_dp_change", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*propertyName*");
    }

    [Fact]
    public async Task DependencyPropertyHandlers_UnsupportedMethod_ShouldThrowInvalidOperationException()
    {
        var handler = new DependencyPropertyHandlers(new DependencyPropertyAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("unknown_method", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported method*");
    }

    // ---- MvvmHandlers parameter validation ----

    [Fact]
    public async Task MvvmHandlers_ExecuteCommand_WithoutCommandName_ShouldThrowArgumentException()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("execute_command", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*commandName*");
    }

    [Fact]
    public async Task MvvmHandlers_ModifyViewModel_WithoutPropertyName_ShouldThrowArgumentException()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("modify_viewmodel", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*propertyName*");
    }

    [Fact]
    public async Task MvvmHandlers_ModifyViewModel_WithPropertyNameButNoValue_ShouldThrowArgumentException()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { propertyName = "Title" });

        var act = () => handler.HandleAsync("modify_viewmodel", @params, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*value*");
    }

    [Fact]
    public async Task MvvmHandlers_UnsupportedMethod_ShouldThrowInvalidOperationException()
    {
        var handler = new MvvmHandlers(new MvvmAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("unknown_method", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported method*");
    }

    // ---- LayoutHandlers unsupported method ----

    [Fact]
    public async Task LayoutHandlers_UnsupportedMethod_ShouldThrowInvalidOperationException()
    {
        var handler = new LayoutHandlers(new LayoutAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("unknown_method", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported method*");
    }

    // ---- StyleHandlers parameter validation ----

    [Fact]
    public async Task StyleHandlers_GetResourceChain_WithoutResourceKey_ShouldThrowArgumentException()
    {
        var handler = new StyleHandlers(new StyleAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("get_resource_chain", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*resourceKey*");
    }

    [Fact]
    public async Task StyleHandlers_OverrideStyleSetter_WithoutPropertyName_ShouldThrowArgumentException()
    {
        var handler = new StyleHandlers(new StyleAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("override_style_setter", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*propertyName*");
    }

    [Fact]
    public async Task StyleHandlers_OverrideStyleSetter_WithPropertyNameButNoValue_ShouldThrowArgumentException()
    {
        var handler = new StyleHandlers(new StyleAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { propertyName = "Background" });

        var act = () => handler.HandleAsync("override_style_setter", @params, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*value*");
    }

    [Fact]
    public async Task StyleHandlers_UnsupportedMethod_ShouldThrowInvalidOperationException()
    {
        var handler = new StyleHandlers(new StyleAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("unknown_method", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported method*");
    }

    // ---- EventHandlers parameter validation ----

    [Fact]
    public async Task EventHandlers_TraceRoutedEvents_WithoutEventName_ShouldThrowArgumentException()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("trace_routed_events", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*eventName*");
    }

    [Fact]
    public async Task EventHandlers_GetEventHandlers_WithoutEventName_ShouldThrowArgumentException()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("get_event_handlers", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*eventName*");
    }

    [Fact]
    public async Task EventHandlers_FireRoutedEvent_WithoutEventName_ShouldThrowArgumentException()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("fire_routed_event", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*eventName*");
    }

    [Fact]
    public async Task EventHandlers_UnsupportedMethod_ShouldThrowInvalidOperationException()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("unknown_method", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported method*");
    }

    // ---- InteractionHandlers parameter validation ----

    [Fact]
    public async Task InteractionHandlers_SimulateKeyboard_WithoutKey_ShouldThrowArgumentException()
    {
        var handler = new InteractionHandlers(new InteractionAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("simulate_keyboard", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*key*");
    }

    [Fact]
    public async Task InteractionHandlers_UnsupportedMethod_ShouldThrowInvalidOperationException()
    {
        var handler = new InteractionHandlers(new InteractionAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("unknown_method", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported method*");
    }

    // ---- PerformanceHandlers unsupported method ----

    [Fact]
    public async Task PerformanceHandlers_UnsupportedMethod_ShouldThrowInvalidOperationException()
    {
        var handler = new PerformanceHandlers(new PerformanceAnalyzer(new ElementFinder()));

        var act = () => handler.HandleAsync("unknown_method", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported method*");
    }

}
