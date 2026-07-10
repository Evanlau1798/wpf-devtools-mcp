using System.Text.Json;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed class FindElementsTool : PipeConnectedToolBase
{
    public FindElementsTool(SessionManager sessionManager) : base(sessionManager)
    {
    }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;

        var unknownArgumentError = McpToolArgumentValidator.ValidateFindElementsArguments(arguments);
        if (unknownArgumentError != null) return unknownArgumentError;

        var query = ParseStringParam(arguments, "query");
        var typeName = ParseStringParam(arguments, "typeName")
            ?? ParseStringParam(arguments, "controlType");
        var typeNames = ParseStringArrayParam(arguments, "typeNames");
        var elementName = ParseStringParam(arguments, "elementName");
        var automationId = ParseStringParam(arguments, "automationId");
        var propertyName = ParseStringParam(arguments, "propertyName");
        var propertyValue = ParseStringParam(arguments, "propertyValue");
        var maxResults = ParseIntParam(arguments, "maxResults");
        var maxTraversalNodes = ParseIntParam(arguments, "maxTraversalNodes");
        var matchMode = ParseStringParam(arguments, "matchMode");
        var typeMatchMode = ParseStringParam(arguments, "typeMatchMode");

        return await SendInspectorRequestAsync(
            processId,
            "find_elements",
            new
            {
                elementId,
                query,
                typeName,
                typeNames,
                elementName,
                automationId,
                propertyName,
                propertyValue,
                maxResults,
                maxTraversalNodes,
                matchMode,
                typeMatchMode
            },
            cancellationToken);
    }
}
