using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ResponseContractParameterVocabularyTests
{
    public static TheoryData<string, string, string[], string[]> ClosedVocabularyParameters => new()
    {
        {
            "matchMode",
            "exact",
            ["exact", "contains"],
            ["find_elements"]
        },
        {
            "direction",
            "Source",
            ["Source", "Target"],
            ["force_binding_update"]
        },
        {
            "mode",
            "capture",
            ["capture", "start", "get"],
            ["trace_routed_events"]
        },
        {
            "statusFilter",
            "All",
            ["All", "Active", "Error"],
            ["get_bindings"]
        },
        {
            "eventType",
            "KeyDown",
            ["KeyDown", "KeyUp"],
            ["simulate_keyboard"]
        },
        {
            "eventTypes",
            "all",
            ["DpChange", "RoutedEvent", "BindingError", "ValidationChange"],
            ["drain_events"]
        }
    };

    [Theory]
    [MemberData(nameof(ClosedVocabularyParameters))]
    public void ResponseContractResource_ShouldExposeClosedVocabularyForEnumLikeParameters(
        string parameterName,
        string defaultValue,
        string[] allowedValues,
        string[] tools)
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var vocabularies = document.RootElement.GetProperty("parameterVocabularies");

        var vocabulary = FindVocabulary(vocabularies, parameterName, tools);

        vocabulary.GetProperty("defaultValue").GetString().Should().Be(defaultValue);
        GetStringArray(vocabulary.GetProperty("allowedValues")).Should().BeEquivalentTo(allowedValues);
        GetStringArray(vocabulary.GetProperty("tools")).Should().BeEquivalentTo(tools);
    }

    [Fact]
    public void ResponseContractResource_ParameterVocabularies_ShouldRemainMachineReadable()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var vocabularies = document.RootElement.GetProperty("parameterVocabularies");
        var parameterToolPairs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var vocabulary in vocabularies.EnumerateArray())
        {
            var parameter = vocabulary.GetProperty("parameter").GetString();
            parameter.Should().NotBeNullOrWhiteSpace();
            vocabulary.TryGetProperty("defaultValue", out _).Should().BeTrue();

            var allowedValues = GetStringArray(vocabulary.GetProperty("allowedValues"));
            allowedValues.Should().NotBeEmpty();
            allowedValues.Should().OnlyContain(value => !string.IsNullOrWhiteSpace(value));

            var tools = GetStringArray(vocabulary.GetProperty("tools"));
            tools.Should().NotBeEmpty();
            tools.Should().OnlyContain(tool => !string.IsNullOrWhiteSpace(tool));

            foreach (var tool in tools)
            {
                parameterToolPairs.Add($"{parameter}:{tool}").Should().BeTrue();
            }
        }
    }

    private static JsonElement FindVocabulary(
        JsonElement vocabularies,
        string parameterName,
        string[] tools)
    {
        var matches = vocabularies
            .EnumerateArray()
            .Where(entry => entry.GetProperty("parameter").GetString() == parameterName)
            .Where(entry => tools.All(tool => GetStringArray(entry.GetProperty("tools")).Contains(tool)))
            .ToArray();

        matches.Should().ContainSingle(
            $"parameterVocabularies should describe {parameterName} for {string.Join(", ", tools)}");

        return matches[0];
    }

    private static string[] GetStringArray(JsonElement arrayElement)
    {
        return arrayElement
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .Where(entry => entry is not null)
            .Cast<string>()
            .ToArray();
    }
}
