using System.Text.Json;
using System.Text;
using System.Windows;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

[Collection("TimingSensitive")]
public sealed class TreeHandlersSerializeToXamlTests
{
    [StaFact]
    public async Task SerializeToXaml_WhenElementIsMissing_ShouldReturnStructuredError()
    {
        var handler = new TreeHandlers(
            new VisualTreeAnalyzer(new ElementFinder()),
            new LogicalTreeAnalyzer(new ElementFinder()),
            new XamlSerializer(),
            new ElementFinder());
        var parameters = JsonDocument.Parse("{\"elementId\":\"missing-element\"}").RootElement;

        var result = await handler.HandleAsync("serialize_to_xaml", parameters, CancellationToken.None);
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("ElementNotFound");
        json.GetProperty("hint").GetString().Should().Contain("elementId");
    }

    [Fact]
    public async Task SerializeToXaml_WhenRequestCancelsWhileDispatcherIsBlocked_ShouldAbortBeforeDispatcherUnblocks()
    {
        using var finder = new ElementFinder();
        Dispatcher? dispatcher = null;
        string? elementId = null;
        var watchSerializePost = 0;
        using var dispatcherReady = new ManualResetEventSlim(false);
        using var blockingStarted = new ManualResetEventSlim(false);
        using var releaseBlock = new ManualResetEventSlim(false);
        using var serializeOperationPosted = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.Hooks.OperationPosted += (_, args) =>
            {
                if (Volatile.Read(ref watchSerializePost) != 0 &&
                    args.Operation.Priority == DispatcherPriority.Normal)
                {
                    serializeOperationPosted.Set();
                }
            };
            var button = new Button { Content = "Blocked" };
            elementId = finder.GenerateElementId(button);
            dispatcher.BeginInvoke(new Action(() =>
            {
                blockingStarted.Set();
                releaseBlock.Wait();
            }), DispatcherPriority.Send);

            dispatcherReady.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        dispatcherReady.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        blockingStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        elementId.Should().NotBeNull();

        var handler = CreateHandler(
            finder,
            dispatcherTimeout: TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        Task<object>? serializeTask = null;
        var completedBeforeRelease = false;

        try
        {
            Volatile.Write(ref watchSerializePost, 1);
            serializeTask = Task.Run(() => handler.HandleAsync(
                "serialize_to_xaml",
                ToJsonElement(new { elementId }),
                cancellation.Token));
            serializeOperationPosted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            cancellation.Cancel();

            completedBeforeRelease = await Task.WhenAny(
                serializeTask,
                Task.Delay(TimeSpan.FromSeconds(1))) == serializeTask;
        }
        finally
        {
            releaseBlock.Set();
            dispatcher!.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }

        completedBeforeRelease.Should().BeTrue(
            "serialize_to_xaml must observe request cancellation instead of waiting for the WPF dispatcher to unblock");
        var act = async () => await serializeTask!;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SerializeToXaml_WhenDispatcherTimeoutElapses_ShouldFailBeforeDispatcherUnblocks()
    {
        using var finder = new ElementFinder();
        Dispatcher? dispatcher = null;
        string? elementId = null;
        using var dispatcherReady = new ManualResetEventSlim(false);
        using var blockingStarted = new ManualResetEventSlim(false);
        using var releaseBlock = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            var button = new Button { Content = "Blocked" };
            elementId = finder.GenerateElementId(button);
            dispatcher.BeginInvoke(new Action(() =>
            {
                blockingStarted.Set();
                releaseBlock.Wait();
            }), DispatcherPriority.Send);

            dispatcherReady.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        dispatcherReady.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        blockingStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        elementId.Should().NotBeNull();

        var handler = CreateHandler(
            finder,
            dispatcherTimeout: TimeSpan.FromMilliseconds(100));
        Task<object>? serializeTask = null;
        var completedBeforeRelease = false;

        try
        {
            serializeTask = Task.Run(() => handler.HandleAsync(
                "serialize_to_xaml",
                ToJsonElement(new { elementId }),
                CancellationToken.None));

            completedBeforeRelease = await Task.WhenAny(
                serializeTask,
                Task.Delay(TimeSpan.FromSeconds(1))) == serializeTask;
        }
        finally
        {
            releaseBlock.Set();
            dispatcher!.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }

        completedBeforeRelease.Should().BeTrue(
            "serialize_to_xaml should use a bounded dispatcher wait instead of blocking until the UI thread unblocks");
        var act = async () => await serializeTask!;
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [StaFact]
    public async Task SerializeToXaml_WhenSerializedXamlExceedsBudget_ShouldReturnStructuredPayloadTooLarge()
    {
        using var finder = new ElementFinder();
        var textBlock = new TextBlock { Text = new string('A', 2048) };
        var elementId = finder.GenerateElementId(textBlock);
        var handler = CreateHandler(
            finder,
            maxSerializedXamlCharacters: 256,
            maxSerializedXamlUtf8Bytes: 512);

        var result = await handler.HandleAsync(
            "serialize_to_xaml",
            ToJsonElement(new { elementId }),
            CancellationToken.None);
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("PayloadTooLarge");
        json.GetProperty("hint").GetString().Should().Contain("smaller");
        json.GetProperty("errorData").GetProperty("maxCharacterCount").GetInt32().Should().Be(256);
        json.GetProperty("errorData").GetProperty("maxByteLength").GetInt32().Should().Be(512);
    }

    [StaFact]
    public async Task SerializeToXaml_WhenJsonEncodedSuccessPayloadExceedsBudget_ShouldReturnPayloadTooLarge()
    {
        using var finder = new ElementFinder();
        var textBlock = new TextBlock { Text = new string('<', 256) };
        var xaml = new XamlSerializer().SerializeToXaml(textBlock);
        var rawByteLength = Encoding.UTF8.GetByteCount(xaml);
        var encodedPayloadLength = JsonSerializer.SerializeToUtf8Bytes(new
        {
            success = true,
            xaml
        }).Length;
        encodedPayloadLength.Should().BeGreaterThan(rawByteLength);
        var byteBudget = (rawByteLength + encodedPayloadLength) / 2;
        byteBudget.Should().BeGreaterThan(rawByteLength);
        byteBudget.Should().BeLessThan(encodedPayloadLength);

        var elementId = finder.GenerateElementId(textBlock);
        var handler = CreateHandler(
            finder,
            maxSerializedXamlCharacters: xaml.Length + 128,
            maxSerializedXamlUtf8Bytes: byteBudget);

        var result = await handler.HandleAsync(
            "serialize_to_xaml",
            ToJsonElement(new { elementId }),
            CancellationToken.None);
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("PayloadTooLarge");
        json.GetProperty("errorData").GetProperty("byteLength").GetInt32().Should()
            .BeGreaterThan(byteBudget);
        json.GetProperty("errorData").GetProperty("maxByteLength").GetInt32().Should().Be(byteBudget);
    }

    [StaFact]
    public async Task SerializeToXaml_WhenContentCannotRoundTripThroughXamlWriter_ShouldReturnSafeSnapshot()
    {
        using var finder = new ElementFinder();
        var button = new Button { Content = new PayloadWithoutDefaultConstructor("probe") };
        var elementId = finder.GenerateElementId(button);
        var handler = CreateHandler(finder);

        var result = await handler.HandleAsync(
            "serialize_to_xaml",
            ToJsonElement(new { elementId }),
            CancellationToken.None);
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeTrue();
        var xaml = json.GetProperty("xaml").GetString();
        xaml.Should().Contain("<Button");
        xaml.Should().Contain("Content=\"PayloadWithoutDefaultConstructor\"");
    }

    [StaFact]
    public async Task SerializeToXaml_WhenTargetIsWindow_ShouldReturnRecoverableInvalidArgument()
    {
        using var finder = new ElementFinder();
        var window = new Window
        {
            Content = new Grid()
        };
        var elementId = finder.GenerateElementId(window);
        var handler = CreateHandler(finder);

        var result = await handler.HandleAsync(
            "serialize_to_xaml",
            ToJsonElement(new { elementId }),
            CancellationToken.None);
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("hint").GetString().Should().Contain("smaller descendant");
        json.GetProperty("errorData").GetProperty("elementId").GetString().Should().Be(elementId);
        json.GetProperty("errorData").GetProperty("elementType").GetString().Should().Be("Window");
        json.GetProperty("errorData").GetProperty("reasonCode").GetString()
            .Should().Be("RootWindowSerializationBlocked");
    }

    private static TreeHandlers CreateHandler(
        ElementFinder finder,
        TimeSpan? dispatcherTimeout = null,
        int? maxSerializedXamlCharacters = null,
        int? maxSerializedXamlUtf8Bytes = null)
    {
        return new TreeHandlers(
            new VisualTreeAnalyzer(finder),
            new LogicalTreeAnalyzer(finder),
            new XamlSerializer(),
            finder,
            dispatcherTimeout,
            maxSerializedXamlCharacters,
            maxSerializedXamlUtf8Bytes);
    }

    private sealed class PayloadWithoutDefaultConstructor
    {
        public PayloadWithoutDefaultConstructor(string value)
        {
            Value = value;
        }

        public string Value { get; }
    }
}
