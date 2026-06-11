using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed partial class ResponseContractResourceTests
{
    private static void AssertHighValueToolContract(
        JsonElement highValueTools,
        string toolName,
        string contractName,
        string[] topLevelFields,
        string[]? requestParameters = null,
        string[]? nestedResponsePaths = null)
    {
        var toolContract = highValueTools
            .EnumerateArray()
            .Single(entry => entry.GetProperty("tool").GetString() == toolName);

        toolContract.GetProperty("contractName").GetString().Should().Be(contractName);
        toolContract.GetProperty("canonicalPayloadField").GetString().Should().Be("result.structuredContent");
        toolContract.GetProperty("textFallbackField").GetString().Should().Be("result.content[0].text");
        toolContract.GetProperty("contractResource").GetString().Should().Be("wpf://contracts/response");

        AssertArrayContains(toolContract.GetProperty("topLevelFields"), topLevelFields);

        if (requestParameters is not null)
        {
            AssertArrayContains(toolContract.GetProperty("requestParameters"), requestParameters);
        }

        if (nestedResponsePaths is not null)
        {
            AssertArrayContains(toolContract.GetProperty("nestedResponsePaths"), nestedResponsePaths);
        }
    }

    private static void AssertTopLevelFieldsDoNotContain(
        JsonElement highValueTools,
        string toolName,
        params string[] excludedFields)
    {
        var toolContract = highValueTools
            .EnumerateArray()
            .Single(entry => entry.GetProperty("tool").GetString() == toolName);

        var fields = toolContract.GetProperty("topLevelFields")
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .Where(entry => entry is not null)
            .Cast<string>()
            .ToArray();

        foreach (var excludedField in excludedFields)
        {
            fields.Should().NotContain(excludedField);
        }
    }

    private static void AssertOutputVariant(
        JsonElement outputVariants,
        string outputMode,
        bool rendered,
        string[] fields)
    {
        var variant = outputVariants
            .EnumerateArray()
            .Single(entry => entry.GetProperty("outputMode").GetString() == outputMode);

        variant.GetProperty("rendered").GetBoolean().Should().Be(rendered);
        AssertArrayContains(variant.GetProperty("fields"), fields);
    }

    private static void AssertArrayContains(JsonElement arrayElement, params string[] expectedValues)
    {
        var values = arrayElement
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .Where(entry => entry is not null)
            .Cast<string>()
            .ToArray();

        foreach (var expectedValue in expectedValues)
        {
            values.Should().Contain(expectedValue);
        }
    }

    private static void AssertParameterVocabulary(
        JsonElement parameterVocabularies,
        string parameterName,
        string defaultValue,
        string[] allowedValues,
        string[] tools)
    {
        var vocabulary = parameterVocabularies
            .EnumerateArray()
            .Single(entry => entry.GetProperty("parameter").GetString() == parameterName);

        vocabulary.GetProperty("defaultValue").GetString().Should().Be(defaultValue);
        AssertArrayContains(vocabulary.GetProperty("allowedValues"), allowedValues);
        AssertArrayContains(vocabulary.GetProperty("tools"), tools);
    }
}
