using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

internal static class BindingDiagnosticsAssertions
{
    private static readonly (string BindingPath, string PropertyName)[] ExpectedIntentionalErrors =
    [
        ("InvalidPropertyName", "Text"),
        ("NonExistent.Property", "Text"),
        ("Name", "Text"),
        ("IsEnabled", "Background")
    ];

    public static int AssertIntentionalBindingErrorsDetected(JsonElement result)
    {
        var actualErrors = GetIntentionalErrorKeys(result);

        actualErrors.Should().Contain(ExpectedIntentionalErrors,
            "TestApp intentional binding errors should include all known broken bindings");

        return actualErrors.Count;
    }

    public static int AssertAtLeastOneIntentionalBindingErrorDetected(JsonElement result)
    {
        var actualErrors = GetIntentionalErrorKeys(result);

        actualErrors.Should().IntersectWith(ExpectedIntentionalErrors,
            "limited get_binding_errors responses should still include a known TestApp intentional binding error");

        return actualErrors.Count;
    }

    private static List<(string BindingPath, string PropertyName)> GetIntentionalErrorKeys(JsonElement result)
    {
        result.GetProperty("success").GetBoolean().Should().BeTrue();

        result.TryGetProperty("errors", out var errors).Should().BeTrue(
            "intentional binding errors should be returned as inspectable error entries, not only as a count");
        errors.ValueKind.Should().Be(JsonValueKind.Array);

        var actualErrors = errors.EnumerateArray()
            .Select(error => (
                BindingPath: TryGetString(error, "bindingPath"),
                PropertyName: TryGetString(error, "propertyName")))
            .ToList();

        actualErrors.Should().NotBeEmpty(
            "TestApp has intentional binding errors that should be detected by get_binding_errors");

        if (result.TryGetProperty("errorCount", out var reportedCount)
            && reportedCount.ValueKind == JsonValueKind.Number)
        {
            reportedCount.GetInt32().Should().BeGreaterThan(0,
                "reported binding error count should not hide missing intentional errors");
        }

        return actualErrors;
    }

    private static string TryGetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
