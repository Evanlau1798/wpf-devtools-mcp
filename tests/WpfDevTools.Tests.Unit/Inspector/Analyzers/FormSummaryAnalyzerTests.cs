using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
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
        result.GetProperty("summary").GetProperty("validationSubmittable").GetBoolean().Should().BeTrue();
        result.GetProperty("summary").GetProperty("interactionSubmittable").GetBoolean().Should().BeFalse();
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
        result.GetProperty("commands")
            .EnumerateArray()
            .SelectMany(command => command.GetProperty("blockers").EnumerateArray())
            .Should()
            .Contain(blocker => blocker.GetString() == "ElementInInactiveTab");
    }

    [StaFact]
    public void GetFormSummary_WhenScopedToInactiveTabContent_ShouldExposeInactiveTabScopeMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new FormSummaryAnalyzer(finder);
        var detailsForm = new StackPanel
        {
            Name = "DetailsForm",
            Children =
            {
                new TextBlock { Text = "Email:" },
                new TextBox { Name = "EmailBox", Text = string.Empty },
                new Button { Name = "SubmitButton", Content = "Submit", IsEnabled = true }
            }
        };
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
                    Content = detailsForm
                }
            },
            SelectedIndex = 0
        };
        finder.GenerateElementId(tabs);
        var formElementId = finder.GenerateElementId(detailsForm);

        var result = JsonSerializer.SerializeToElement(analyzer.GetFormSummary(formElementId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("scopeVisibility").GetString().Should().Be("InactiveTab");
        result.GetProperty("isCurrentlyVisible").GetBoolean().Should().BeFalse();
    }

    [StaFact]
    public void GetFormSummary_WhenRootContainsTemplatedTabs_ShouldNotDuplicateInputs()
    {
        var finder = new ElementFinder();
        var analyzer = new FormSummaryAnalyzer(finder);
        var window = new Window();
        var tabs = new TabControl
        {
            Name = "RootTabs",
            Items =
            {
                new TabItem
                {
                    Header = "Profile",
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBox { Name = "FirstNameBox", Text = "Edge" },
                            new TextBox { Name = "LastNameBox", Text = "Case" },
                            new Button { Name = "SaveButton", Content = "Save", IsEnabled = true }
                        }
                    }
                }
            }
        };
        window.Content = tabs;
        window.Show();
        try
        {
            window.ApplyTemplate();
            tabs.ApplyTemplate();
            window.UpdateLayout();
            var elementId = finder.GenerateElementId(window);

            var result = JsonSerializer.SerializeToElement(analyzer.GetFormSummary(elementId));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("summary").GetProperty("totalInputs").GetInt32().Should().Be(2);
            result.GetProperty("inputs")
                .EnumerateArray()
                .Select(input => input.GetProperty("elementName").GetString())
                .Should()
                .OnlyHaveUniqueItems();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void GetFormSummary_ShouldUseHeaderedContainerHeaderAsLabelFallback()
    {
        var finder = new ElementFinder();
        var analyzer = new FormSummaryAnalyzer(finder);
        var groupBox = new GroupBox
        {
            Header = "Account Details",
            Content = new StackPanel
            {
                Children =
                {
                    new TextBox { Name = "AccountNameBox", Text = string.Empty }
                }
            }
        };
        var elementId = finder.GenerateElementId(groupBox);

        var result = JsonSerializer.SerializeToElement(analyzer.GetFormSummary(elementId));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("inputs")[0].GetProperty("label").GetString().Should().Be("Account Details");
    }

    [StaFact]
    public void GetFormSummary_ShouldDisambiguateRepeatedSectionHeaderFallbackLabels()
    {
        var finder = new ElementFinder();
        var analyzer = new FormSummaryAnalyzer(finder);
        var groupBox = new GroupBox
        {
            Header = "Focus and Keyboard Testing",
            Content = new StackPanel
            {
                Children =
                {
                    new TextBox { Name = "FocusBox1", Text = string.Empty },
                    new TextBox { Name = "FocusBox2", Text = string.Empty },
                    new TextBox { Name = "FocusBox3", Text = string.Empty }
                }
            }
        };
        var elementId = finder.GenerateElementId(groupBox);

        var result = JsonSerializer.SerializeToElement(analyzer.GetFormSummary(elementId));
        var labels = result.GetProperty("inputs")
            .EnumerateArray()
            .Select(input => input.GetProperty("label").GetString())
            .ToArray();

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        labels.Should().OnlyHaveUniqueItems();
        labels.Should().Contain("Focus and Keyboard Testing / Focus Box 1");
        labels.Should().Contain("Focus and Keyboard Testing / Focus Box 2");
        labels.Should().Contain("Focus and Keyboard Testing / Focus Box 3");
    }

    [StaFact]
    public void GetFormSummary_ShouldFilterFrameworkNoiseByDefaultAndRestoreItOnRequest()
    {
        var finder = new ElementFinder();
        var analyzer = new FormSummaryAnalyzer(finder);
        var frameworkChrome = new ContentControl
        {
            Template = (ControlTemplate)XamlReader.Parse("""
                <ControlTemplate
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    TargetType="{x:Type ContentControl}">
                    <StackPanel>
                        <Button x:Name="PART_MinimizeButton" Content="Minimize" />
                        <Button x:Name="ClearButton" Content="Clear" Visibility="Collapsed" Width="0" Height="0" />
                    </StackPanel>
                </ControlTemplate>
                """)
        };
        var form = new StackPanel
        {
            Name = "NoiseForm",
            Children =
            {
                new TextBox { Name = "NameBox", Text = "Edge" },
                new Button { Name = "SaveButton", Content = "Save", IsEnabled = true },
                new RepeatButton { Name = "PART_LineUpButton" },
                new RepeatButton(),
                frameworkChrome
            }
        };
        var window = new Window
        {
            Content = form
        };

        try
        {
            window.Show();
            frameworkChrome.ApplyTemplate();
            window.UpdateLayout();
            var elementId = finder.GenerateElementId(form);

            var filtered = JsonSerializer.SerializeToElement(analyzer.GetFormSummary(elementId));
            var complete = JsonSerializer.SerializeToElement(analyzer.GetFormSummary(elementId, includeFramework: true));

            filtered.GetProperty("commands")
                .EnumerateArray()
                .Select(command => command.GetProperty("elementName").GetString())
                .Should()
                .Equal("SaveButton");
            filtered.GetProperty("omittedFrameworkElementCount").GetInt32().Should().Be(4);
            complete.GetProperty("commands").GetArrayLength().Should().Be(5);
            complete.GetProperty("omittedFrameworkElementCount").GetInt32().Should().Be(0);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void GetFormSummary_WhenFallbackCommandIsReadyButValidationErrorsExist_ShouldReportNotSubmittable()
    {
        var finder = new ElementFinder();
        var analyzer = new FormSummaryAnalyzer(finder);
        var textBox = new TextBox { Name = "NameBox" };
        InjectValidationError(textBox, "Name is required");
        var form = new StackPanel
        {
            Name = "FallbackOnlyForm",
            Children =
            {
                new TextBlock { Text = "Name:" },
                textBox,
                new Button { Name = "ContinueButton", Content = "Continue", IsEnabled = true }
            }
        };
        var window = new Window
        {
            Content = form
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            var elementId = finder.GenerateElementId(form);

            var result = JsonSerializer.SerializeToElement(analyzer.GetFormSummary(elementId));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("summary").GetProperty("errorCount").GetInt32().Should().Be(1);
            result.GetProperty("summary").GetProperty("validationSubmittable").GetBoolean().Should().BeFalse();
            result.GetProperty("summary").GetProperty("interactionSubmittable").GetBoolean().Should().BeTrue();
            result.GetProperty("summary").GetProperty("isSubmittable").GetBoolean().Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void GetFormSummary_WhenFormExceedsPayloadBudget_ShouldTruncateAndReportMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new FormSummaryAnalyzer(finder);
        var form = new StackPanel { Name = "LargeForm" };
        for (var index = 0; index < 160; index++)
        {
            form.Children.Add(new TextBox
            {
                Name = $"Input{index:000}",
                Text = new string('x', 80)
            });
        }

        var elementId = finder.GenerateElementId(form);

        var result = JsonSerializer.SerializeToElement(analyzer.GetFormSummary(elementId));
        var totalInputs = result.GetProperty("summary").GetProperty("totalInputs").GetInt32();
        var limits = result.GetProperty("payloadLimits");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
        result.GetProperty("inputs").GetArrayLength().Should().Be(totalInputs);
        totalInputs.Should().Be(limits.GetProperty("maxInputs").GetInt32());
        result.GetProperty("omittedInputCount").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("truncationReasons")
            .EnumerateArray()
            .Select(reason => reason.GetString())
            .Should()
            .Contain("InputLimit");
    }

    private static void InjectValidationError(TextBox textBox, string errorMessage)
    {
        var binding = new System.Windows.Data.Binding("Text")
        {
            Source = new { Text = "" },
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        textBox.SetBinding(TextBox.TextProperty, binding);

        var expression = System.Windows.Data.BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
        Validation.MarkInvalid(
            expression!,
            new ValidationError(new ExceptionValidationRule(), expression!) { ErrorContent = errorMessage });
    }
}
