using System.Text.Json;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

internal delegate Task<object> BatchMutationExecutor(
    string toolName,
    JsonElement args,
    CancellationToken cancellationToken);

internal delegate Task<object> BatchJsonExecutor(JsonElement args, CancellationToken cancellationToken);

public sealed partial class BatchMutateTool : PipeConnectedToolBase
{
    private readonly BatchMutationExecutor _mutationExecutor;
    private readonly BatchJsonExecutor _snapshotExecutor;
    private readonly BatchJsonExecutor _stateDiffExecutor;

    public BatchMutateTool(SessionManager sessionManager)
        : this(
            sessionManager,
            null,
            null,
            null)
    {
    }

    internal BatchMutateTool(
        SessionManager sessionManager,
        BatchMutationExecutor? mutationExecutor,
        BatchJsonExecutor? snapshotExecutor,
        BatchJsonExecutor? stateDiffExecutor)
        : base(sessionManager)
    {
        _mutationExecutor = mutationExecutor ?? ExecuteMutationAsync;
        _snapshotExecutor = snapshotExecutor ?? ExecuteCaptureSnapshotAsync;
        _stateDiffExecutor = stateDiffExecutor ?? ExecuteStateDiffAsync;
    }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        var parseResult = ParseRequest(arguments, processId, elementId);
        if (parseResult.error != null)
        {
            return parseResult.error;
        }

        var request = parseResult.request!;
        string? snapshotId = null;

        if (request.CaptureSnapshot is not null)
        {
            var captureResult = ToJsonElement(await _snapshotExecutor(
                BuildSnapshotArgs(request),
                cancellationToken).ConfigureAwait(false));
            if (!IsSuccess(captureResult))
            {
                return CreateStepFailure("capture_state_snapshot", captureResult);
            }

            snapshotId = GetOptionalString(captureResult, "snapshotId");
        }

        var mutationResults = new List<object>(request.Mutations.Count);
        var executedMutationCount = 0;
        var successfulMutationCount = 0;
        var failedMutationCount = 0;
        var skippedMutationCount = 0;
        var stopExecution = false;
        var stateAfterTimeoutUnknown = false;
        var requiresReconnect = false;
        string? firstFailureContext = null;
        string? firstFailureError = null;
        string? firstFailureErrorCode = null;
        ToolRecoveryProjection? firstFailureRecovery = null;

        foreach (var mutation in request.Mutations)
        {
            if (stopExecution)
            {
                skippedMutationCount++;
                mutationResults.Add(CreateSkippedMutationResult(mutation));
                continue;
            }

            JsonElement mutationResult;
            try
            {
                mutationResult = ToJsonElement(await _mutationExecutor(
                    mutation.Tool,
                    BuildMutationArgs(request, mutation),
                    cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (IsRecoverableTimeoutAfterSnapshot(ex, snapshotId))
            {
                executedMutationCount++;
                failedMutationCount++;
                stateAfterTimeoutUnknown = true;
                requiresReconnect = true;
                stopExecution = true;
                firstFailureContext ??= GetOptionalString(mutation.Args, "propertyName")
                    ?? mutation.Label
                    ?? mutation.Tool;
                firstFailureError ??= "The mutation was canceled or timed out before a response was returned.";
                firstFailureErrorCode ??= "Timeout";

                mutationResults.Add(CreateTimeoutMutationResult(mutation, firstFailureError));
                continue;
            }

            executedMutationCount++;
            var mutationSucceeded = IsSuccess(mutationResult);
            var mutationRecovery = !mutationSucceeded
                ? ToolRecoveryPayload.Extract(mutationResult)
                : null;
            var mutationHasRecovery = !mutationSucceeded && ToolRecoveryPayload.HasRecoveryGuidance(mutationResult);
            var mutationTimeoutOrTransport = !mutationSucceeded && ToolRecoveryPayload.IsTimeoutOrTransportRecovery(mutationResult);
            var mutationProjection = mutationHasRecovery || mutationTimeoutOrTransport
                ? mutationRecovery
                : null;
            var mutationTimedOutWithUnknownState = mutationTimeoutOrTransport
                && (mutationRecovery?.StateAfterTimeoutUnknown ?? true);
            if (mutationSucceeded)
            {
                successfulMutationCount++;
            }
            else
            {
                failedMutationCount++;
                firstFailureContext ??= GetOptionalString(mutation.Args, "propertyName")
                    ?? mutation.Label
                    ?? mutation.Tool;
                firstFailureError ??= GetOptionalString(mutationResult, "error");
                firstFailureErrorCode ??= mutationProjection?.ErrorCode;
                if (mutationProjection != null)
                {
                    firstFailureRecovery ??= mutationProjection;
                    if (mutationTimeoutOrTransport)
                    {
                        stateAfterTimeoutUnknown = mutationProjection.StateAfterTimeoutUnknown ?? true;
                        requiresReconnect = mutationProjection.RequiresReconnect ?? true;
                    }
                    else
                    {
                        stateAfterTimeoutUnknown |= mutationProjection.StateAfterTimeoutUnknown == true;
                        requiresReconnect |= mutationProjection.RequiresReconnect == true;
                    }

                    firstFailureError ??= mutationProjection.Error
                        ?? "The mutation reported that runtime state may be partially changed.";
                }

                stopExecution = true;
            }

            mutationResults.Add(new
            {
                index = mutation.Index,
                tool = mutation.Tool,
                label = mutation.Label,
                success = mutationSucceeded,
                skipped = false,
                error = mutationSucceeded ? null : GetOptionalString(mutationResult, "error"),
                errorCode = mutationSucceeded ? null : GetOptionalString(mutationResult, "errorCode"),
                stateAfterTimeoutUnknown = mutationTimedOutWithUnknownState ? true : (bool?)null,
                result = mutationResult.Clone()
            });
        }

        object? stateDiff = null;
        if (request.IncludeDiff && failedMutationCount == 0 && !string.IsNullOrWhiteSpace(snapshotId))
        {
            try
            {
                var diffResult = ToJsonElement(await _stateDiffExecutor(
                    JsonSerializer.SerializeToElement(new
                    {
                        processId = request.ProcessId,
                        snapshotId,
                        trigger = request.DiffTrigger
                    }),
                    cancellationToken).ConfigureAwait(false));
                stateDiff = diffResult.Clone();
                if (!IsSuccess(diffResult))
                {
                    var recovery = ToolRecoveryPayload.Extract(diffResult);
                    var diffHasRecovery = ToolRecoveryPayload.HasRecoveryGuidance(diffResult);
                    var diffTimeoutOrTransport = ToolRecoveryPayload.IsTimeoutOrTransportRecovery(diffResult);
                    var diffProjection = diffHasRecovery || diffTimeoutOrTransport
                        ? recovery
                        : null;
                    if (diffTimeoutOrTransport)
                    {
                        stateAfterTimeoutUnknown = recovery.StateAfterTimeoutUnknown ?? true;
                        requiresReconnect = recovery.RequiresReconnect ?? true;
                    }
                    else
                    {
                        stateAfterTimeoutUnknown |= recovery.StateAfterTimeoutUnknown == true;
                        requiresReconnect |= recovery.RequiresReconnect == true;
                    }

                    firstFailureContext ??= "get_state_diff";
                    firstFailureError ??= diffProjection?.Error ?? GetOptionalString(diffResult, "error");
                    firstFailureErrorCode ??= diffProjection?.ErrorCode;
                    if (diffProjection != null)
                    {
                        firstFailureRecovery ??= diffProjection;
                    }
                }
            }
            catch (Exception ex) when (IsRecoverableTimeoutAfterSnapshot(ex, snapshotId))
            {
                stateAfterTimeoutUnknown = true;
                requiresReconnect = true;
                firstFailureContext ??= "get_state_diff";
                firstFailureError ??= "State diff was canceled or timed out before a response was returned.";
                firstFailureErrorCode ??= "Timeout";
            }
        }

        var diffFailed = stateDiff is JsonElement diffPayload && !IsSuccess(diffPayload);
        var overallSuccess = failedMutationCount == 0
            && !diffFailed
            && !stateAfterTimeoutUnknown;
        var rollback = BuildRollback(processId, snapshotId, !overallSuccess);
        var failureErrorCode = firstFailureErrorCode
            ?? (stateAfterTimeoutUnknown ? "Timeout" : failedMutationCount > 0 ? "BatchStepFailed" : "DiffFailed");
        var failure = overallSuccess
            ? null
            : BuildBatchFailure(
                processId,
                snapshotId,
                failureErrorCode,
                stateAfterTimeoutUnknown
                    ? $"batch_mutate was canceled or timed out while executing {firstFailureContext}. {firstFailureError}".Trim()
                    : failedMutationCount > 0
                    ? $"Batch mutation step failed for {firstFailureContext}. {firstFailureError}".Trim()
                    : $"batch_mutate failed while running get_state_diff. {firstFailureError ?? GetOptionalString((JsonElement)stateDiff!, "error")}".Trim(),
                firstFailureRecovery);

        return new
        {
            success = overallSuccess,
            error = failure?.Error,
            errorCode = failure?.ErrorCode,
            hint = failure?.Projection?.Hint,
            suggestedAction = failure?.Projection?.SuggestedAction,
            requiresReconnect = requiresReconnect ? true : failure?.Projection?.RequiresReconnect,
            stateAfterTimeoutUnknown = stateAfterTimeoutUnknown ? true : failure?.Projection?.StateAfterTimeoutUnknown,
            processId = failure?.Projection?.ProcessId,
            timeoutSeconds = failure?.Projection?.TimeoutSeconds,
            retryAfterSeconds = failure?.Projection?.RetryAfterSeconds,
            retryAfter = failure?.Projection?.RetryAfter,
            availableTokens = failure?.Projection?.AvailableTokens,
            availableEvents = failure?.Projection?.AvailableEvents,
            recovery = failure?.Recovery,
            executionMode = "sequential-stop-on-error",
            mutationCount = request.Mutations.Count,
            executedMutationCount,
            successfulMutationCount,
            failedMutationCount,
            skippedMutationCount,
            snapshotId,
            stateDiff,
            rollback,
            mutations = mutationResults
        };
    }

    private async Task<object> ExecuteMutationAsync(string toolName, JsonElement args, CancellationToken cancellationToken)
    {
        return toolName switch
        {
            "modify_viewmodel" => await new GenericPipeTool(
                _sessionManager,
                "modify_viewmodel",
                GenericPipeTool.ExtractElementPropertyAndValueParams,
                GenericPipeTool.AugmentModifyViewModelResult).ExecuteAsync(args, cancellationToken).ConfigureAwait(false),
            "set_dp_value" => await new SetDpValueTool(_sessionManager).ExecuteAsync(args, cancellationToken).ConfigureAwait(false),
            "clear_dp_value" => await new ClearDpValueTool(_sessionManager).ExecuteAsync(args, cancellationToken).ConfigureAwait(false),
            "execute_command" => await new ExecuteCommandTool(_sessionManager).ExecuteAsync(args, cancellationToken).ConfigureAwait(false),
            "click_element" => await new ClickElementTool(_sessionManager).ExecuteAsync(args, cancellationToken).ConfigureAwait(false),
            "fire_routed_event" => await new FireRoutedEventTool(_sessionManager).ExecuteAsync(args, cancellationToken).ConfigureAwait(false),
            "focus_element" => await new GenericPipeTool(_sessionManager, "focus_element").ExecuteAsync(args, cancellationToken).ConfigureAwait(false),
            "scroll_to_element" => await new ScrollToElementTool(_sessionManager).ExecuteAsync(args, cancellationToken).ConfigureAwait(false),
            "simulate_keyboard" => await new SimulateKeyboardTool(_sessionManager).ExecuteAsync(args, cancellationToken).ConfigureAwait(false),
            "override_style_setter" => await new OverrideStyleSetterTool(_sessionManager).ExecuteAsync(args, cancellationToken).ConfigureAwait(false),
            "drag_and_drop" => await new GenericPipeTool(
                _sessionManager,
                "drag_and_drop",
                GenericPipeTool.ExtractDragAndDropParams).ExecuteAsync(args, cancellationToken).ConfigureAwait(false),
            _ => new ToolErrorPayload
            {
                Error = $"Unsupported mutation tool '{toolName}'.",
                ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
                Hint = $"Use one of: {string.Join(", ", BatchMutationCatalog.SupportedTools)}."
            }
        };
    }

    private Task<object> ExecuteCaptureSnapshotAsync(JsonElement args, CancellationToken cancellationToken) =>
        new CaptureStateSnapshotTool(_sessionManager).ExecuteAsync(args, cancellationToken);

    private Task<object> ExecuteStateDiffAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var processId = args.GetProperty("processId").GetInt32();
        var snapshotId = args.GetProperty("snapshotId").GetString() ?? string.Empty;
        var trigger = GetOptionalString(args, "trigger");
        return new GetStateDiffTool(_sessionManager).ExecuteAsync(processId, snapshotId, trigger, cancellationToken);
    }

    private static JsonElement BuildMutationArgs(BatchMutationRequest request, BatchMutationStep mutation)
    {
        var payload = new Dictionary<string, object?>
        {
            ["processId"] = request.ProcessId
        };

        if (!string.IsNullOrWhiteSpace(request.DefaultElementId)
            && !mutation.Args.TryGetProperty("elementId", out _))
        {
            payload["elementId"] = request.DefaultElementId;
        }

        foreach (var property in mutation.Args.EnumerateObject())
        {
            payload[property.Name] = property.Value.Clone();
        }

        return JsonSerializer.SerializeToElement(payload);
    }

    private static JsonElement BuildSnapshotArgs(BatchMutationRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["processId"] = request.ProcessId
        };

        foreach (var property in request.CaptureSnapshot!.Args.EnumerateObject())
        {
            payload[property.Name] = property.Value.Clone();
        }

        if (!payload.ContainsKey("elementId") && !string.IsNullOrWhiteSpace(request.DefaultElementId))
        {
            payload["elementId"] = request.DefaultElementId;
        }

        return JsonSerializer.SerializeToElement(payload);
    }

    private static JsonElement ToJsonElement(object value) =>
        value is JsonElement element ? element.Clone() : JsonSerializer.SerializeToElement(value);

    private static bool IsSuccess(JsonElement response) =>
        response.TryGetProperty("success", out var successProperty) && successProperty.GetBoolean();

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : null;
}
