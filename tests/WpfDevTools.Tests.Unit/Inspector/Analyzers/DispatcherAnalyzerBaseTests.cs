using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class DispatcherAnalyzerBaseTests
{

    private class TestAnalyzer : DispatcherAnalyzerBase
    {
        public TestAnalyzer()
        {
        }

        public TestAnalyzer(ElementFinder elementFinder)
            : base(elementFinder)
        {
        }

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

        public static object? TestConvertValue(object? value, Type targetType)
        {
            return ConvertValue(value, targetType);
        }

        public static DependencyProperty? TestFindDependencyProperty(DependencyObject element, string propertyName)
        {
            return FindDependencyProperty(element, propertyName);
        }

        public static long TestLoadedTypeEnumerationCount => LoadedTypeEnumerationCount;

        public static void TestResetDependencyPropertyLookupDiagnostics()
        {
            ResetDependencyPropertyLookupDiagnostics();
        }

        public DependencyObject? TestResolveElement(string? elementId)
        {
            return ResolveElement(elementId);
        }
    }

    [StaFact]
    public void InvokeOnDispatcher_WhenOnDispatcherThread_ShouldExecuteAndReturnValue()
    {
        // Arrange
        var analyzer = new TestAnalyzer();
        var expectedValue = 42;

        // Act
        var result = analyzer.TestInvokeOnDispatcher(
            Dispatcher.CurrentDispatcher,
            () => expectedValue);

        // Assert
        result.Should().Be(expectedValue);
    }

    [StaFact]
    public void InvokeOnDispatcher_WithAction_WhenOnDispatcherThread_ShouldExecute()
    {
        // Arrange
        var analyzer = new TestAnalyzer();
        var executed = false;

        // Act
        analyzer.TestInvokeOnDispatcher(
            Dispatcher.CurrentDispatcher,
            () => executed = true);

        // Assert
        executed.Should().BeTrue();
    }

    [StaFact]
    public void IsOnUIThread_WhenOnUIThread_ShouldReturnTrue()
    {
        // Arrange
        var analyzer = new TestAnalyzer();

        // Act
        var result = analyzer.TestIsOnUIThread();

        // Assert - in STA test, we should be on UI thread if Application exists
        if (Application.Current != null)
        {
            result.Should().BeTrue();
        }
    }

    [Fact]
    public void ConvertValue_WithNull_ShouldReturnNull()
    {
        // Act
        var result = TestAnalyzer.TestConvertValue(null, typeof(string));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveElement_WithNullElementId_ShouldUseRootElement()
    {
        var elementFinder = new ElementFinder();
        var analyzer = new TestAnalyzer(elementFinder);

        var result = analyzer.TestResolveElement(null);

        result.Should().Be(elementFinder.GetRootElement());
    }

    [StaFact]
    public void ResolveElement_WithElementId_ShouldUseFindById()
    {
        var elementFinder = new ElementFinder();
        var analyzer = new TestAnalyzer(elementFinder);
        var element = new Button();
        var elementId = elementFinder.GenerateElementId(element);

        var result = analyzer.TestResolveElement(elementId);

        result.Should().BeSameAs(element);
    }

    [Fact]
    public void ConvertValue_WithSameType_ShouldReturnValue()
    {
        // Arrange
        var value = "test";

        // Act
        var result = TestAnalyzer.TestConvertValue(value, typeof(string));

        // Assert
        result.Should().Be(value);
    }

    [Fact]
    public void ConvertValue_WithCompatibleType_ShouldReturnValue()
    {
        // Arrange
        var value = "test";

        // Act
        var result = TestAnalyzer.TestConvertValue(value, typeof(object));

        // Assert
        result.Should().Be(value);
    }

    [Fact]
    public void ConvertValue_WithIntToDouble_ShouldConvert()
    {
        // Arrange
        var value = 42;

        // Act
        var result = TestAnalyzer.TestConvertValue(value, typeof(double));

        // Assert
        result.Should().Be(42.0);
    }

    [Fact]
    public void ConvertValue_WithStringToInt_ShouldConvert()
    {
        // Arrange
        var value = "123";

        // Act
        var result = TestAnalyzer.TestConvertValue(value, typeof(int));

        // Assert
        result.Should().Be(123);
    }

    [StaFact]
    public void ConvertValue_WithStringToBrush_ShouldConvert()
    {
        // Arrange
        var value = "Red";

        // Act
        var result = TestAnalyzer.TestConvertValue(value, typeof(Brush));

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<SolidColorBrush>();
        var brush = result as SolidColorBrush;
        brush!.Color.Should().Be(Colors.Red);
    }


    [Fact]
    public void ConvertValue_WithJsonNumberToDouble_ShouldConvert()
    {
        // Arrange
        var value = JsonSerializer.Deserialize<JsonElement>("220");

        // Act
        var result = TestAnalyzer.TestConvertValue(value, typeof(double));

        // Assert
        result.Should().Be(220.0);
    }

    [Fact]
    public void ConvertValue_WithJsonStringToString_ShouldConvert()
    {
        // Arrange
        var value = JsonSerializer.Deserialize<JsonElement>("\"Codex\"");

        // Act
        var result = TestAnalyzer.TestConvertValue(value, typeof(string));

        // Assert
        result.Should().Be("Codex");
    }

    [StaFact]
    public void ConvertValue_WithJsonStringToBrush_ShouldConvert()
    {
        // Arrange
        var value = JsonSerializer.Deserialize<JsonElement>("\"Orange\"");

        // Act
        var result = TestAnalyzer.TestConvertValue(value, typeof(Brush));

        // Assert
        result.Should().BeOfType<SolidColorBrush>();
        ((SolidColorBrush)result!).Color.Should().Be(Colors.Orange);
    }
    [Fact]
    public void ConvertValue_WithQuotedJsonString_ShouldStripSurroundingQuotes()
    {
        // Simulate: AI agent sends value: "\"Alice\"" -> JSON string content has literal quotes
        var jsonWithQuotes = JsonSerializer.Deserialize<JsonElement>("\"\\\"Alice\\\"\"");

        var result = TestAnalyzer.TestConvertValue(jsonWithQuotes, typeof(string));

        result.Should().Be("Alice", "Surrounding double-quotes should be stripped from string values");
    }

    [StaFact]
    public void ConvertValue_WithQuotedJsonStringToBrush_ShouldStripQuotesAndConvert()
    {
        // Simulate: AI agent sends value: "\"Blue\"" for a Brush property
        var jsonWithQuotes = JsonSerializer.Deserialize<JsonElement>("\"\\\"Blue\\\"\"");

        var result = TestAnalyzer.TestConvertValue(jsonWithQuotes, typeof(Brush));

        result.Should().BeOfType<SolidColorBrush>();
        ((SolidColorBrush)result!).Color.Should().Be(Colors.Blue);
    }

    [Fact]
    public void ConvertValue_WithUnquotedJsonString_ShouldNotChange()
    {
        var jsonNormal = JsonSerializer.Deserialize<JsonElement>("\"Alice\"");

        var result = TestAnalyzer.TestConvertValue(jsonNormal, typeof(string));

        result.Should().Be("Alice", "Normal string values should pass through unchanged");
    }

    [Fact]
    public void ConvertValue_WithSingleQuoteString_ShouldNotStrip()
    {
        var result = TestAnalyzer.TestConvertValue("\"Hello", typeof(string));

        result.Should().Be("\"Hello", "Strings with only one quote should not be modified");
    }

    [StaFact]
    public void FindDependencyProperty_WithValidProperty_ShouldReturnProperty()
    {
        // Arrange
        var button = new Button();

        // Act
        var result = TestAnalyzer.TestFindDependencyProperty(button, "Width");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(FrameworkElement.WidthProperty);
    }

    [StaFact]
    public void FindDependencyProperty_WithInvalidProperty_ShouldReturnNull()
    {
        // Arrange
        var button = new Button();

        // Act
        var result = TestAnalyzer.TestFindDependencyProperty(button, "NonExistentProperty");

        // Assert
        result.Should().BeNull();
    }

    [StaFact]
    public void FindDependencyProperty_WithInheritedProperty_ShouldReturnProperty()
    {
        // Arrange
        var button = new Button();

        // Act
        var result = TestAnalyzer.TestFindDependencyProperty(button, "Visibility");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(UIElement.VisibilityProperty);
    }

    [StaFact]
    public void FindDependencyProperty_WithContentProperty_ShouldReturnProperty()
    {
        // Arrange
        var button = new Button();

        // Act
        var result = TestAnalyzer.TestFindDependencyProperty(button, "Content");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(ContentControl.ContentProperty);
    }

    [StaFact]
    public void FindDependencyProperty_WithRepeatedAttachedLookup_ShouldReuseLoadedTypeScan()
    {
        var button = new Button();
        TestAnalyzer.TestResetDependencyPropertyLookupDiagnostics();

        var first = TestAnalyzer.TestFindDependencyProperty(button, "Grid.Row");
        var afterFirstLookup = TestAnalyzer.TestLoadedTypeEnumerationCount;
        var second = TestAnalyzer.TestFindDependencyProperty(button, "Grid.Row");

        first.Should().Be(Grid.RowProperty);
        second.Should().Be(Grid.RowProperty);
        afterFirstLookup.Should().BeGreaterThan(0);
        TestAnalyzer.TestLoadedTypeEnumerationCount.Should().Be(afterFirstLookup,
            "the same qualified dependency property lookup should not rescan all loaded types");
    }

    [StaFact]
    public void FindDependencyProperty_WithRepeatedMissingLookup_ShouldReuseLoadedTypeScanMiss()
    {
        var button = new Button();
        TestAnalyzer.TestResetDependencyPropertyLookupDiagnostics();

        var first = TestAnalyzer.TestFindDependencyProperty(button, "MissingAttachedProperty");
        var afterFirstLookup = TestAnalyzer.TestLoadedTypeEnumerationCount;
        var second = TestAnalyzer.TestFindDependencyProperty(button, "MissingAttachedProperty");

        first.Should().BeNull();
        second.Should().BeNull();
        afterFirstLookup.Should().BeGreaterThan(0);
        TestAnalyzer.TestLoadedTypeEnumerationCount.Should().Be(afterFirstLookup,
            "missing dependency property lookups should cache misses instead of rescanning all loaded types");
    }
}
