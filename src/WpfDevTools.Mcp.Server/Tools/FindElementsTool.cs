using System.Text.Json;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed class FindElementsTool : PipeConnectedToolBase
{
    private static readonly HashSet<string> AllowedArgumentNames = new(StringComparer.Ordinal)
    {
        "processId",
        "elementId",
        "typeName",
        "typeNames",
        "elementName",
        "automationId",
        "propertyName",
        "propertyValue",
        "maxResults",
        "maxTraversalNodes",
        "matchMode"
    };

    public FindElementsTool(SessionManager sessionManager) : base(sessionManager)
    {
    }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;

        var unknownArgumentError = ValidateKnownArguments(arguments);
        if (unknownArgumentError != null) return unknownArgumentError;

        var typeName = ParseStringParam(arguments, "typeName");
        var typeNames = ParseStringArrayParam(arguments, "typeNames");
        var elementName = ParseStringParam(arguments, "elementName");
        var automationId = ParseStringParam(arguments, "automationId");
        var propertyName = ParseStringParam(arguments, "propertyName");
        var propertyValue = ParseStringParam(arguments, "propertyValue");
        var maxResults = ParseIntParam(arguments, "maxResults");
        var maxTraversalNodes = ParseIntParam(arguments, "maxTraversalNodes");
        var matchMode = ParseStringParam(arguments, "matchMode");

        return await SendInspectorRequestAsync(
            processId,
            "find_elements",
            new
            {
                elementId,
                typeName,
                typeNames,
                elementName,
                automationId,
                propertyName,
                propertyValue,
                maxResults,
                maxTraversalNodes,
                matchMode
            },
            cancellationToken);
    }

    private static ToolErrorPayload? ValidateKnownArguments(JsonElement? arguments)
    {
        if (!arguments.HasValue || arguments.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in arguments.Value.EnumerateObject())
        {
            if (AllowedArgumentNames.Contains(property.Name))
            {
                continue;
            }

            return property.NameEquals("nameFilter")
                ? CreateUnknownNameFilterError()
                : CreateUnknownArgumentError(property.Name);
        }

        return null;
    }

    private static ToolErrorPayload CreateUnknownNameFilterError() =>
        new()
        {
            Error = "Unknown argument 'nameFilter' for find_elements. Use 'elementName' for FrameworkElement.Name or 'automationId' for AutomationProperties.AutomationId; 'nameFilter' is only supported by get_processes.",
            ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
            Hint = "For named WPF controls, call find_elements with elementName. Use get_processes(nameFilter) only when filtering process names before connecting."
        };

    private static ToolErrorPayload CreateUnknownArgumentError(string argumentName) =>
        new()
        {
            Error = $"Unknown argument '{argumentName}' for find_elements.",
            ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
            Hint = "Use one of: processId, elementId, typeName, typeNames, elementName, automationId, propertyName, propertyValue, maxResults, maxTraversalNodes, matchMode."
        };
}
