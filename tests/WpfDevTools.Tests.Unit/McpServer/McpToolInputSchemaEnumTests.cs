using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolInputSchemaEnumTests
{
    [Theory]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.InteractionMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.InteractionMcpTools.ClickElement), "detail", "compact", "minimal", "verbose", "standard")]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.MvvmMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.MvvmMcpTools.ExecuteCommand), "detail", "compact", "minimal", "verbose", "standard")]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.MvvmMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.MvvmMcpTools.ModifyViewModel), "detail", "compact", "minimal", "verbose", "standard")]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.BindingMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.BindingMcpTools.ForceBindingUpdate), "direction", "Source", "Target")]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.BindingMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.BindingMcpTools.GetBindings), "statusFilter", "All", "Active", "Error")]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.DependencyPropertyMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.DependencyPropertyMcpTools.SetDpValue), "detail", "compact", "minimal", "verbose", "standard")]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.DependencyPropertyMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.DependencyPropertyMcpTools.ClearDpValue), "detail", "compact", "minimal", "verbose", "standard")]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.EventMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.EventMcpTools.FireRoutedEvent), "detail", "compact", "minimal", "verbose", "standard")]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.EventMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.EventMcpTools.TraceRoutedEvents), "mode", "capture", "start", "get")]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.TreeMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.TreeMcpTools.FindElements), "matchMode", "exact", "contains")]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.InteractionMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.InteractionMcpTools.SimulateKeyboard), "eventType", "KeyDown", "KeyUp")]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.StyleMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.StyleMcpTools.OverrideStyleSetter), "detail", "compact", "minimal", "verbose", "standard")]
    public void ClosedStringParameters_ShouldPublishInputSchemaEnums(
        Type toolType,
        string methodName,
        string parameterName,
        params string[] expectedValues)
    {
        var schema = CreateInputSchema(toolType, methodName);

        AssertEnumConstraint(schema, parameterName, expectedValues);
    }

    [Fact]
    public void DrainEventsEventTypes_ShouldPublishArrayItemVocabulary()
    {
        string[] expectedValues = ["all", "DpChange", "RoutedEvent", "BindingError", "ValidationChange"];
        var schema = CreateInputSchema(
            typeof(WpfDevTools.Mcp.Server.McpTools.EventDrainMcpTools),
            nameof(WpfDevTools.Mcp.Server.McpTools.EventDrainMcpTools.DrainEvents));
        var eventTypes = schema.GetProperty("properties").GetProperty("eventTypes");

        AssertSchemaTypeContains(eventTypes.GetProperty("type"), "array");
        AssertSchemaTypeContains(eventTypes.GetProperty("items").GetProperty("type"), "string");
        AssertEnumConstraint(schema, "eventTypes", expectedValues);

        var contractJson = JsonSerializer.SerializeToElement(ResponseContractParameterVocabularies.GetParameterVocabularies());
        var eventTypesVocabulary = contractJson.EnumerateArray()
            .Single(item => item.GetProperty("parameter").GetString() == "eventTypes");
        eventTypesVocabulary.GetProperty("allowedValues")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Should().BeEquivalentTo(expectedValues);
    }

    [Fact]
    public void Apply_WhenEnumContainsWholeArrays_ShouldPreserveArrayVocabulary()
    {
        var tool = CreateSyntheticTool(new
        {
            type = "array",
            @enum = new[] { new[] { "compact" }, new[] { "verbose" } },
            items = new { type = "string" }
        });

        McpToolInputSchemaNormalizer.Apply(tool);

        var parameter = tool.InputSchema.GetProperty("properties").GetProperty("value");
        parameter.GetProperty("enum")[0][0].GetString().Should().Be("compact");
        parameter.GetProperty("enum")[1][0].GetString().Should().Be("verbose");
        parameter.GetProperty("items").TryGetProperty("enum", out _).Should().BeFalse();
    }

    [Fact]
    public void Apply_WhenItemsAlreadyDefineEnum_ShouldNotOverwriteItemVocabulary()
    {
        var tool = CreateSyntheticTool(new
        {
            type = "array",
            @enum = new[] { "misplaced" },
            items = new { type = "string", @enum = new[] { "compact", "verbose" } }
        });

        McpToolInputSchemaNormalizer.Apply(tool);

        tool.InputSchema.GetProperty("properties").GetProperty("value")
            .GetProperty("items").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString())
            .Should().Equal("compact", "verbose");
    }

    private static JsonElement CreateInputSchema(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName);
        method.Should().NotBeNull();

        using var services = new ServiceCollection()
            .AddSingleton<SessionManager>(_ => throw new InvalidOperationException("Schema tests do not invoke tools."))
            .BuildServiceProvider();
        var tool = McpServerTool.Create(
            method!,
            target: null,
            new McpServerToolCreateOptions { Services = services }).ProtocolTool;
        McpToolInputSchemaNormalizer.Apply(tool);
        return tool.InputSchema;
    }

    private static Tool CreateSyntheticTool(object parameterSchema)
        => new()
        {
            Name = "synthetic",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { value = parameterSchema }
            })
        };

    private static void AssertEnumConstraint(JsonElement schema, string parameterName, string[] expectedValues)
    {
        var parameterSchema = schema.GetProperty("properties").GetProperty(parameterName);
        var enumSource = parameterSchema.TryGetProperty("items", out var items)
            ? items
            : parameterSchema;
        var values = enumSource.GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        values.Should().BeEquivalentTo(expectedValues);
    }

    private static void AssertSchemaTypeContains(JsonElement typeElement, string expectedType)
    {
        if (typeElement.ValueKind == JsonValueKind.String)
        {
            typeElement.GetString().Should().Be(expectedType);
            return;
        }

        typeElement.EnumerateArray().Select(type => type.GetString()).Should().Contain(expectedType);
    }
}
