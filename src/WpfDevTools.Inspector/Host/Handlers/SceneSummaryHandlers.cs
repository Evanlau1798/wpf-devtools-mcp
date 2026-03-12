using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Routes scene-level UI and form summary requests.
/// </summary>
public sealed class SceneSummaryHandlers : IRequestHandler
{
    private readonly UiSummaryAnalyzer _uiSummaryAnalyzer;
    private readonly FormSummaryAnalyzer _formSummaryAnalyzer;

    /// <summary>
    /// Create a handler for scene-level UI summary requests.
    /// </summary>
    public SceneSummaryHandlers(UiSummaryAnalyzer uiSummaryAnalyzer, FormSummaryAnalyzer formSummaryAnalyzer)
    {
        _uiSummaryAnalyzer = uiSummaryAnalyzer;
        _formSummaryAnalyzer = formSummaryAnalyzer;
    }

    /// <inheritdoc />
    public IEnumerable<string> GetSupportedMethods()
    {
        return ["get_ui_summary", "get_form_summary"];
    }

    /// <inheritdoc />
    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return method switch
        {
            "get_ui_summary" => _uiSummaryAnalyzer.GetUiSummary(
                ParameterHelpers.GetStringParam(@params, "elementId"),
                ParameterHelpers.GetIntParam(@params, "depth"),
                ParameterHelpers.GetStringParam(@params, "depthMode")),
            "get_form_summary" => _formSummaryAnalyzer.GetFormSummary(
                ParameterHelpers.GetStringParam(@params, "elementId")),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }
}
