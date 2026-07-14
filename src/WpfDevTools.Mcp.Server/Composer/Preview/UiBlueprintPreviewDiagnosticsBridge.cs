using System.Diagnostics;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static class UiBlueprintPreviewDiagnosticsBridge
{
    private const int ToolTimeoutSeconds = 60;
    internal const int ExistingNameLookupLimit = 32;

    internal static async Task<IReadOnlyList<PreviewRuntimeDiagnostic>> CaptureAsync(
        SessionManager sessionManager,
        Process previewProcess,
        bool includeScreenshotDiagnostics,
        string screenshotOutputMode,
        int? screenshotMaxWidth,
        int? screenshotMaxHeight,
        IReadOnlyList<RenderElementCorrelation> elementCorrelations,
        CancellationToken cancellationToken)
    {
        var processId = previewProcess.Id;
        var diagnostics = new List<PreviewRuntimeDiagnostic>();
        var policy = McpToolExecutionPolicy.FromEnvironment();
        PreviewRuntimeDiagnostic? screenshotDiagnostic = null;

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

            var lookupDiagnostics = new List<PreviewRuntimeDiagnostic>();
            foreach (var lookup in BuildCorrelationLookupPlan(elementCorrelations))
            {
                var lookupDiagnostic = await RunGatedAsync(
                    policy,
                    "find_elements",
                    ct => new FindElementsTool(sessionManager).ExecuteAsync(
                        ToolCallHelper.BuildJsonArgs(
                            ("processId", processId),
                            ("query", lookup.Query),
                            ("matchMode", lookup.MatchMode),
                            ("maxResults", 500)),
                        ct),
                    cancellationToken).ConfigureAwait(false);
                diagnostics.Add(lookupDiagnostic);
                lookupDiagnostics.Add(lookupDiagnostic);
            }

            var correlatedElementNames = elementCorrelations
                .Select(item => item.ElementName)
                .ToHashSet(StringComparer.Ordinal);
            var clippingTargets = BuildClippingTargetIds(lookupDiagnostics, correlatedElementNames);
            foreach (var clippingBatch in BuildClippingTargetBatches(clippingTargets))
            {
                var clippingDiagnostic = await RunGatedAsync(
                    policy,
                    "get_clipping_info",
                    ct => new GetClippingInfoTool(sessionManager).ExecuteAsync(
                        ToolCallHelper.BuildJsonArgs(
                            ("processId", processId),
                            ("elementIds", clippingBatch)),
                        ct),
                    cancellationToken).ConfigureAwait(false);
                diagnostics.Add(clippingDiagnostic with { TargetElementIds = clippingBatch });
            }

            diagnostics.Add(await RunGatedAsync(
                policy,
                "get_layout_info",
                ct => new GetLayoutInfoTool(sessionManager).ExecuteAsync(
                    ToolCallHelper.BuildJsonArgs(("processId", processId)),
                    ct),
                cancellationToken).ConfigureAwait(false));

            if (includeScreenshotDiagnostics)
            {
                screenshotDiagnostic = await RunGatedAsync(
                    policy,
                    "element_screenshot",
                    ct => new ElementScreenshotTool(sessionManager).ExecuteAsync(
                        ToolCallHelper.BuildJsonArgs(
                            ("processId", processId),
                            ("outputMode", screenshotOutputMode),
                            ("maxWidth", screenshotMaxWidth),
                            ("maxHeight", screenshotMaxHeight)),
                        ct),
                    cancellationToken).ConfigureAwait(false);
                diagnostics.Add(screenshotDiagnostic);
            }

            return diagnostics;
        }
        finally
        {
            var screenshotId = GetRegisteredFileScreenshotId(screenshotDiagnostic, screenshotOutputMode);
            if (screenshotId is not null)
            {
                sessionManager.DetachScreenshotResource(processId, screenshotId);
            }

            sessionManager.RemoveSession(processId);
        }
    }

    internal static IReadOnlyList<PreviewCorrelationLookup> BuildCorrelationLookupPlan(
        IReadOnlyList<RenderElementCorrelation> correlations)
    {
        var plan = new List<PreviewCorrelationLookup>();
        if (correlations.Any(item => item.ElementName.StartsWith("WpfDevToolsBp_", StringComparison.Ordinal)))
        {
            plan.Add(new PreviewCorrelationLookup("WpfDevToolsBp_", "contains"));
        }

        plan.AddRange(correlations
            .Select(item => item.ElementName)
            .Where(name => !name.StartsWith("WpfDevToolsBp_", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(ExistingNameLookupLimit)
            .Select(name => new PreviewCorrelationLookup(name, "exact")));
        return plan;
    }

    internal static IReadOnlyList<string> BuildClippingTargetIds(
        IReadOnlyList<PreviewRuntimeDiagnostic> diagnostics,
        IReadOnlySet<string>? correlatedElementNames = null)
        => diagnostics
            .Where(diagnostic => diagnostic.Tool == "find_elements" && diagnostic.Success)
            .SelectMany(diagnostic => ReadSearchResults(diagnostic.Payload))
            .Where(result => correlatedElementNames is null
                             || result.TryGetProperty("elementName", out var elementName)
                             && elementName.ValueKind == JsonValueKind.String
                             && correlatedElementNames.Contains(elementName.GetString()!))
            .Select(result => result.GetProperty("elementId").GetString())
            .Where(elementId => !string.IsNullOrWhiteSpace(elementId))
            .Select(elementId => elementId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    internal static IReadOnlyList<IReadOnlyList<string>> BuildClippingTargetBatches(
        IReadOnlyList<string> targetIds)
        => targetIds
            .Chunk(BatchItemLimits.MaxQueryInputItems)
            .Select(batch => (IReadOnlyList<string>)batch)
            .ToArray();

    private static IEnumerable<JsonElement> ReadSearchResults(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Object
           && payload.TryGetProperty("results", out var results)
           && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray().Where(result =>
                result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("elementId", out var elementId)
                && elementId.ValueKind == JsonValueKind.String)
            : [];

    private static string? GetRegisteredFileScreenshotId(
        PreviewRuntimeDiagnostic? diagnostic,
        string screenshotOutputMode)
    {
        if (!string.Equals(screenshotOutputMode, "file", StringComparison.OrdinalIgnoreCase)
            || diagnostic is not { Success: true }
            || !diagnostic.Payload.TryGetProperty("screenshotId", out var screenshotIdProperty)
            || !diagnostic.Payload.TryGetProperty("resourceUri", out var resourceUriProperty))
        {
            return null;
        }

        var screenshotId = screenshotIdProperty.GetString();
        return string.IsNullOrWhiteSpace(screenshotId)
               || resourceUriProperty.GetString() != $"wpf://screenshots/{screenshotId}"
            ? null
            : screenshotId;
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
