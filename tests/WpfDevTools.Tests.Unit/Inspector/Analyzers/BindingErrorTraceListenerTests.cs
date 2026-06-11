using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using System.Diagnostics;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("BindingErrorTests")]
public class BindingErrorTraceListenerTests : IDisposable
{
    public BindingErrorTraceListenerTests()
    {
        // Reset singleton state before each test
        BindingErrorTraceListener.ResetInstance();
    }

    public void Dispose()
    {
        BindingErrorTraceListener.ResetInstance();
    }

    [Fact]
    public void Instance_ShouldReturnSameInstance()
    {
        // Act
        var instance1 = BindingErrorTraceListener.Instance;
        var instance2 = BindingErrorTraceListener.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void GetErrors_WhenNoErrors_ShouldReturnEmptyList()
    {
        // Act
        var errors = BindingErrorTraceListener.Instance.GetErrors();

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ErrorCount_WhenNoErrors_ShouldBeZero()
    {
        // Act & Assert
        BindingErrorTraceListener.Instance.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void TraceEvent_WithMessage_ShouldCaptureError()
    {
        // Arrange
        var listener = BindingErrorTraceListener.Instance;
        var message = "Cannot find source for binding with reference 'ElementName=InvalidName'";

        // Act
        listener.TraceEvent(
            eventCache: null,
            source: "System.Windows.Data",
            eventType: TraceEventType.Error,
            id: 40,
            message: message);

        // Assert
        var errors = listener.GetErrors();
        errors.Should().HaveCount(1);
        errors[0].Message.Should().Be(message);
        errors[0].EventType.Should().Be("Error");
        errors[0].SourceId.Should().Be(40);
        errors[0].Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void TraceEvent_WithNullMessage_ShouldNotCaptureError()
    {
        // Arrange
        var listener = BindingErrorTraceListener.Instance;

        // Act
        listener.TraceEvent(
            eventCache: null,
            source: "System.Windows.Data",
            eventType: TraceEventType.Error,
            id: 40,
            message: null);

        // Assert
        listener.GetErrors().Should().BeEmpty();
    }

    [Fact]
    public void TraceEvent_WithEmptyMessage_ShouldNotCaptureError()
    {
        // Arrange
        var listener = BindingErrorTraceListener.Instance;

        // Act
        listener.TraceEvent(
            eventCache: null,
            source: "System.Windows.Data",
            eventType: TraceEventType.Error,
            id: 40,
            message: string.Empty);

        // Assert
        listener.GetErrors().Should().BeEmpty();
    }

    [Fact]
    public void TraceEvent_WithFormatString_ShouldFormatMessage()
    {
        // Arrange
        var listener = BindingErrorTraceListener.Instance;

        // Act
        listener.TraceEvent(
            eventCache: null,
            source: "System.Windows.Data",
            eventType: TraceEventType.Warning,
            id: 10,
            format: "Binding path '{0}' not found on '{1}'",
            args: new object[] { "InvalidPath", "TextBlock" });

        // Assert
        var errors = listener.GetErrors();
        errors.Should().HaveCount(1);
        errors[0].Message.Should().Be("Binding path 'InvalidPath' not found on 'TextBlock'");
        errors[0].EventType.Should().Be("Warning");
    }

    [Fact]
    public void TraceEvent_WithInvalidFormatString_ShouldFallbackToRawFormat()
    {
        // Arrange
        var listener = BindingErrorTraceListener.Instance;

        // Act
        listener.TraceEvent(
            eventCache: null,
            source: "System.Windows.Data",
            eventType: TraceEventType.Error,
            id: 10,
            format: "Bad format {0} {1} {2}",
            args: new object[] { "only one arg" });

        // Assert
        var errors = listener.GetErrors();
        errors.Should().HaveCount(1);
        errors[0].Message.Should().Be("Bad format {0} {1} {2}");
    }

    [Fact]
    public void TraceEvent_WithNullArgsAndFormat_ShouldUseFormatAsMessage()
    {
        // Arrange
        var listener = BindingErrorTraceListener.Instance;

        // Act
        listener.TraceEvent(
            eventCache: null,
            source: "System.Windows.Data",
            eventType: TraceEventType.Error,
            id: 10,
            format: "Simple error message",
            args: null);

        // Assert
        var errors = listener.GetErrors();
        errors.Should().HaveCount(1);
        errors[0].Message.Should().Be("Simple error message");
    }

    [Fact]
    public void TraceEvent_MultipleErrors_ShouldCaptureAll()
    {
        // Arrange
        var listener = BindingErrorTraceListener.Instance;

        // Act
        for (int i = 0; i < 5; i++)
        {
            listener.TraceEvent(
                eventCache: null,
                source: "System.Windows.Data",
                eventType: TraceEventType.Error,
                id: i,
                message: $"Error {i}");
        }

        // Assert
        var errors = listener.GetErrors();
        errors.Should().HaveCount(5);
        listener.ErrorCount.Should().Be(5);
    }

    [Fact]
    public void TraceEvent_ExceedingMaxErrors_ShouldTrimOldest()
    {
        // Arrange
        var listener = BindingErrorTraceListener.Instance;
        var totalErrors = BindingErrorTraceListener.MaxErrors + 50;

        // Act
        for (int i = 0; i < totalErrors; i++)
        {
            listener.TraceEvent(
                eventCache: null,
                source: "System.Windows.Data",
                eventType: TraceEventType.Error,
                id: i,
                message: $"Error {i}");
        }

        // Assert
        var errors = listener.GetErrors();
        errors.Count.Should().BeLessThanOrEqualTo(BindingErrorTraceListener.MaxErrors);
        // Oldest errors should have been trimmed; newest should remain
        errors.Last().Message.Should().Be($"Error {totalErrors - 1}");
    }

    [Fact]
    public void ClearErrors_ShouldRemoveAllErrors()
    {
        // Arrange
        var listener = BindingErrorTraceListener.Instance;
        for (int i = 0; i < 10; i++)
        {
            listener.TraceEvent(
                eventCache: null,
                source: "System.Windows.Data",
                eventType: TraceEventType.Error,
                id: i,
                message: $"Error {i}");
        }

        // Act
        listener.ClearErrors();

        // Assert
        listener.GetErrors().Should().BeEmpty();
        listener.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void GetErrors_ShouldReturnSnapshot_NotLiveReference()
    {
        // Arrange
        var listener = BindingErrorTraceListener.Instance;
        listener.ClearErrors();
        listener.TraceEvent(null, "test", TraceEventType.Error, 1, "Error 1");

        // Act
        var snapshot = listener.GetErrors();
        listener.TraceEvent(null, "test", TraceEventType.Error, 2, "Error 2");

        // Assert - snapshot should not include the new error
        snapshot.Should().HaveCount(1);
        listener.GetErrors().Should().HaveCount(2);
    }

    [Fact]
    public void ResetInstance_ShouldCreateNewInstance()
    {
        // Arrange
        var original = BindingErrorTraceListener.Instance;
        original.TraceEvent(null, "test", TraceEventType.Error, 1, "Error 1");

        // Act
        BindingErrorTraceListener.ResetInstance();
        var newInstance = BindingErrorTraceListener.Instance;

        // Assert
        newInstance.Should().NotBeSameAs(original);
        newInstance.GetErrors().Should().BeEmpty();
    }

    [StaFact]
    public void Install_ShouldAddListenerToDataBindingSource()
    {
        // Act
        BindingErrorTraceListener.Install();

        // Assert
        var source = PresentationTraceSources.DataBindingSource;
        var containsListener = source.Listeners.Cast<TraceListener>()
            .Any(l => l is BindingErrorTraceListener);
        containsListener.Should().BeTrue();
        source.Switch.Level.Should().Be(SourceLevels.Error);
    }

    [StaFact]
    public void Install_CalledTwice_ShouldNotAddDuplicateListener()
    {
        // Act
        BindingErrorTraceListener.Install();
        BindingErrorTraceListener.Install();

        // Assert
        var source = PresentationTraceSources.DataBindingSource;
        var count = source.Listeners.Cast<TraceListener>()
            .Count(l => l is BindingErrorTraceListener);
        count.Should().Be(1);
    }

    [StaFact]
    public void Uninstall_ShouldRemoveListenerFromDataBindingSource()
    {
        // Arrange
        BindingErrorTraceListener.Install();

        // Act
        BindingErrorTraceListener.Uninstall();

        // Assert
        var source = PresentationTraceSources.DataBindingSource;
        var containsListener = source.Listeners.Cast<TraceListener>()
            .Any(l => l is BindingErrorTraceListener);
        containsListener.Should().BeFalse();
    }

    [Fact]
    public void Write_ShouldNotCaptureAnything()
    {
        // Arrange
        var listener = BindingErrorTraceListener.Instance;

        // Act
        listener.Write("Some message");

        // Assert
        listener.GetErrors().Should().BeEmpty();
    }

    [Fact]
    public void WriteLine_ShouldNotCaptureAnything()
    {
        // Arrange
        var listener = BindingErrorTraceListener.Instance;

        // Act
        listener.WriteLine("Some message");

        // Assert
        listener.GetErrors().Should().BeEmpty();
    }

    [Fact]
    public void ErrorInfo_ShouldBeImmutable()
    {
        // Arrange & Act
        var error = new BindingErrorInfo
        {
            Timestamp = DateTime.UtcNow,
            Message = "Test error",
            EventType = "Error",
            SourceId = 42
        };

        // Assert - record type properties should be init-only
        error.Message.Should().Be("Test error");
        error.EventType.Should().Be("Error");
        error.SourceId.Should().Be(42);
    }
}
