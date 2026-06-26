using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class BatchMutateTool
{
    private static (BatchMutationRequest? request, object? error) ParseRequest(
        JsonElement? arguments,
        int processId,
        string? defaultElementId)
    {
        if (arguments is not { ValueKind: JsonValueKind.Object } root)
        {
            return (null, CreateMissingParamError("mutations"));
        }

        if (!TryParseMutationsElement(root, out var mutationsElement, out var mutationsError))
        {
            return (null, mutationsError);
        }

        if (!TryValidateMutationCount(mutationsElement, out var mutationCountError))
        {
            return (null, mutationCountError);
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

            if (mutationElement.TryGetProperty("arguments", out _))
            {
                return (null, CreateInvalidParamError(
                    "Each mutations item uses 'arguments', but batch_mutate mutation steps must put nested tool inputs under 'args': { \"tool\": \"set_dp_value\", \"args\": { \"propertyName\": \"Text\", \"value\": \"Ready\" } }."));
            }

            JsonElement args = JsonSerializer.SerializeToElement(new { });
            if (mutationElement.TryGetProperty("args", out var argsElement))
            {
                if (argsElement.ValueKind != JsonValueKind.Object)
                {
                    return (null, CreateInvalidParamError("Each mutations item args value must be an object when provided."));
                }

                args = argsElement.Clone();
                if (args.TryGetProperty("processId", out _))
                {
                    return (null, CreateInvalidParamError(
                        "Each mutations item args value must not include processId; use the batch root processId only."));
                }
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
        var rollbackOnFailure = ParseBoolParam(arguments, "rollbackOnFailure") ?? false;
        BatchMutationSnapshot? captureSnapshot = null;
        if (!TryParseCaptureSnapshotElement(
                root,
                mutations,
                defaultElementId,
                out var captureSnapshotElement,
                out var hasCaptureSnapshot,
                out var captureSnapshotError))
        {
            return (null, CreateInvalidParamError(captureSnapshotError!));
        }

        if (hasCaptureSnapshot)
        {
            if (captureSnapshotElement.TryGetProperty("processId", out _))
            {
                return (null, CreateInvalidParamError(
                    "captureSnapshot must not include processId; use the batch root processId only."));
            }

            captureSnapshot = new BatchMutationSnapshot(captureSnapshotElement);
        }

        if (includeDiff && captureSnapshot is null)
        {
            return (null, CreateInvalidParamError("includeDiff requires captureSnapshot so the batch has a baseline snapshot."));
        }

        if (rollbackOnFailure && captureSnapshot is null)
        {
            return (null, CreateInvalidParamError("rollbackOnFailure requires captureSnapshot so the batch has a rollback baseline."));
        }

        var diffTrigger = ParseStringParam(arguments, "trigger") ?? "batch_mutate";

        return (new BatchMutationRequest(
            processId,
            defaultElementId,
            mutations,
            captureSnapshot,
            includeDiff,
            rollbackOnFailure,
            diffTrigger), null);
    }

    private static bool TryParseCaptureSnapshotElement(
        JsonElement root,
        IReadOnlyList<BatchMutationStep> mutations,
        string? defaultElementId,
        out JsonElement captureSnapshotElement,
        out bool hasCaptureSnapshot,
        out string? errorMessage)
    {
        captureSnapshotElement = default;
        hasCaptureSnapshot = false;
        errorMessage = null;

        if (!root.TryGetProperty("captureSnapshot", out var rawCaptureSnapshot))
        {
            return true;
        }

        if (rawCaptureSnapshot.ValueKind is JsonValueKind.False or JsonValueKind.Null)
        {
            return true;
        }

        if (rawCaptureSnapshot.ValueKind == JsonValueKind.True)
        {
            hasCaptureSnapshot = true;
            return TryInferCaptureSnapshotElement(
                mutations,
                defaultElementId,
                out captureSnapshotElement,
                out errorMessage);
        }

        return JsonCompatibilityPayloadParser.TryParseOptionalObjectProperty(
            root,
            "captureSnapshot",
            out captureSnapshotElement,
            out hasCaptureSnapshot,
            out errorMessage);
    }

    private static bool TryInferCaptureSnapshotElement(
        IReadOnlyList<BatchMutationStep> mutations,
        string? defaultElementId,
        out JsonElement captureSnapshotElement,
        out string? errorMessage)
    {
        captureSnapshotElement = default;
        errorMessage = null;

        var elementIds = new List<string>();
        var seenElementIds = new HashSet<string>(StringComparer.Ordinal);
        var propertyNames = new List<string>();
        var seenPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        var viewModelPropertyNames = new List<string>();
        var seenViewModelPropertyNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mutation in mutations)
        {
            var mutationElementId = GetMutationElementId(mutation, defaultElementId);
            if (!string.IsNullOrWhiteSpace(mutationElementId) && seenElementIds.Add(mutationElementId))
            {
                elementIds.Add(mutationElementId);
            }

            if (RequiresDependencyPropertySnapshot(mutation.Tool))
            {
                if (!TryAddMutationPropertyName(mutation, propertyNames, seenPropertyNames, out errorMessage))
                {
                    return false;
                }
            }
            else if (string.Equals(mutation.Tool, "modify_viewmodel", StringComparison.Ordinal))
            {
                if (!TryAddMutationPropertyName(
                        mutation,
                        viewModelPropertyNames,
                        seenViewModelPropertyNames,
                        out errorMessage))
                {
                    return false;
                }
            }
        }

        if (elementIds.Count > 1 && (propertyNames.Count > 0 || viewModelPropertyNames.Count > 0))
        {
            errorMessage = "captureSnapshot=true can infer rollback state only when property mutations target a single element. Provide an explicit captureSnapshot object for multi-element batches.";
            return false;
        }

        var payload = new Dictionary<string, object?>
        {
            ["includeFocus"] = true
        };

        if (elementIds.Count == 1)
        {
            payload["elementId"] = elementIds[0];
        }

        if (propertyNames.Count > 0)
        {
            payload["propertyNames"] = propertyNames;
        }

        if (viewModelPropertyNames.Count > 0)
        {
            payload["viewModelPropertyNames"] = viewModelPropertyNames;
        }

        captureSnapshotElement = JsonSerializer.SerializeToElement(payload);
        return true;
    }

    private static string? GetMutationElementId(BatchMutationStep mutation, string? defaultElementId)
    {
        var elementId = GetOptionalString(mutation.Args, "elementId");
        return !string.IsNullOrWhiteSpace(elementId)
            ? elementId
            : defaultElementId;
    }

    private static bool RequiresDependencyPropertySnapshot(string tool) =>
        tool is "set_dp_value" or "clear_dp_value" or "override_style_setter";

    private static bool TryAddMutationPropertyName(
        BatchMutationStep mutation,
        List<string> propertyNames,
        HashSet<string> seenPropertyNames,
        out string? errorMessage)
    {
        errorMessage = null;
        var propertyName = GetOptionalString(mutation.Args, "propertyName");
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            errorMessage = $"captureSnapshot=true cannot infer rollback state because mutation step {mutation.Index} ({mutation.Tool}) is missing propertyName. Provide an explicit captureSnapshot object.";
            return false;
        }

        if (seenPropertyNames.Add(propertyName))
        {
            propertyNames.Add(propertyName);
        }

        return true;
    }

    private static bool TryParseMutationsElement(
        JsonElement root,
        out JsonElement mutationsElement,
        out object? error)
    {
        mutationsElement = default;
        error = null;

        if (!root.TryGetProperty("mutations", out var rawMutationsElement))
        {
            error = CreateMissingParamError("mutations");
            return false;
        }

        if (rawMutationsElement.ValueKind == JsonValueKind.Array)
        {
            mutationsElement = rawMutationsElement;
            return true;
        }

        if (rawMutationsElement.ValueKind != JsonValueKind.String)
        {
            error = CreateInvalidParamError("mutations must be a JSON array.");
            return false;
        }

        var serializedMutations = rawMutationsElement.GetString();
        if (string.IsNullOrWhiteSpace(serializedMutations))
        {
            error = CreateMissingParamError("mutations");
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(serializedMutations);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = CreateInvalidParamError("Stringified mutations payload must decode to a JSON array.");
                return false;
            }

            mutationsElement = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            error = CreateInvalidParamError("mutations string payload must contain valid JSON.");
            return false;
        }
    }
}
