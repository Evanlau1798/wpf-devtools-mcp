using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolInputConstraintTests
{
    [Fact]
    public void HighValueToolInputSchemas_ShouldExposeParameterConstraints()
    {
        var getProcessesSchema = CreateInputSchema(typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses));
        AssertEnumConstraint(getProcessesSchema, "windowFilter", "visible", "all", "foreground");

        var connectSchema = CreateInputSchema(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect));
        AssertIntegerConstraint(connectSchema, "processId", minimum: 1, maximum: int.MaxValue);
        AssertEnumConstraint(connectSchema, "selectionStrategy", "single_only", "largest_working_set");
        AssertEnumConstraint(connectSchema, "windowFilter", "visible", "all", "foreground");

        var visualTreeSchema = CreateInputSchema(typeof(TreeMcpTools), nameof(TreeMcpTools.GetVisualTree));
        AssertTreeConstraints(visualTreeSchema);
        var logicalTreeSchema = CreateInputSchema(typeof(TreeMcpTools), nameof(TreeMcpTools.GetLogicalTree));
        AssertTreeConstraints(logicalTreeSchema);

        var findElementsSchema = CreateInputSchema(typeof(TreeMcpTools), nameof(TreeMcpTools.FindElements));
        AssertIntegerConstraint(findElementsSchema, "maxTraversalNodes", minimum: 1, maximum: 10000);
        AssertStringMaxLength(findElementsSchema, "propertyName", 256);
        AssertEnumConstraint(findElementsSchema, "typeMatchMode", "exact", "assignable");

        var uiSummarySchema = CreateInputSchema(typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetUiSummary));
        AssertIntegerConstraint(uiSummarySchema, "depth", minimum: 0, maximum: 100);
        AssertEnumConstraint(uiSummarySchema, "depthMode", "semantic", "visual");

        var screenshotSchema = CreateInputSchema(typeof(InteractionMcpTools), nameof(InteractionMcpTools.ElementScreenshot));
        AssertEnumConstraint(screenshotSchema, "outputMode", "metadata", "file", "base64");
        var screenshotOutputModeDescription = GetSchemaProperty(screenshotSchema, "outputMode")
            .GetProperty("description")
            .GetString();
        screenshotOutputModeDescription.Should().Contain("does not render or return pixel bytes");
        screenshotOutputModeDescription.Should().Contain("rendered=false");
        screenshotOutputModeDescription.Should().Contain("file");
        screenshotOutputModeDescription.Should().Contain("resources/read");
        screenshotOutputModeDescription.Should().Contain("pixel evidence");
        AssertIntegerConstraint(screenshotSchema, "maxWidth", minimum: 1, maximum: int.MaxValue);
        AssertIntegerConstraint(screenshotSchema, "maxHeight", minimum: 1, maximum: int.MaxValue);

        var composerBlueprintMethods = typeof(UiComposerMcpTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.GetParameters().Any(parameter => parameter.Name == "blueprintJson"))
            .ToArray();
        composerBlueprintMethods.Should().NotBeEmpty();
        foreach (var method in composerBlueprintMethods)
        {
            AssertStringMaxLength(
                CreateInputSchema(typeof(UiComposerMcpTools), method.Name),
                "blueprintJson",
                BoundaryStringLimits.MaxStringifiedJsonArgumentLength);
        }

        var previewSchema = CreateInputSchema(typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.PreviewUiBlueprint));
        AssertEnumConstraint(previewSchema, "screenshotOutputMode", "metadata", "file");
        AssertIntegerConstraint(previewSchema, "screenshotMaxWidth", minimum: 1, maximum: int.MaxValue);
        AssertIntegerConstraint(previewSchema, "screenshotMaxHeight", minimum: 1, maximum: int.MaxValue);
    }

    private static void AssertTreeConstraints(JsonElement schema)
    {
        AssertIntegerConstraint(schema, "depth", minimum: 0, maximum: 100);
        AssertIntegerConstraint(schema, "maxNodes", minimum: 1, maximum: 10000);
        AssertIntegerConstraint(schema, "maxChildrenPerNode", minimum: 1, maximum: 1000);
    }

    private static JsonElement CreateInputSchema(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName);
        method.Should().NotBeNull();
        using var services = new ServiceCollection()
            .AddSingleton<SessionManager>(_ => throw new InvalidOperationException("Schema tests do not invoke tools."))
            .BuildServiceProvider();
        return McpServerTool.Create(
            method!,
            target: null,
            new McpServerToolCreateOptions { Services = services }).ProtocolTool.InputSchema;
    }

    private static void AssertIntegerConstraint(JsonElement schema, string parameterName, int? minimum, int? maximum)
    {
        var parameter = GetSchemaProperty(schema, parameterName);
        AssertSchemaTypeContains(parameter.GetProperty("type"), "integer");
        AssertNullableIntSchemaKeyword(parameter, "minimum", minimum);
        AssertNullableIntSchemaKeyword(parameter, "maximum", maximum);
    }

    private static void AssertEnumConstraint(JsonElement schema, string parameterName, params string[] expectedValues)
    {
        GetSchemaProperty(schema, parameterName).GetProperty("enum").EnumerateArray()
            .Select(value => value.GetString())
            .Should().BeEquivalentTo(expectedValues);
    }

    private static void AssertStringMaxLength(JsonElement schema, string parameterName, int maxLength)
    {
        var parameter = GetSchemaProperty(schema, parameterName);
        AssertSchemaTypeContains(parameter.GetProperty("type"), "string");
        parameter.GetProperty("maxLength").GetInt32().Should().Be(maxLength);
    }

    private static JsonElement GetSchemaProperty(JsonElement schema, string parameterName)
        => schema.GetProperty("properties").GetProperty(parameterName);

    private static void AssertSchemaTypeContains(JsonElement typeElement, string expectedType)
    {
        if (typeElement.ValueKind == JsonValueKind.String)
        {
            typeElement.GetString().Should().Be(expectedType);
            return;
        }

        typeElement.EnumerateArray().Select(type => type.GetString()).Should().Contain(expectedType);
    }

    private static void AssertNullableIntSchemaKeyword(JsonElement property, string keyword, int? expectedValue)
    {
        if (!expectedValue.HasValue)
        {
            property.TryGetProperty(keyword, out _).Should().BeFalse();
            return;
        }

        property.GetProperty(keyword).GetInt32().Should().Be(expectedValue.Value);
    }
}
