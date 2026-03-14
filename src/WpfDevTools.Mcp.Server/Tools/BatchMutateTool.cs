using System.Text.Json;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed class BatchMutateTool : PipeConnectedToolBase
{
    private readonly Func<string, JsonElement, CancellationToken, Task<object>> _mutationExecutor;
    private readonly Func<JsonElement, CancellationToken, Task<object>> _snapshotExecutor;
    private readonly Func<JsonElement, CancellationToken, Task<object>> _stateDiffExecutor;

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
        Func<string, JsonElement, CancellationToken, Task<object>>? mutationExecutor,
        Func<JsonElement, CancellationToken, Task<object>>? snapshotExecutor,
        Func<JsonElement, CancellationToken, Task<object>>? stateDiffExecutor)
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

        foreach (var mutation in request.Mutations)
        {
            if (stopExecution)
            {
                skippedMutationCount++;
                mutationResults.Add(new
                {
                    index = mutation.Index,
                    tool = mutation.Tool,
                    label = mutation.Label,
                    success = false,
                    skipped = true,
                    error = "Skipped because an earlier mutation failed."
                });
                continue;
            }

            var mutationResult = ToJsonElement(await _mutationExecutor(
                mutation.Tool,
                BuildMutationArgs(request, mutation),
                cancellationToken).ConfigureAwait(false));

            executedMutationCount++;
            var mutationSucceeded = IsSuccess(mutationResult);
            if (mutationSucceeded)
            {
                successfulMutationCount++;
            }
            else
            {
                failedMutationCount++;
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
                result = mutationResult.Clone()
            });
        }

        object? stateDiff = null;
        if (request.IncludeDiff && failedMutationCount == 0 && !string.IsNullOrWhiteSpace(snapshotId))
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
        }

        var rollback = BuildRollback(processId, snapshotId, failedMutationCount);
        var overallSuccess = failedMutationCount == 0
            && (stateDiff is not JsonElement diffPayload || IsSuccess(diffPayload));

        return new
        {
            success = overallSuccess,
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

    private static (BatchMutationRequest? request, object? error) ParseRequest(
        JsonElement? arguments,
        int processId,
        string? defaultElementId)
    {
        if (arguments is not { ValueKind: JsonValueKind.Object } root)
        {
            return (null, CreateMissingParamError("mutations"));
        }

        if (!root.TryGetProperty("mutations", out var mutationsElement) || mutationsElement.ValueKind != JsonValueKind.Array)
        {
            return (null, CreateMissingParamError("mutations"));
        }

        var mutations = new List<BatchMutationStep>();
        var index = 0;
        foreach (var mutationElement in mutationsElement.EnumerateArray())
        {
            if (mutationElement.ValueKind != JsonValueKind.Object)
            {
                return (null, CreateInvalidParamError("Each mutations item must be an object."));
            }

            if (!mutationElement.TryGetProperty("tool", out var toolElement)
                || toolElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(toolElement.GetString()))
            {
                return (null, CreateInvalidParamError("Each mutations item must include a non-empty tool."));
            }

            var tool = toolElement.GetString()!;
            if (!BatchMutationCatalog.SupportedTools.Contains(tool))
            {
                return (null, CreateInvalidParamError(
                    $"Unsupported mutation tool '{tool}'. Supported tools: {string.Join(", ", BatchMutationCatalog.SupportedTools)}."));
            }

            JsonElement args = JsonSerializer.SerializeToElement(new { });
            if (mutationElement.TryGetProperty("args", out var argsElement))
            {
                if (argsElement.ValueKind != JsonValueKind.Object)
                {
                    return (null, CreateInvalidParamError("Each mutations item args value must be an object when provided."));
                }

                args = argsElement.Clone();
            }

            string? label = null;
            if (mutationElement.TryGetProperty("label", out var labelElement))
            {
                if (labelElement.ValueKind != JsonValueKind.String)
                {
                    return (null, CreateInvalidParamError("Each mutations item label must be a string when provided."));
                }

                label = labelElement.GetString();
            }

            mutations.Add(new BatchMutationStep(index++, tool, args, label));
        }

        if (mutations.Count == 0)
        {
            return (null, CreateInvalidParamError("mutations must contain at least one mutation step."));
        }

        var includeDiff = ParseBoolParam(arguments, "includeDiff") ?? false;
        BatchMutationSnapshot? captureSnapshot = null;
        if (root.TryGetProperty("captureSnapshot", out var captureSnapshotElement))
        {
            if (captureSnapshotElement.ValueKind != JsonValueKind.Object)
            {
                return (null, CreateInvalidParamError("captureSnapshot must be an object when provided."));
            }

            captureSnapshot = new BatchMutationSnapshot(captureSnapshotElement.Clone());
        }

        if (includeDiff && captureSnapshot is null)
        {
            return (null, CreateInvalidParamError("includeDiff requires captureSnapshot so the batch has a baseline snapshot."));
        }

        var diffTrigger = ParseStringParam(arguments, "trigger") ?? "batch_mutate";

        return (new BatchMutationRequest(
            processId,
            defaultElementId,
            mutations,
            captureSnapshot,
            includeDiff,
            diffTrigger), null);
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

    private object? BuildRollback(int processId, string? capturedSnapshotId, int failedMutationCount)
    {
        if (failedMutationCount == 0)
        {
            return null;
        }

        var snapshotId = capturedSnapshotId;
        if (string.IsNullOrWhiteSpace(snapshotId)
            && !_sessionManager.TryGetActiveSnapshotId(processId, out snapshotId))
        {
            return new
            {
                available = false,
                reason = "No active snapshot is available for rollback guidance."
            };
        }

        return new
        {
            available = true,
            snapshotId,
            tool = "restore_state_snapshot",
            @params = new
            {
                processId,
                snapshotId
            }
        };
    }

    private static object CreateStepFailure(string stepName, JsonElement response)
    {
        return new ToolErrorPayload
        {
            Error = response.TryGetProperty("error", out var errorProperty)
                ? $"Failed during {stepName}. {errorProperty.GetString()}".Trim()
                : $"Failed during {stepName}.",
            ErrorCode = response.TryGetProperty("errorCode", out var errorCodeProperty)
                ? errorCodeProperty.GetString() ?? ToolErrorCode.OperationFailed.ToString()
                : ToolErrorCode.OperationFailed.ToString(),
            Hint = response.TryGetProperty("hint", out var hintProperty)
                ? hintProperty.GetString()
                : $"Inspect the failing {stepName} step before retrying batch_mutate.",
            ErrorData = response.Clone()
        };
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
