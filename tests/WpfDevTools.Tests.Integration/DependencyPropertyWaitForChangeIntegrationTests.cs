using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfAndBootstrapIntegration")]
public sealed class DependencyPropertyWaitForChangeIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public DependencyPropertyWaitForChangeIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WaitForChange_ShouldReturnChanged_WhenPropertyValueChanges()
    {
        ElementFinder? finder = null;
        DependencyPropertyAnalyzer? analyzer = null;
        Button? button = null;
        string? elementId = null;

        _fixture.RunOnUIThread(() =>
        {
            finder = new ElementFinder();
            analyzer = new DependencyPropertyAnalyzer(finder);
            button = new Button { Width = 100 };
            Application.Current.MainWindow.Content = button;
            elementId = finder.GenerateElementId(button);
        });

        using var snapshotRead = CreateBackgroundSnapshotSignal();
        var snapshotBaseline = snapshotRead.CompletedBackgroundOperationCount;
        var waitTask = RunWaitForChangeAsync(
            () => analyzer!.WaitForChange("Width", elementId, timeoutMs: 1000, pollIntervalMs: 50));

        snapshotRead.WaitForBackgroundOperationAfter(snapshotBaseline);
        await QueueUiMutationAsync(() => button!.Width = 200);

        var result = await waitTask;
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("changed").GetBoolean().Should().BeTrue();
        result.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        result.GetProperty("observedChange").GetBoolean().Should().BeTrue();
        result.GetProperty("matchedExpectedValueAtStart").GetBoolean().Should().BeFalse();
        result.GetProperty("completionReason").GetString().Should().Be("ValueChanged");
        result.GetProperty("initialValue").GetString().Should().Be("100");
        result.GetProperty("currentValue").GetString().Should().Be("200");
    }

    [Fact]
    public async Task WaitForChange_ShouldWaitUntilExpectedValueIsReached()
    {
        ElementFinder? finder = null;
        DependencyPropertyAnalyzer? analyzer = null;
        Button? button = null;
        string? elementId = null;

        _fixture.RunOnUIThread(() =>
        {
            finder = new ElementFinder();
            analyzer = new DependencyPropertyAnalyzer(finder);
            button = new Button { Width = 100 };
            Application.Current.MainWindow.Content = button;
            elementId = finder.GenerateElementId(button);
        });

        using var snapshotRead = CreateBackgroundSnapshotSignal();
        var snapshotBaseline = snapshotRead.CompletedBackgroundOperationCount;
        var waitTask = RunWaitForChangeAsync(
            () => analyzer!.WaitForChange(
                "Width",
                elementId,
                timeoutMs: 1200,
                pollIntervalMs: 50,
                expectedValue: JsonSerializer.SerializeToElement(300.0)));

        snapshotRead.WaitForBackgroundOperationAfter(snapshotBaseline);
        await QueueUiMutationAsync(() => button!.Width = 200);
        await QueueUiMutationAsync(() => button!.Width = 300);

        var result = await waitTask;
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("changed").GetBoolean().Should().BeTrue();
        result.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        result.GetProperty("observedChange").GetBoolean().Should().BeTrue();
        result.GetProperty("matchedExpectedValueAtStart").GetBoolean().Should().BeFalse();
        result.GetProperty("completionReason").GetString().Should().Be("ExpectedValueReached");
        result.GetProperty("currentValue").GetString().Should().Be("300");
    }

    [Fact]
    public async Task WaitForChange_ShouldObserveBindingTargetUpdate_AfterViewModelMutation()
    {
        ElementFinder? finder = null;
        DependencyPropertyAnalyzer? analyzer = null;
        TextBox? textBox = null;
        BindingWaitViewModel? viewModel = null;
        string? elementId = null;

        _fixture.RunOnUIThread(() =>
        {
            finder = new ElementFinder();
            analyzer = new DependencyPropertyAnalyzer(finder);
            viewModel = new BindingWaitViewModel();
            textBox = new TextBox();
            textBox.SetBinding(TextBox.TextProperty, new Binding(nameof(BindingWaitViewModel.SearchText))
            {
                Source = viewModel,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            Application.Current.MainWindow.Content = textBox;
            Application.Current.MainWindow.Show();
            Application.Current.MainWindow.Activate();
            Application.Current.MainWindow.UpdateLayout();
            Application.Current.MainWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            elementId = finder.GenerateElementId(textBox);
        });

        var expectedValue = "binding-after";
        using var snapshotRead = CreateBackgroundSnapshotSignal();
        var snapshotBaseline = snapshotRead.CompletedBackgroundOperationCount;
        var waitTask = RunWaitForChangeAsync(
            () => analyzer!.WaitForChange(
                "Text",
                elementId,
                timeoutMs: 1200,
                pollIntervalMs: 50,
                expectedValue: JsonSerializer.SerializeToElement(expectedValue)));

        snapshotRead.WaitForBackgroundOperationAfter(snapshotBaseline);
        await QueueUiMutationAsync(() => viewModel!.SearchText = expectedValue);

        var result = await waitTask;
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("changed").GetBoolean().Should().BeTrue();
        result.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        result.GetProperty("completionReason").GetString().Should().Be("ExpectedValueReached");
        result.GetProperty("currentValue").GetString().Should().Be(expectedValue);
    }

    private sealed class BindingWaitViewModel : INotifyPropertyChanged
    {
        private string _searchText = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchText)));
            }
        }
    }

    private static Task<JsonElement> RunWaitForChangeAsync(Func<object> waitForChange)
    {
        return Task.Run(() => JsonSerializer.SerializeToElement(waitForChange()));
    }

    private BackgroundSnapshotSignal CreateBackgroundSnapshotSignal()
        => _fixture.RunOnUIThread(() => new BackgroundSnapshotSignal(Application.Current.Dispatcher));

    private Task QueueUiMutationAsync(Action mutation)
    {
        return _fixture.RunOnUIThread(() =>
            Application.Current.Dispatcher.InvokeAsync(
                mutation,
                DispatcherPriority.ApplicationIdle).Task);
    }

    private sealed class BackgroundSnapshotSignal : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private int _completedBackgroundOperationCount;

        public BackgroundSnapshotSignal(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _dispatcher.Hooks.OperationCompleted += OnOperationCompleted;
        }

        public int CompletedBackgroundOperationCount =>
            Volatile.Read(ref _completedBackgroundOperationCount);

        public void WaitForBackgroundOperationAfter(int baseline)
        {
            ConditionWaiter.WaitUntil(
                () => CompletedBackgroundOperationCount > baseline,
                TimeSpan.FromSeconds(2),
                "Timed out waiting for the initial DP wait snapshot read.");
        }

        public void Dispose()
        {
            if (_dispatcher.CheckAccess())
            {
                _dispatcher.Hooks.OperationCompleted -= OnOperationCompleted;
                return;
            }

            _dispatcher.Invoke(() => _dispatcher.Hooks.OperationCompleted -= OnOperationCompleted);
        }

        private void OnOperationCompleted(object? sender, DispatcherHookEventArgs e)
        {
            if (e.Operation.Priority == DispatcherPriority.Background)
            {
                Interlocked.Increment(ref _completedBackgroundOperationCount);
            }
        }
    }
}
