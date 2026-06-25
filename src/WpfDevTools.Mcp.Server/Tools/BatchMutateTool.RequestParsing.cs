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
        BatchMutationSnapshot? captureSnapshot = null;
        if (!JsonCompatibilityPayloadParser.TryParseOptionalObjectProperty(
                root,
                "captureSnapshot",
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

        var diffTrigger = ParseStringParam(arguments, "trigger") ?? "batch_mutate";

        return (new BatchMutationRequest(
            processId,
            defaultElementId,
            mutations,
            captureSnapshot,
            includeDiff,
            diffTrigger), null);
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
