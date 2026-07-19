using System.Collections;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class BindingAttachedPropertyValueChainTests
{
    [StaFact]
    public void ReturnedSimpleName_WithOneActiveAttachedBinding_ShouldRoundTrip()
    {
        var (analyzer, elementId, button) = CreateTarget();
        button.SetBinding(FirstProbeOwner.SharedProbeProperty, new Binding("RowIndex"));

        dynamic bindingsResult = analyzer.GetBindings(elementId);
        var returnedName = ((IEnumerable<object>)bindingsResult.bindings)
            .Cast<IDictionary>()
            .Should()
            .ContainSingle()
            .Subject["propertyName"]!
            .ToString();

        var result = Serialize(analyzer.GetBindingValueChain(elementId, returnedName!));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("hasBinding").GetBoolean().Should().BeTrue();
    }

    [StaFact]
    public void SimpleName_WithUnboundElementPropertyAndActiveAttachedBinding_ShouldUseActiveBinding()
    {
        var finder = new ElementFinder();
        var analyzer = new BindingAnalyzer(finder);
        var element = new CollisionElement();
        var elementId = finder.GenerateElementId(element);
        element.SetBinding(CollisionAttachedOwner.SharedCollisionProperty, new Binding("Value"));

        var result = Serialize(analyzer.GetBindingValueChain(elementId, "SharedCollision"));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("hasBinding").GetBoolean().Should().BeTrue();
    }

    [StaFact]
    public void SimpleName_WithMultipleActiveBindings_ShouldFailWithQualifiedCandidates()
    {
        var (analyzer, elementId, button) = CreateTarget();
        button.SetBinding(FirstDuplicateOwner.DuplicateActiveProperty, new Binding("First"));
        button.SetBinding(SecondDuplicateOwner.DuplicateActiveProperty, new Binding("Second"));

        var result = Serialize(analyzer.GetBindingValueChain(elementId, "DuplicateActive"));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        var candidates = result.GetProperty("errorData").GetProperty("candidates")
            .EnumerateArray()
            .Select(candidate => candidate.GetString())
            .ToArray();
        candidates.Should().BeEquivalentTo(
            Qualified(FirstDuplicateOwner.DuplicateActiveProperty),
            Qualified(SecondDuplicateOwner.DuplicateActiveProperty));

        var qualifiedResult = Serialize(
            analyzer.GetBindingValueChain(elementId, candidates[0]!));
        qualifiedResult.GetProperty("hasBinding").GetBoolean().Should().BeTrue();
    }

    private static (BindingAnalyzer Analyzer, string ElementId, Button Button) CreateTarget()
    {
        var finder = new ElementFinder();
        var button = new Button();
        return (new BindingAnalyzer(finder), finder.GenerateElementId(button), button);
    }

    private static JsonElement Serialize(object value) => JsonSerializer.SerializeToElement(value);

    private static string Qualified(DependencyProperty property) =>
        $"{property.OwnerType.FullName}.{property.Name}";

    private sealed class CollisionElement : Button
    {
        public static readonly DependencyProperty SharedCollisionProperty =
            DependencyProperty.Register(
                "SharedCollision",
                typeof(int),
                typeof(CollisionElement));
    }

    private static class CollisionAttachedOwner
    {
        public static readonly DependencyProperty SharedCollisionProperty = RegisterAttached(
            "SharedCollision",
            typeof(CollisionAttachedOwner));

        public static int GetSharedCollision(DependencyObject element) =>
            (int)element.GetValue(SharedCollisionProperty);

        public static void SetSharedCollision(DependencyObject element, int value) =>
            element.SetValue(SharedCollisionProperty, value);
    }

    private static class FirstProbeOwner
    {
        public static readonly DependencyProperty SharedProbeProperty = RegisterAttached(
            "SharedProbe",
            typeof(FirstProbeOwner));

        public static int GetSharedProbe(DependencyObject element) =>
            (int)element.GetValue(SharedProbeProperty);

        public static void SetSharedProbe(DependencyObject element, int value) =>
            element.SetValue(SharedProbeProperty, value);
    }

    private static class SecondProbeOwner
    {
        public static readonly DependencyProperty SharedProbeProperty = RegisterAttached(
            "SharedProbe",
            typeof(SecondProbeOwner));

        public static int GetSharedProbe(DependencyObject element) =>
            (int)element.GetValue(SharedProbeProperty);

        public static void SetSharedProbe(DependencyObject element, int value) =>
            element.SetValue(SharedProbeProperty, value);
    }

    private static class FirstDuplicateOwner
    {
        public static readonly DependencyProperty DuplicateActiveProperty = RegisterAttached(
            "DuplicateActive",
            typeof(FirstDuplicateOwner));

        public static int GetDuplicateActive(DependencyObject element) =>
            (int)element.GetValue(DuplicateActiveProperty);

        public static void SetDuplicateActive(DependencyObject element, int value) =>
            element.SetValue(DuplicateActiveProperty, value);
    }

    private static class SecondDuplicateOwner
    {
        public static readonly DependencyProperty DuplicateActiveProperty = RegisterAttached(
            "DuplicateActive",
            typeof(SecondDuplicateOwner));

        public static int GetDuplicateActive(DependencyObject element) =>
            (int)element.GetValue(DuplicateActiveProperty);

        public static void SetDuplicateActive(DependencyObject element, int value) =>
            element.SetValue(DuplicateActiveProperty, value);
    }

    private static DependencyProperty RegisterAttached(string name, Type ownerType) =>
        DependencyProperty.RegisterAttached(name, typeof(int), ownerType);
}
