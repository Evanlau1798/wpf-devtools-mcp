using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpMutationOutputContractAliasTests
{
    [Fact]
    public void StateDiffContracts_ShouldNotAdvertiseDiffAliasAbsentFromRuntimePayload()
    {
        AssertSchemaFields(
            "get_state_diff",
            includedFields: [
                "propertyChanges",
                "viewModelChanges",
                "newBindingErrors",
                "resolvedBindingErrors",
                "validationChanges",
                "focusChange"
            ],
            excludedFields: ["diff"]);

        var contract = GetResponseContract("get_state_diff");
        AssertContractFields(
            contract,
            includedFields: [
                "propertyChanges",
                "viewModelChanges",
                "newBindingErrors",
                "resolvedBindingErrors",
                "validationChanges",
                "focusChange"
            ],
            excludedFields: ["diff"],
            includedPaths: ["propertyChanges[].propertyName", "viewModelChanges[].propertyName", "focusChange.changed"],
            excludedPaths: ["diff.snapshotId"]);
    }

    [Fact]
    public void BatchMutateContracts_ShouldNotAdvertiseMutationOrDiffAliasesAbsentFromRuntimePayload()
    {
        AssertSchemaFields(
            "batch_mutate",
            includedFields: ["mutations", "stateDiff"],
            excludedFields: ["results", "diff"]);

        var contract = GetResponseContract("batch_mutate");
        AssertContractFields(
            contract,
            includedFields: ["mutations", "stateDiff"],
            excludedFields: ["results", "diff"],
            includedPaths: ["mutations[].tool", "mutations[].success", "stateDiff.snapshotId"],
            excludedPaths: ["results[].tool", "results[].success", "diff.snapshotId"]);
    }

    private static void AssertSchemaFields(
        string toolName,
        string[] includedFields,
        string[] excludedFields)
    {
        var tool = new Tool
        {
            Name = toolName,
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
        };

        McpToolOutputSchemas.Apply(tool);
        var properties = tool.OutputSchema!.Value.GetProperty("properties");
        var fields = properties.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        fields.Should().Contain(includedFields);
        fields.Should().NotContain(excludedFields);
    }

    private static JsonElement GetResponseContract(string toolName)
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        return document.RootElement
            .GetProperty("highValueTools")
            .EnumerateArray()
            .Single(tool => tool.GetProperty("tool").GetString() == toolName)
            .Clone();
    }

    private static void AssertContractFields(
        JsonElement contract,
        string[] includedFields,
        string[] excludedFields,
        string[] includedPaths,
        string[] excludedPaths)
    {
        AssertStringArray(contract.GetProperty("topLevelFields"), includedFields, excludedFields);
        AssertStringArray(contract.GetProperty("nestedResponsePaths"), includedPaths, excludedPaths);
    }

    private static void AssertStringArray(
        JsonElement arrayElement,
        string[] includedValues,
        string[] excludedValues)
    {
        var values = arrayElement
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .Where(entry => entry is not null)
            .Cast<string>()
            .ToArray();

        values.Should().Contain(includedValues);
        values.Should().NotContain(excludedValues);
    }
}
