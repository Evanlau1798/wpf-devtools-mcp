using System.Diagnostics;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("BindingErrorTests")]
public sealed class BindingErrorTraceListenerLifecycleTests : IDisposable
{
    private readonly SourceLevels _originalLevel;

    public BindingErrorTraceListenerLifecycleTests()
    {
        _originalLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        RemoveAllInstanceRegistrations();
        BindingErrorTraceListener.ResetInstance();
        RemoveAllInstanceRegistrations();
    }

    public void Dispose()
    {
        RemoveAllInstanceRegistrations();
        BindingErrorTraceListener.ResetInstance();
        RemoveAllInstanceRegistrations();
        PresentationTraceSources.DataBindingSource.Switch.Level = _originalLevel;
    }

    [Fact]
    public void Install_WhenDuplicateRegistrationExists_ShouldLeaveSingleRegistration()
    {
        var source = PresentationTraceSources.DataBindingSource;
        source.Listeners.Add(BindingErrorTraceListener.Instance);
        source.Listeners.Add(BindingErrorTraceListener.Instance);

        BindingErrorTraceListener.Install();

        CountInstanceRegistrations().Should().Be(1);
    }

    [Fact]
    public void Uninstall_WhenDuplicateRegistrationExists_ShouldRemoveAllRegistrations()
    {
        var source = PresentationTraceSources.DataBindingSource;
        source.Listeners.Add(BindingErrorTraceListener.Instance);
        source.Listeners.Add(BindingErrorTraceListener.Instance);

        BindingErrorTraceListener.Uninstall();

        CountInstanceRegistrations().Should().Be(0);
    }

    [Fact]
    public void Install_WhenListenerAlreadyRegistered_ShouldRestoreErrorLevel()
    {
        var source = PresentationTraceSources.DataBindingSource;
        source.Listeners.Add(BindingErrorTraceListener.Instance);
        source.Switch.Level = SourceLevels.Off;

        BindingErrorTraceListener.Install();

        source.Switch.Level.Should().Be(SourceLevels.Error);
    }

    private static int CountInstanceRegistrations()
        => PresentationTraceSources.DataBindingSource.Listeners
            .Cast<TraceListener>()
            .Count(ReferenceEqualsInstance);

    private static void RemoveAllInstanceRegistrations()
    {
        var source = PresentationTraceSources.DataBindingSource;
        while (source.Listeners.Cast<TraceListener>().Any(ReferenceEqualsInstance))
        {
            source.Listeners.Remove(BindingErrorTraceListener.Instance);
        }
    }

    private static bool ReferenceEqualsInstance(TraceListener listener)
        => ReferenceEquals(listener, BindingErrorTraceListener.Instance);
}
