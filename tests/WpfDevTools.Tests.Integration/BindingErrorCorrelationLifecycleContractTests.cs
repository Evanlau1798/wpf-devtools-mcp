using System.Diagnostics;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfAndBootstrapIntegration")]
public sealed class BindingErrorCorrelationLifecycleContractTests
{
    private readonly WpfApplicationFixture _fixture;

    public BindingErrorCorrelationLifecycleContractTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void BindingErrorCorrelationIntegrationTests_ShouldRestoreBindingTraceStateOnDispose()
    {
        var originalLevel = _fixture.RunOnUIThread(() => PresentationTraceSources.DataBindingSource.Switch.Level);
        try
        {
            _fixture.RunOnUIThread(() => PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning);
            var test = new BindingErrorCorrelationIntegrationTests(_fixture);
            test.GetBindingErrors_ShouldProvideActionableElementCorrelation();

            var installedListener = _fixture.RunOnUIThread(() => BindingErrorTraceListener.Instance);
            _fixture.RunOnUIThread(() => PresentationTraceSources.DataBindingSource.Switch.Level)
                .Should().Be(SourceLevels.Error);
            _fixture.RunOnUIThread(() => IsListenerRegistered(installedListener)).Should().BeTrue();

            test.Dispose();

            _fixture.RunOnUIThread(() => PresentationTraceSources.DataBindingSource.Switch.Level)
                .Should().Be(SourceLevels.Warning);
            _fixture.RunOnUIThread(() => IsListenerRegistered(installedListener)).Should().BeFalse();
            _fixture.RunOnUIThread(HasAnyBindingTraceListener).Should().BeFalse();
        }
        finally
        {
            BindingErrorTraceListener.ResetInstance();
            _fixture.RunOnUIThread(() => PresentationTraceSources.DataBindingSource.Switch.Level = originalLevel);
        }
    }

    private static bool IsListenerRegistered(BindingErrorTraceListener listener)
        => PresentationTraceSources.DataBindingSource.Listeners
            .Cast<TraceListener>()
            .Any(current => ReferenceEquals(current, listener));

    private static bool HasAnyBindingTraceListener()
        => PresentationTraceSources.DataBindingSource.Listeners
            .Cast<TraceListener>()
            .Any(listener => listener is BindingErrorTraceListener);
}
