using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Events;

public sealed class ValidationChangeTrackerTests
{
    [StaFact]
    public void CreateTransitionEvent_WhenValidationCountChanges_ShouldReturnValidationChangeRecord()
    {
        var finder = new ElementFinder();
        var tracker = new ValidationChangeTracker(finder);
        var root = new StackPanel();
        var textBox = new TextBox();
        root.Children.Add(textBox);
        var scopeElementId = finder.GenerateElementId(root);

        textBox.SetBinding(TextBox.TextProperty, new Binding("Text")
        {
            Source = new { Text = string.Empty },
            Mode = BindingMode.OneWay
        });
        var expression = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);

        var before = tracker.CaptureSnapshot(root);
        Validation.MarkInvalid(
            expression!,
            new ValidationError(new ExceptionValidationRule(), expression!)
            {
                ErrorContent = "Bad value"
            });
        var after = tracker.CaptureSnapshot(root);

        var record = tracker.CreateTransitionEvent(scopeElementId, before, after);

        record.Should().NotBeNull();
        record!.EventType.Should().Be("ValidationChange");
        record.ElementId.Should().Be(scopeElementId);
        record.NewValue.Should().Be("0->1");
    }

    [StaFact]
    public void CreateTransitionEvent_WhenValidationSnapshotDoesNotChange_ShouldReturnNull()
    {
        var finder = new ElementFinder();
        var tracker = new ValidationChangeTracker(finder);
        var root = new StackPanel();
        var textBox = new TextBox();
        root.Children.Add(textBox);
        var scopeElementId = finder.GenerateElementId(root);

        textBox.SetBinding(TextBox.TextProperty, new Binding("Text")
        {
            Source = new { Text = string.Empty },
            Mode = BindingMode.OneWay
        });
        var expression = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
        Validation.MarkInvalid(
            expression!,
            new ValidationError(new ExceptionValidationRule(), expression!)
            {
                ErrorContent = "Bad value"
            });

        var before = tracker.CaptureSnapshot(root);
        var after = tracker.CaptureSnapshot(root);

        tracker.CreateTransitionEvent(scopeElementId, before, after).Should().BeNull();
    }
}
