using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolOutputSchemaTests
{
    public static readonly TheoryData<string, string[]> HighValueToolFields = new()
    {
        { "connect", ["processId", "processName", "windowTitle", "autoDiscovered", "candidateCount", "redactedCandidateCount", "suggestedAction"] },
        { "get_processes", ["processes", "redactedTargetCount", "policyEnvVar"] },
        { "get_ui_summary", ["rootElementId", "rootElementType", "rootElementName", "depth", "semanticNodeCount", "summaryText", "nodes"] },
        { "get_element_snapshot", ["elementId", "elementType", "elementName", "dataContextType", "properties", "bindings", "validationErrors", "style", "layout"] },
        { "get_bindings", ["bindings", "results", "resultCount", "successCount", "failureCount"] },
        { "get_binding_errors", ["errorCount", "errors", "navigation", "nextSteps"] },
        { "drain_events", ["pendingEventCount", "droppedEventCount", "cleanupIncomplete", "cleanupFailureMessage", "cleanupFailureType", "pendingEvents"] },
        { "get_form_summary", ["formScope", "scopeVisibility", "isCurrentlyVisible", "inputs", "commands", "summary"] },
        { "capture_state_snapshot", ["snapshotId", "snapshotSummary"] },
        { "get_state_diff", ["snapshotId", "trigger", "propertyChanges", "viewModelChanges", "focusChange"] },
        { "restore_state_snapshot", ["snapshotId", "restoredDependencyProperties", "restoredViewModelProperties", "skippedDependencyProperties", "skippedViewModelProperties"] },
        { "batch_mutate", ["mutations", "mutationCount", "successfulMutationCount", "failedMutationCount", "snapshotId", "stateDiff", "rollback", "recovery"] },
        { "element_screenshot", ["elementId", "screenshotId", "resourceUri", "expiresAtUtc", "outputMode", "width", "height", "mimeType", "base64Image"] }
    };

    [Fact]
    public void StructuredPayloadSchema_ShouldPlaceContextRefsUnderNavigationOnly()
    {
        var tool = new Tool
        {
            Name = "schema_probe",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
        };

        McpToolOutputSchemas.Apply(tool);

        var properties = tool.OutputSchema!.Value.GetProperty("properties");
        properties.TryGetProperty("contextRefs", out _).Should().BeFalse(
            "contextRefs are emitted as navigation.contextRefs in structuredContent, not as a top-level result field");

        var navigation = properties.GetProperty("navigation");
        navigation.GetProperty("type").GetString().Should().Be("object");
        navigation.GetProperty("properties")
            .TryGetProperty("contextRefs", out var contextRefs)
            .Should().BeTrue();
        contextRefs.GetProperty("type").GetString().Should().Be("array");
        var contextRefProperties = contextRefs.GetProperty("items").GetProperty("properties");
        contextRefProperties.TryGetProperty("type", out _).Should().BeTrue();
        contextRefProperties.TryGetProperty("kind", out _).Should().BeFalse(
            "ToolNavigationReference serializes its discriminator as type");

        var nextSteps = properties.GetProperty("nextSteps");
        var nextStepProperties = nextSteps.GetProperty("items").GetProperty("properties");
        nextStepProperties.TryGetProperty("params", out _).Should().BeTrue(
            "ToolNextStep serializes suggested call arguments as params");
        nextStepProperties.TryGetProperty("kind", out _).Should().BeTrue();
        nextStepProperties.TryGetProperty("preconditions", out _).Should().BeTrue();
        nextStepProperties.TryGetProperty("expectedOutcome", out _).Should().BeTrue();
        nextStepProperties.TryGetProperty("workflowId", out _).Should().BeTrue();
        nextStepProperties.TryGetProperty("prefetchTools", out _).Should().BeTrue();
        nextStepProperties.TryGetProperty("args", out _).Should().BeFalse(
            "nextSteps no longer emit the legacy args field");
    }

    [Theory]
    [MemberData(nameof(HighValueToolFields))]
    public void HighValueTools_ShouldExposeToolSpecificClosedOutputSchemas(string toolName, string[] expectedFields)
    {
        var tool = CreateTool(toolName);

        McpToolOutputSchemas.Apply(tool);

        var schema = tool.OutputSchema!.Value;
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(
            $"{toolName} should advertise a tool-specific schema instead of the shared open schema");
        schema.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("success");

        var properties = schema.GetProperty("properties");
        properties.TryGetProperty("success", out _).Should().BeTrue();
        properties.TryGetProperty("structuredContent", out _).Should().BeFalse();
        foreach (var expectedField in expectedFields)
        {
            properties.TryGetProperty(expectedField, out _).Should().BeTrue(
                $"{toolName} should publish its '{expectedField}' structuredContent field in tools/list outputSchema");
        }
    }

    [Theory]
    [MemberData(nameof(HighValueToolFields))]
    public void HighValueTools_ShouldNotExposeArbitraryNestedObjectSchemas(string toolName, string[] _)
    {
        var tool = CreateTool(toolName);

        McpToolOutputSchemas.Apply(tool);

        var violations = new List<string>();
        CollectLooseSchemaViolations(tool.OutputSchema!.Value, "$", violations);

        violations.Should().BeEmpty(
            $"{toolName} is advertised as an exact high-value output schema and should describe nested object and array shapes");
    }

    [Fact]
    public void ExactHighValueTools_ShouldCoverResponseContractTopLevelFields()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var missingByTool = new List<string>();

        foreach (var contract in document.RootElement.GetProperty("highValueTools").EnumerateArray())
        {
            var toolName = contract.GetProperty("tool").GetString()!;
            var tool = CreateTool(toolName);
            McpToolOutputSchemas.Apply(tool);

            var schema = tool.OutputSchema!.Value;
            if (schema.GetProperty("additionalProperties").GetBoolean())
            {
                continue;
            }

            var properties = schema.GetProperty("properties");
            var missing = contract.GetProperty("topLevelFields")
                .EnumerateArray()
                .Select(field => field.GetString())
                .Where(field => field is not null)
                .Cast<string>()
                .Where(field => !properties.TryGetProperty(field, out _))
                .ToArray();

            if (missing.Length > 0)
            {
                missingByTool.Add($"{toolName}: {string.Join(", ", missing)}");
            }
        }

        missingByTool.Should().BeEmpty(
            "closed high-value tools/list outputSchema entries must not omit fields advertised by wpf://contracts/response");
    }

    [Fact]
    public void ResponseContract_ShouldCoverExactHighValueToolSpecificOutputSchemaFields()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var missingByTool = new List<string>();

        foreach (var contract in document.RootElement.GetProperty("highValueTools").EnumerateArray())
        {
            var toolName = contract.GetProperty("tool").GetString()!;
            var tool = CreateTool(toolName);
            McpToolOutputSchemas.Apply(tool);

            var schema = tool.OutputSchema!.Value;
            if (schema.GetProperty("additionalProperties").GetBoolean())
            {
                continue;
            }

            var contractFields = contract.GetProperty("topLevelFields")
                .EnumerateArray()
                .Select(field => field.GetString())
                .Where(field => field is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            var missing = schema.GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .Where(field => !CommonSchemaFields.Contains(field))
                .Where(field => !contractFields.Contains(field))
                .ToArray();

            if (missing.Length > 0)
            {
                missingByTool.Add($"{toolName}: {string.Join(", ", missing)}");
            }
        }

        missingByTool.Should().BeEmpty(
            "wpf://contracts/response must include every tool-specific field advertised by exact tools/list outputSchema entries");
    }

    [Fact]
    public void SpecializedResponseContractTools_ShouldExposeExactClosedOutputSchemas()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var genericTools = new List<string>();

        foreach (var contract in document.RootElement.GetProperty("highValueTools").EnumerateArray())
        {
            var toolName = contract.GetProperty("tool").GetString()!;
            var tool = CreateTool(toolName);
            McpToolOutputSchemas.Apply(tool);

            if (tool.OutputSchema!.Value.GetProperty("additionalProperties").GetBoolean())
            {
                genericTools.Add(toolName);
            }
        }

        genericTools.Should().BeEmpty(
            "tools with specialized response-resource contracts should expose the same exact closed contract through tools/list outputSchema");
    }

    [Fact]
    public void ExactHighValueTools_ShouldCoverKnownRuntimePayloadFields()
    {
        AssertTopLevelFields("connect",
            "hint",
            "reusedExistingHost",
            "connectionSource",
            "targetIsElevated",
            "requiresExplicitTargetOptIn");
        AssertProcessSummaryFields(
            "get_processes",
            "secondaryWindowTitle",
            "dotNetVersion",
            "isElevated",
            "connectionWarning");
        AssertTopLevelFields("get_ui_summary",
            "traversalNodeCount",
            "omittedNodeCount",
            "omittedSemanticNodeCount",
            "truncated",
            "truncationReasons",
            "payloadLimits",
            "navigationNodes");
        AssertNestedFields("get_ui_summary", ["payloadLimits"],
            "maxTraversalNodes",
            "maxSemanticNodes",
            "maxSummaryTextLength",
            "maxStringValueLength");
        AssertTopLevelFields("get_form_summary",
            "traversalNodeCount",
            "omittedNodeCount",
            "omittedInputCount",
            "omittedCommandCount",
            "truncated",
            "truncationReasons",
            "payloadLimits");
        AssertPropertyType("get_form_summary", "formScope", "string");
        AssertArrayItemFields("get_form_summary", "inputs",
            "elementName",
            "currentValue",
            "bindingPath",
            "isEmpty");
        AssertArrayItemFields("get_form_summary", "commands",
            "elementName",
            "text",
            "isPrimary",
            "isReady",
            "blockers");
        AssertNestedFields("get_form_summary", ["payloadLimits"],
            "maxTraversalNodes",
            "maxInputs",
            "maxCommands",
            "maxStringValueLength");
        AssertNestedFields("capture_state_snapshot", ["snapshotSummary"],
            "restorableDependencyPropertyCount",
            "skippedDependencyPropertyCount",
            "capturedFocus");
        AssertPropertyType("restore_state_snapshot", "availableEvents", "array");
        AssertPropertyType("batch_mutate", "availableEvents", "array");
        AssertNestedPropertyType("restore_state_snapshot", ["recovery"], "availableEvents", "array");
        AssertNestedPropertyType("batch_mutate", ["recovery"], "availableEvents", "array");
        AssertTopLevelFields("batch_mutate",
            "executionMode",
            "mutations",
            "stateDiff",
            "requiresReconnect",
            "stateAfterTimeoutUnknown",
            "retryAfterSeconds",
            "retryAfter",
            "availableTokens",
            "availableEvents");
        AssertNestedFields("batch_mutate", ["stateDiff"],
            "success",
            "snapshotId",
            "trigger",
            "propertyChanges",
            "viewModelChanges",
            "newBindingErrors",
            "resolvedBindingErrors",
            "validationChanges",
            "focusChange");
        AssertTopLevelFields("element_screenshot",
            "format",
            "rendered",
            "byteLength",
            "fileName",
            "localPathRedacted",
            "sha256");
    }

    [Fact]
    public void NonHighValueTools_ShouldKeepSharedOpenOutputSchema()
    {
        var tool = CreateTool("schema_probe");

        McpToolOutputSchemas.Apply(tool);

        tool.OutputSchema!.Value.GetProperty("additionalProperties").GetBoolean().Should().BeTrue();
    }

    private static Tool CreateTool(string name)
        => new()
        {
            Name = name,
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
        };

    private static readonly HashSet<string> CommonSchemaFields = new(StringComparer.Ordinal)
    {
        "success",
        "error",
        "errorCode",
        "message",
        "hint",
        "suggestedAction",
        "status",
        "summaryText",
        "navigation",
        "nextSteps",
        "pendingEvents",
        "pendingEventCount",
        "droppedEventCount",
        "pendingEventsOrigin",
        "pendingEventsMayIncludePriorContext",
        "cleanupIncomplete",
        "cleanupFailureMessage",
        "cleanupFailureType",
        "recovery",
        "errorData"
    };

    private static void AssertTopLevelFields(string toolName, params string[] expectedFields)
    {
        var tool = CreateTool(toolName);
        McpToolOutputSchemas.Apply(tool);
        var properties = tool.OutputSchema!.Value.GetProperty("properties");

        foreach (var expectedField in expectedFields)
        {
            properties.TryGetProperty(expectedField, out _).Should().BeTrue(
                $"{toolName} outputSchema should include runtime structuredContent field '{expectedField}'");
        }
    }

    private static void AssertProcessSummaryFields(string toolName, params string[] expectedFields)
    {
        var tool = CreateTool(toolName);
        McpToolOutputSchemas.Apply(tool);
        var processProperties = tool.OutputSchema!.Value
            .GetProperty("properties")
            .GetProperty("processes")
            .GetProperty("items")
            .GetProperty("properties");

        foreach (var expectedField in expectedFields)
        {
            processProperties.TryGetProperty(expectedField, out _).Should().BeTrue(
                $"{toolName} outputSchema process item should include runtime field '{expectedField}'");
        }
    }

    private static void AssertNestedFields(string toolName, string[] path, params string[] expectedFields)
    {
        var schema = CreateToolSchema(toolName);
        var properties = schema.GetProperty("properties");
        foreach (var segment in path)
        {
            properties = properties.GetProperty(segment).GetProperty("properties");
        }

        foreach (var expectedField in expectedFields)
        {
            properties.TryGetProperty(expectedField, out _).Should().BeTrue(
                $"{toolName} outputSchema should include runtime structuredContent path '{string.Join(".", path)}.{expectedField}'");
        }
    }

    private static void AssertPropertyType(string toolName, string fieldName, string expectedType)
    {
        var properties = CreateToolSchema(toolName).GetProperty("properties");
        properties.GetProperty(fieldName).GetProperty("type").GetString().Should().Be(expectedType);
    }

    private static void AssertNestedPropertyType(string toolName, string[] path, string fieldName, string expectedType)
    {
        var properties = CreateToolSchema(toolName).GetProperty("properties");
        foreach (var segment in path)
        {
            properties = properties.GetProperty(segment).GetProperty("properties");
        }

        properties.GetProperty(fieldName).GetProperty("type").GetString().Should().Be(expectedType);
    }

    private static void AssertArrayItemFields(string toolName, string fieldName, params string[] expectedFields)
    {
        var itemProperties = CreateToolSchema(toolName)
            .GetProperty("properties")
            .GetProperty(fieldName)
            .GetProperty("items")
            .GetProperty("properties");

        foreach (var expectedField in expectedFields)
        {
            itemProperties.TryGetProperty(expectedField, out _).Should().BeTrue(
                $"{toolName} outputSchema array '{fieldName}' item should include runtime field '{expectedField}'");
        }
    }

    private static JsonElement CreateToolSchema(string toolName)
    {
        var tool = CreateTool(toolName);
        McpToolOutputSchemas.Apply(tool);
        return tool.OutputSchema!.Value;
    }

    private static void CollectLooseSchemaViolations(JsonElement schema, string path, List<string> violations)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (schema.TryGetProperty("additionalProperties", out var additionalProperties)
            && additionalProperties.ValueKind == JsonValueKind.True)
        {
            violations.Add($"{path}.additionalProperties=true");
        }

        if (schema.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() == "array"
            && schema.TryGetProperty("items", out var items)
            && IsBareObjectSchema(items))
        {
            violations.Add($"{path}.items is a bare object schema");
        }

        foreach (var property in schema.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                CollectLooseSchemaViolations(property.Value, $"{path}.{property.Name}", violations);
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in property.Value.EnumerateArray())
                {
                    CollectLooseSchemaViolations(item, $"{path}.{property.Name}[{index}]", violations);
                    index++;
                }
            }
        }
    }

    private static bool IsBareObjectSchema(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("type", out var type)
            || type.GetString() != "object")
        {
            return false;
        }

        return !schema.TryGetProperty("properties", out _)
            && !schema.TryGetProperty("additionalProperties", out _);
    }
}
