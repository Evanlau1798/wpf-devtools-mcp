using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class FormSummaryAnalyzerTests
{
    [StaFact]
    public void GetFormSummary_ShouldAggregateInputsAndCommandReadiness()
    {
        var finder = new ElementFinder();
        var analyzer = new FormSummaryAnalyzer(finder);
        var form = new StackPanel
        {
            Name = "ProfileForm"
        };
        form.Children.Add(new TextBlock { Text = "Name:" });
        form.Children.Add(new TextBox { Name = "NameBox", Text = "" });
        form.Children.Add(new TextBlock { Text = "Age:" });
        form.Children.Add(new TextBox { Name = "AgeBox", Text = "" });
        form.Children.Add(new Button { Name = "SaveButton", Content = "Save", IsEnabled = false });
        var elementId = finder.GenerateElementId(form);

        var result = JsonSerializer.SerializeToElement(analyzer.GetFormSummary(elementId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("summary").GetProperty("totalInputs").GetInt32().Should().Be(2);
        result.GetProperty("summary").GetProperty("emptyInputs").GetInt32().Should().Be(2);
        result.GetProperty("summary").GetProperty("isSubmittable").GetBoolean().Should().BeFalse();
        result.GetProperty("inputs")[0].GetProperty("label").GetString().Should().Be("Name");
    }

    [StaFact]
    public void GetFormSummary_WhenInputsAreInsideInactiveTab_ShouldStillAggregateThem()
    {
        var finder = new ElementFinder();
        var analyzer = new FormSummaryAnalyzer(finder);
        var tabs = new TabControl
        {
            Name = "WizardTabs",
            Items =
            {
                new TabItem
                {
                    Header = "Step1",
                    Content = new TextBlock { Text = "Welcome" }
                },
                new TabItem
                {
                    Header = "Step2",
                    Content = new StackPanel
                    {
                        Name = "DetailsForm",
                        Children =
                        {
                            new TextBlock { Text = "Email:" },
                            new TextBox { Name = "EmailBox", Text = string.Empty },
                            new CheckBox { Name = "AcceptBox", Content = "Accept", IsChecked = false },
                            new Button { Name = "SubmitButton", Content = "Submit", IsEnabled = true }
                        }
                    }
                }
            }
        };
        tabs.SelectedIndex = 0;
        var elementId = finder.GenerateElementId(tabs);

        var result = JsonSerializer.SerializeToElement(analyzer.GetFormSummary(elementId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("summary").GetProperty("totalInputs").GetInt32().Should().Be(2);
        result.GetProperty("summary").GetProperty("emptyInputs").GetInt32().Should().Be(1);
        result.GetProperty("commands")
            .EnumerateArray()
            .Select(command => command.GetProperty("elementName").GetString())
            .Should()
            .Contain("SubmitButton");
    }
}
