using System.Diagnostics;
using System.Text.Json;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static class UiBlueprintPreviewDiagnosticsBridge
{
    private const int ToolTimeoutSeconds = 60;

    internal static async Task<IReadOnlyList<PreviewRuntimeDiagnostic>> CaptureAsync(
        SessionManager sessionManager,
        Process previewProcess,
        bool includeScreenshotDiagnostics,
        CancellationToken cancellationToken)
    {
        var processId = previewProcess.Id;
        var diagnostics = new List<PreviewRuntimeDiagnostic>();
        var policy = McpToolExecutionPolicy.FromEnvironment();

        try
        {
            var connect = await RunAsync(
                "connect",
                ct => ConnectExistingPreviewHostAsync(sessionManager, previewProcess, ct),
                cancellationToken)
                .ConfigureAwait(false);
            diagnostics.Add(connect);
            if (!connect.Success)
            {
                return diagnostics;
            }

            diagnostics.Add(await RunGatedAsync(
                policy,
                "get_ui_summary",
                ct => new GetUiSummaryTool(sessionManager).ExecuteAsync(
                    ToolCallHelper.BuildJsonArgs(
                        ("processId", processId),
                        ("depthMode", "semantic")),
                    ct),
                cancellationToken).ConfigureAwait(false));

            diagnostics.Add(await RunGatedAsync(
                policy,
                "get_layout_info",
                ct => new GetLayoutInfoTool(sessionManager).ExecuteAsync(
                    ToolCallHelper.BuildJsonArgs(("processId", processId)),
                    ct),
                cancellationToken).ConfigureAwait(false));

            if (includeScreenshotDiagnostics)
            {
                diagnostics.Add(await RunGatedAsync(
                    policy,
                    "element_screenshot",
                    ct => new ElementScreenshotTool(sessionManager).ExecuteAsync(
                        ToolCallHelper.BuildJsonArgs(
                            ("processId", processId),
                            ("outputMode", "metadata")),
                        ct),
                    cancellationToken).ConfigureAwait(false));
            }

            return diagnostics;
        }
        finally
        {
            sessionManager.RemoveSession(processId);
        }
    }

    private static async Task<object> ConnectExistingPreviewHostAsync(
        SessionManager sessionManager,
        Process previewProcess,
        CancellationToken cancellationToken)
    {
        var processId = previewProcess.Id;
        try
        {
            if (previewProcess.HasExited)
            {
                return FailurePayload(
                    "PreviewHostExited",
                    "Preview host exited before runtime diagnostics could connect.");
            }

            var failure = await sessionManager.ConnectExistingHostSessionAsync(
                processId,
                TimeSpan.FromSeconds(ToolTimeoutSeconds),
                cancellationToken).ConfigureAwait(false);
            if (failure != NamedPipeConnectFailure.None)
            {
                var described = ConnectTool.DescribePipeConnectFailure(failure, processId);
                return new
                {
                    success = false,
                    error = described.Error,
                    errorCode = described.ErrorCode,
                    hint = described.Hint
                };
            }

            return new
            {
                success = true,
                message = "Connected to preview Inspector host.",
                processId,
                connectionSource = "sdk-hosted-inspector",
                reusedExistingHost = true
            };
        }
        catch (InvalidOperationException)
        {
            return FailurePayload(
                "PreviewHostUnavailable",
                "Preview host process was not available for runtime diagnostics.");
        }
    }

    private static async Task<PreviewRuntimeDiagnostic> RunGatedAsync(
        McpToolExecutionPolicy policy,
        string tool,
        Func<CancellationToken, Task<object>> execute,
        CancellationToken cancellationToken)
    {
        var decision = policy.EvaluateToolCall(tool);
        return decision.IsAllowed
            ? await RunAsync(tool, execute, cancellationToken).ConfigureAwait(false)
            : new PreviewRuntimeDiagnostic(tool, Success: false, PolicyPayload(decision));
    }

    private static async Task<PreviewRuntimeDiagnostic> RunAsync(
        string tool,
        Func<CancellationToken, Task<object>> execute,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(ToolTimeoutSeconds));
        try
        {
            var payload = ToPayload(await execute(timeout.Token).ConfigureAwait(false));
            return new PreviewRuntimeDiagnostic(tool, IsSuccess(payload), payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new PreviewRuntimeDiagnostic(
                tool,
                Success: false,
                FailurePayload(
                    "PreviewDiagnosticTimeout",
                    $"Preview runtime diagnostic '{tool}' timed out."));
        }
        catch (Exception ex)
        {
            return new PreviewRuntimeDiagnostic(
                tool,
                Success: false,
                FailurePayload(
                    "PreviewDiagnosticFailed",
                    $"Preview runtime diagnostic '{tool}' failed before completion.",
                    ex));
        }
    }

    private static JsonElement ToPayload(object result)
        => result is JsonElement element
            ? element.Clone()
            : JsonSerializer.SerializeToElement(result);

    private static bool IsSuccess(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Object
           && payload.TryGetProperty("success", out var success)
           && success.ValueKind == JsonValueKind.True;

    private static JsonElement PolicyPayload(McpToolPolicyDecision decision)
        => JsonSerializer.SerializeToElement(new
        {
            success = false,
            error = decision.Error,
            errorCode = decision.ErrorCode,
            hint = decision.Hint,
            suggestedAction = decision.SuggestedAction
        });

    private static JsonElement FailurePayload(string errorCode, string error, Exception? exception = null)
        => JsonSerializer.SerializeToElement(new
        {
            success = false,
            error,
            errorCode,
            exceptionType = exception?.GetType().FullName
        });
}
