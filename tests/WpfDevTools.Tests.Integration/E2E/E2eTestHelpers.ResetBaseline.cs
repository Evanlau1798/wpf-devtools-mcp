using System.Text.Json;

namespace WpfDevTools.Tests.Integration.E2E;

public static partial class E2eTestHelpers
{
    internal static async Task VerifySharedSessionBaselineAsync(
        ToolCallAsync callToolAsync,
        int processId,
        string nameTextBoxElementId,
        string saveButtonElementId)
    {
        await VerifyNameTextBoxBaselineAsync(callToolAsync, processId, nameTextBoxElementId);
        await VerifySaveCommandBaselineAsync(callToolAsync, processId, saveButtonElementId);
        await DrainPendingEventsUntilEmptyAsync(() => callToolAsync(
            "drain_events",
            new
            {
                processId,
                maxEvents = ResetEventDrainMaxEvents
            }));
    }

    private static async Task VerifyNameTextBoxBaselineAsync(
        ToolCallAsync callToolAsync,
        int processId,
        string elementId)
    {
        var result = await callToolAsync(
            "get_dp_value_source",
            new
            {
                processId,
                elementId,
                propertyName = "Text",
                compact = true,
                settleBindings = true,
                navigation = false
            });
        EnsureToolSucceeded(result, "get_dp_value_source", ResetCommandTargetName);

        var currentValue = GetDpCurrentValue(result);
        if (!string.Equals(currentValue, string.Empty, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Shared E2E reset baseline verification failed: {ResetCommandTargetName} Text expected empty but was '{currentValue}'.");
        }
    }

    private static async Task VerifySaveCommandBaselineAsync(
        ToolCallAsync callToolAsync,
        int processId,
        string elementId)
    {
        var result = await callToolAsync(
            "get_interaction_readiness",
            new
            {
                processId,
                elementId,
                interactionType = "Click",
                navigation = false
            });
        EnsureToolSucceeded(result, "get_interaction_readiness", ResetCommandReadinessTargetName);

        if (TryGetBoolean(result, "isReady") == true)
        {
            throw new InvalidOperationException(
                $"Shared E2E reset baseline verification failed: {ResetCommandReadinessTargetName} should not be ready after reset.");
        }

        var commandReadiness = GetRequiredCommandReadiness(result);
        if (TryGetBoolean(commandReadiness, "hasCommand") != true)
        {
            throw new InvalidOperationException(
                $"Shared E2E reset baseline verification failed: {ResetCommandReadinessTargetName} commandReadiness.hasCommand must be true. Payload: {result.GetRawText()}");
        }

        var canExecute = TryGetBoolean(commandReadiness, "canExecute");
        if (canExecute is null)
        {
            throw new InvalidOperationException(
                $"Shared E2E reset baseline verification failed: {ResetCommandReadinessTargetName} commandReadiness.canExecute must be a boolean. Payload: {result.GetRawText()}");
        }

        if (canExecute.Value)
        {
            throw new InvalidOperationException(
                $"Shared E2E reset baseline verification failed: {ResetCommandReadinessTargetName} command canExecute should be false after reset.");
        }
    }

    private static JsonElement GetRequiredCommandReadiness(JsonElement result)
    {
        if (result.TryGetProperty("commandReadiness", out var commandReadiness) &&
            commandReadiness.ValueKind == JsonValueKind.Object)
        {
            return commandReadiness;
        }

        throw new InvalidOperationException(
            $"Shared E2E reset baseline verification failed: {ResetCommandReadinessTargetName} commandReadiness is required. Payload: {result.GetRawText()}");
    }

    private static bool? TryGetBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }
}
