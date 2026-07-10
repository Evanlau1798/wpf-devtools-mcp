using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class ElementSearchAssignableTypeTests
{
    [StaFact]
    public void FindElements_WithAssignableTypeMatchMode_ShouldMatchBaseTypesAndInterfaces()
    {
        var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);
        var window = CreateWindow();
        try
        {
            window.Content = new SearchButton { Name = "SearchButton" };
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);

            var baseTypeResult = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: windowId,
                typeName: nameof(Button),
                typeMatchMode: "assignable"));
            var interfaceResult = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: windowId,
                typeName: nameof(ISearchMarker),
                typeMatchMode: "assignable"));
            var containsResult = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: windowId,
                typeName: "utto",
                matchMode: "contains",
                typeMatchMode: "assignable"));

            baseTypeResult.GetProperty("resultCount").GetInt32().Should().Be(1);
            interfaceResult.GetProperty("resultCount").GetInt32().Should().Be(1);
            containsResult.GetProperty("resultCount").GetInt32().Should().Be(1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FindElements_WithoutTypeMatchMode_ShouldKeepExactTypeMatching()
    {
        var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);
        var window = CreateWindow();
        try
        {
            window.Content = new SearchButton { Name = "SearchButton" };
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: windowId,
                typeName: nameof(Button)));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("resultCount").GetInt32().Should().Be(0);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FindElements_WithInvalidTypeMatchMode_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);
        var window = CreateWindow();
        try
        {
            window.Content = new SearchButton { Name = "SearchButton" };
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: windowId,
                typeName: nameof(Button),
                typeMatchMode: "derived"));

            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
            result.GetProperty("error").GetString().Should().Contain("typeMatchMode");
        }
        finally
        {
            window.Close();
        }
    }

    private static Window CreateWindow()
        => new()
        {
            Width = 300,
            Height = 200,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };

    private interface ISearchMarker;

    private sealed class SearchButton : Button, ISearchMarker;
}
