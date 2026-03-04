using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using System;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class DispatcherAnalyzerBaseGapTests
{

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
    public void InvokeOnUIThread_Func_NoApplication_ShouldExecuteDirectly()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();
        var executed = false;

        // Act - when Application.Current is null, action executes directly
        var result = analyzer.TestInvokeOnUIThread(() =>
        {
            executed = true;
            return 42;
        });

        // Assert
        executed.Should().BeTrue();
        result.Should().Be(42);
    }

    [Fact]
    public void InvokeOnUIThread_Action_NoApplication_ShouldExecuteDirectly()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();
        var executed = false;

        // Act - when Application.Current is null, action executes directly
        analyzer.TestInvokeOnUIThread(() => executed = true);

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public void InvokeOnUIThread_Func_NoApplication_WithTimeout_ShouldExecuteDirectly()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();

        // Act - timeout is ignored when Application.Current is null
        var result = analyzer.TestInvokeOnUIThread(
            () => "test_value",
            TimeSpan.FromSeconds(5));

        // Assert
        result.Should().Be("test_value");
    }

    [Fact]
    public void InvokeOnUIThread_Action_NoApplication_WithTimeout_ShouldExecuteDirectly()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();
        var executed = false;

        // Act
        analyzer.TestInvokeOnUIThread(
            () => executed = true,
            TimeSpan.FromSeconds(5));

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public void InvokeOnUIThread_Func_NoApplication_ShouldPropagateException()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();

        // Act & Assert - exception should propagate when no Application.Current
        var act = () => analyzer.TestInvokeOnUIThread<int>(() =>
        {
            throw new InvalidOperationException("test error");
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("test error");
    }

    [Fact]
    public void InvokeOnUIThread_Action_NoApplication_ShouldPropagateException()
    {
        // Arrange
        var analyzer = new TestableAnalyzer();

        // Act & Assert
        var act = () => analyzer.TestInvokeOnUIThread(() =>
        {
            throw new InvalidOperationException("test error");
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("test error");
    }
}
