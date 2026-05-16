using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed class BindingDiagnosticsAssertionsTests
{
    [Fact]
    public void AssertIntentionalBindingErrorsDetected_WithZeroErrorCount_ShouldFail()
    {
        using var document = JsonDocument.Parse("""
            {
              "success": true,
              "errorCount": 0
            }
            """);

        var act = () => BindingDiagnosticsAssertions.AssertIntentionalBindingErrorsDetected(document.RootElement);

        act.Should().Throw<Exception>()
            .WithMessage("*intentional binding errors*");
    }

    [Fact]
    public void AssertIntentionalBindingErrorsDetected_WithErrorsArray_ShouldReturnErrorCount()
    {
        using var document = JsonDocument.Parse("""
            {
              "success": true,
              "errorCount": 4,
              "errors": [
                {
                  "bindingPath": "InvalidPropertyName",
                  "propertyName": "Text"
                },
                {
                  "bindingPath": "NonExistent.Property",
                  "propertyName": "Text"
                },
                {
                  "bindingPath": "Name",
                  "propertyName": "Text"
                },
                {
                  "bindingPath": "IsEnabled",
                  "propertyName": "Background"
                }
              ]
            }
            """);

        var count = BindingDiagnosticsAssertions.AssertIntentionalBindingErrorsDetected(document.RootElement);

        count.Should().Be(4);
    }

    [Fact]
    public void AssertIntentionalBindingErrorsDetected_WithUnrelatedError_ShouldFail()
    {
        using var document = JsonDocument.Parse("""
            {
              "success": true,
              "errorCount": 1,
              "errors": [
                {
                  "bindingPath": "UnrelatedPath",
                  "propertyName": "Text"
                }
              ]
            }
            """);

        var act = () => BindingDiagnosticsAssertions.AssertIntentionalBindingErrorsDetected(document.RootElement);

        act.Should().Throw<Exception>()
            .WithMessage("*intentional binding errors*");
    }

    [Fact]
    public void AssertIntentionalBindingErrorsDetected_WithMissingExpectedError_ShouldFail()
    {
        using var document = JsonDocument.Parse("""
            {
              "success": true,
              "errorCount": 3,
              "errors": [
                {
                  "bindingPath": "InvalidPropertyName",
                  "propertyName": "Text"
                },
                {
                  "bindingPath": "NonExistent.Property",
                  "propertyName": "Text"
                },
                {
                  "bindingPath": "Name",
                  "propertyName": "Text"
                }
              ]
            }
            """);

        var act = () => BindingDiagnosticsAssertions.AssertIntentionalBindingErrorsDetected(document.RootElement);

        act.Should().Throw<Exception>()
            .WithMessage("*IsEnabled*");
    }

    [Fact]
    public void AssertAtLeastOneIntentionalBindingErrorDetected_WithExpectedSubset_ShouldReturnErrorCount()
    {
        using var document = JsonDocument.Parse("""
            {
              "success": true,
              "errorCount": 1,
              "errors": [
                {
                  "bindingPath": "InvalidPropertyName",
                  "propertyName": "Text"
                }
              ]
            }
            """);

        var count = BindingDiagnosticsAssertions.AssertAtLeastOneIntentionalBindingErrorDetected(document.RootElement);

        count.Should().Be(1);
    }
}
