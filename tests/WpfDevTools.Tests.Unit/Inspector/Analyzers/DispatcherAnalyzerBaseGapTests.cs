using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using System;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class DispatcherAnalyzerBaseGapTests
{
    private sealed record WrappedAnalyzerResult(object Result, object? Session);

    private class TestableAnalyzer : DispatcherAnalyzerBase
    {
        public T TestInvokeOnUIThread<T>(Func<T> action, TimeSpan? timeout = null)
        {
            return InvokeOnUIThread(action, timeout);
        }

        public void TestInvokeOnUIThread(Action action, TimeSpan? timeout = null)
        {
            InvokeOnUIThread(action, timeout);
        }

        public T TestInvokeOnDispatcher<T>(Dispatcher? dispatcher, Func<T> action, TimeSpan? timeout = null)
        {
            return InvokeOnDispatcher(dispatcher, action, timeout);
        }

        public void TestInvokeOnDispatcher(Dispatcher? dispatcher, Action action, TimeSpan? timeout = null)
        {
            InvokeOnDispatcher(dispatcher, action, timeout);
        }

        public bool TestIsOnUIThread()
        {
            return IsOnUIThread();
        }
    }

    [Fact]
    public void IsOnUIThread_NoApplication_ShouldReturnFalse()
    {
        // Arrange - Application.Current is null in unit tests (non-STA thread)
        var analyzer = new TestableAnalyzer();

        // Act
        var result = analyzer.TestIsOnUIThread();

        // Assert - When Application.Current is null, should return false
        result.Should().BeFalse();
    }

    [Fact]
    public void InvokeOnDispatcher_Func_NullDispatcher_ShouldReturnStructuredUnavailableWithoutExecuting()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();
        var executed = false;

        // Act
        var result = analyzer.TestInvokeOnDispatcher<object>(null, () =>
        {
            executed = true;
            return "pipe-thread fallback";
        });

        // Assert
        executed.Should().BeFalse();
        AssertDispatcherUnavailable(result);
    }

    [Fact]
    public void InvokeOnDispatcher_Action_NullDispatcher_ShouldThrowWithoutExecuting()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();
        var executed = false;

        // Act
        var act = () => analyzer.TestInvokeOnDispatcher(null, () => executed = true);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*dispatcher*unavailable*");
        executed.Should().BeFalse();
    }

    [Fact]
    public void InvokeOnDispatcher_Func_NullDispatcher_WithTimeout_ShouldReturnStructuredUnavailable()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();

        // Act
        var result = analyzer.TestInvokeOnDispatcher<object>(
            null,
            () => "test_value",
            TimeSpan.FromSeconds(5));

        // Assert
        AssertDispatcherUnavailable(result);
    }

    [Fact]
    public void InvokeOnDispatcher_Action_NullDispatcher_WithTimeout_ShouldThrowWithoutExecuting()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();
        var executed = false;

        // Act
        var act = () => analyzer.TestInvokeOnDispatcher(
            null,
            () => executed = true,
            TimeSpan.FromSeconds(5));

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*dispatcher*unavailable*");
        executed.Should().BeFalse();
    }

    [Fact]
    public void InvokeOnDispatcher_Func_NullDispatcher_ShouldNotExecuteBodyException()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();

        // Act
        var result = analyzer.TestInvokeOnDispatcher<object>(null, () =>
        {
            throw new InvalidOperationException("test error");
        });

        // Assert
        AssertDispatcherUnavailable(result);
    }

    [Fact]
    public void InvokeOnDispatcher_Func_NullDispatcher_WithWrappedResult_ShouldReturnStructuredUnavailable()
    {
        var analyzer = new TestableAnalyzer();
        var executed = false;

        var result = analyzer.TestInvokeOnDispatcher<WrappedAnalyzerResult>(null, () =>
        {
            executed = true;
            return new WrappedAnalyzerResult("fallback", new object());
        });

        executed.Should().BeFalse();
        result.Session.Should().BeNull();
        AssertDispatcherUnavailable(result.Result);
    }

    [Fact]
    public void InvokeOnDispatcher_Action_NullDispatcher_ShouldNotExecuteBodyException()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();

        // Act
        var act = () => analyzer.TestInvokeOnDispatcher(null, () =>
        {
            throw new InvalidOperationException("test error");
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*dispatcher*unavailable*");
    }

    [Fact]
    public void InvokeOnDispatcher_Func_ShutdownDispatcherOnOwningThread_ShouldNotExecuteBody()
    {
        object? result = null;
        Exception? threadException = null;
        var executed = false;
        using var finished = new ManualResetEventSlim(initialState: false);

        var thread = new Thread(() =>
        {
            try
            {
                var analyzer = new TestableAnalyzer();
                var dispatcher = Dispatcher.CurrentDispatcher;
                dispatcher.InvokeShutdown();

                result = analyzer.TestInvokeOnDispatcher<object>(dispatcher, () =>
                {
                    executed = true;
                    return "shutdown fallback";
                });
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
            finally
            {
                finished.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        finished.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        threadException.Should().BeNull();
        executed.Should().BeFalse();
        AssertDispatcherUnavailable(result!);
    }

    private static void AssertDispatcherUnavailable(object result)
    {
        var json = JsonSerializer.SerializeToElement(result, result.GetType());
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("OperationFailed");
        json.GetProperty("error").GetString().Should().Contain("dispatcher");
        json.GetProperty("hint").GetString().Should().Contain("dispatcher");
    }
}
