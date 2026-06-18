using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ToolInputExamplesResourceTests
{
    private static readonly string[] RequiredHighRiskTools =
    [
        "batch_mutate",
        "wait_for_dp_change_after_mutation",
        "element_screenshot",
        "capture_state_snapshot",
        "restore_state_snapshot"
    ];

    private static readonly string[] RequiredHighValueTools =
    [
        "connect",
        "get_processes",
        "get_ui_summary",
        "get_form_summary",
        "get_element_snapshot",
        "get_bindings",
        "get_binding_errors",
        "drain_events",
        "capture_state_snapshot",
        "get_state_diff",
        "restore_state_snapshot",
        "batch_mutate",
        "element_screenshot"
    ];

    [Fact]
    public void ToolExamplesResource_ShouldExposeHighRiskToolExamples()
    {
        using var document = ReadToolExamples();
        var root = document.RootElement;
        var examplesByTool = root.GetProperty("examplesByTool");

        root.GetProperty("resourceUri").GetString().Should().Be("wpf://contracts/tool-examples");
        root.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();

        foreach (var toolName in RequiredHighRiskTools)
        {
            examplesByTool.TryGetProperty(toolName, out var examples).Should().BeTrue(
                $"{toolName} should have machine-readable input examples");
            examples.ValueKind.Should().Be(JsonValueKind.Array);
            examples.GetArrayLength().Should().BeInRange(1, 5);

            foreach (var example in examples.EnumerateArray())
            {
                example.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
                example.GetProperty("arguments").ValueKind.Should().Be(JsonValueKind.Object);
                JsonSerializer.Serialize(example).Should().NotContainAny(
                    "password",
                    "secret",
                    "token",
                    "apiKey",
                    "auth");
            }
        }
    }

    [Fact]
    public void ToolExamplesResource_ShouldCoverHighValueResponseContractTools()
    {
        using var document = ReadToolExamples();
        var examplesByTool = document.RootElement.GetProperty("examplesByTool");

        foreach (var toolName in RequiredHighValueTools)
        {
            examplesByTool.TryGetProperty(toolName, out var examples).Should().BeTrue(
                $"{toolName} has an exact high-value output contract and should have machine-readable input examples");
            examples.ValueKind.Should().Be(JsonValueKind.Array);
            examples.GetArrayLength().Should().BeInRange(1, 3);

            foreach (var example in examples.EnumerateArray())
            {
                example.GetProperty("arguments").ValueKind.Should().Be(JsonValueKind.Object);
                using var reparsed = JsonDocument.Parse(JsonSerializer.Serialize(example.GetProperty("arguments")));
                reparsed.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
                JsonSerializer.Serialize(example).Should().NotMatchRegex(
                    "(?i)password|secret|token|apikey|customer");
            }
        }
    }

    [Fact]
    public void ToolExamplesResource_ShouldDescribeScreenshotResourceFollowUp()
    {
        using var document = ReadToolExamples();
        var screenshotExamples = document.RootElement
            .GetProperty("examplesByTool")
            .GetProperty("element_screenshot");

        screenshotExamples.EnumerateArray().Any(HasScreenshotResourceFollowUp)
            .Should().BeTrue();
    }

    [Fact]
    public void ToolExamplesResource_ShouldIncludeBoundDpSnapshotRollbackExample()
    {
        using var document = ReadToolExamples();
        var captureExamples = document.RootElement
            .GetProperty("examplesByTool")
            .GetProperty("capture_state_snapshot");

        captureExamples.EnumerateArray().Any(HasBoundDpSnapshotRollbackExample)
            .Should().BeTrue("agents need a concrete rollback-safe example for two-way bound DependencyProperties");
    }

    [Fact]
    public void ToolExamplesResource_ShouldIncludeErgonomicBatchMutateExamples()
    {
        using var document = ReadToolExamples();
        var batchExamples = document.RootElement
            .GetProperty("examplesByTool")
            .GetProperty("batch_mutate");

        batchExamples.EnumerateArray().Any(HasSequentialRollbackBatchExample)
            .Should().BeTrue("agents need a multi-step rollback-safe batch example with diff capture");
        batchExamples.EnumerateArray().Any(HasStringifiedMutationsExample)
            .Should().BeTrue("text-only clients need a valid stringified mutations example");
    }

    [Fact]
    public void ToolExamplesResource_ShouldOnlyReferenceRegisteredTools()
    {
        using var document = ReadToolExamples();
        var registeredTools = GetRegisteredToolNames();
        var exampleToolNames = document.RootElement
            .GetProperty("examplesByTool")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        exampleToolNames.Should().OnlyContain(name => registeredTools.Contains(name));
    }

    [Fact]
    public void ToolExamplesResource_DrainEventsExamples_ShouldUseAllowedEventTypes()
    {
        using var document = ReadToolExamples();
        var allowedValues = GetAllowedValues(
            typeof(WpfDevTools.Mcp.Server.McpTools.EventDrainMcpTools),
            nameof(WpfDevTools.Mcp.Server.McpTools.EventDrainMcpTools.DrainEvents),
            "eventTypes");

        var eventTypes = document.RootElement
            .GetProperty("examplesByTool")
            .GetProperty("drain_events")
            .EnumerateArray()
            .SelectMany(example => example.GetProperty("arguments")
                .GetProperty("eventTypes")
                .EnumerateArray()
                .Select(item => item.GetString()))
            .Where(item => item is not null)
            .Cast<string>()
            .ToArray();

        eventTypes.Should().OnlyContain(value => allowedValues.Contains(value));
    }

    [Fact]
    public void ServerInstructions_ShouldReferenceToolExamplesResource()
    {
        ServerInstructions.Value.Should().Contain("wpf://contracts/tool-examples");
    }

    private static JsonDocument ReadToolExamples()
    {
        var resource = GetResourceByUri("wpf://contracts/tool-examples");
        var json = resource.Method.Invoke(null, null).Should().BeOfType<string>().Subject;
        return JsonDocument.Parse(json);
    }

    private static (MethodInfo Method, McpServerResourceAttribute Attribute) GetResourceByUri(string uriTemplate)
        => typeof(CapabilityResources).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerResourceTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerResourceAttribute>()))
                .Where(resource => resource.Attribute != null)
                .Select(resource => (resource.Method, Attribute: resource.Attribute!)))
            .Single(resource => resource.Attribute.UriTemplate == uriTemplate);

    private static bool HasScreenshotResourceFollowUp(JsonElement example)
        => example.TryGetProperty("resourceFollowUp", out var followUp)
           && followUp.GetProperty("resourceUriTemplate").GetString() == "wpf://screenshots/{screenshotId}";

    private static bool HasBoundDpSnapshotRollbackExample(JsonElement example)
    {
        var arguments = example.GetProperty("arguments");
        return example.GetProperty("name").GetString()?.Contains("bound", StringComparison.OrdinalIgnoreCase) == true
               && arguments.TryGetProperty("propertyNames", out var propertyNames)
               && propertyNames.EnumerateArray().Any(item => item.GetString() == "Text")
               && arguments.TryGetProperty("viewModelPropertyNames", out var viewModelPropertyNames)
               && viewModelPropertyNames.EnumerateArray().Any(item => item.GetString() == "SearchText")
               && arguments.GetProperty("includeFocus").GetBoolean();
    }

    private static bool HasSequentialRollbackBatchExample(JsonElement example)
    {
        var arguments = example.GetProperty("arguments");
        if (!arguments.TryGetProperty("captureSnapshot", out var captureSnapshot)
            || !arguments.TryGetProperty("includeDiff", out var includeDiff)
            || !includeDiff.GetBoolean()
            || !captureSnapshot.TryGetProperty("viewModelPropertyNames", out var viewModelPropertyNames)
            || !viewModelPropertyNames.EnumerateArray().Any(item => item.GetString() == "SearchText")
            || !arguments.TryGetProperty("mutations", out var mutations)
            || mutations.ValueKind != JsonValueKind.Array
            || mutations.GetArrayLength() < 2)
        {
            return false;
        }

        return mutations.EnumerateArray()
            .Select(mutation => mutation.GetProperty("args"))
            .All(args => !args.TryGetProperty("processId", out _));
    }

    private static bool HasStringifiedMutationsExample(JsonElement example)
    {
        var arguments = example.GetProperty("arguments");
        if (!arguments.TryGetProperty("mutations", out var mutations)
            || mutations.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        using var parsed = JsonDocument.Parse(mutations.GetString()!);
        return parsed.RootElement.ValueKind == JsonValueKind.Array
               && parsed.RootElement.GetArrayLength() > 0;
    }

    private static HashSet<string> GetRegisteredToolNames()
        => typeof(CapabilityResources).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> GetAllowedValues(Type toolType, string methodName, string parameterName)
    {
        var method = toolType.GetMethod(methodName);
        method.Should().NotBeNull();

        var parameter = method!.GetParameters().Single(item => item.Name == parameterName);
        return parameter.GetCustomAttribute<AllowedValuesAttribute>()!.Values
            .Select(value => value?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
