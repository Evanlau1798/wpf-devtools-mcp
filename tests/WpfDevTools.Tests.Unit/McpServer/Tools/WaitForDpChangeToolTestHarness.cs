using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

internal static class WaitForDpChangeToolTestHarness
{
    internal static async Task<ConnectedWaitSession> CreateConnectedSessionAsync(int processId)
    {
        var state = new WaitServerState();
        return await ConnectedWaitSessionBuilder.CreateAsync(processId, state, BuildResultAsync);
    }

    internal static async Task<ConnectedWaitSession> CreateBoundaryConnectedSessionAsync(int processId)
    {
        var state = new BoundaryWaitServerState();
        return await ConnectedWaitSessionBuilder.CreateAsync(
            processId,
            state,
            static (request, currentState) => Task.FromResult(BuildBoundaryResult(request, currentState)));
    }

    internal static async Task<ConnectedWaitSession> CreateBindingSettlementSessionAsync(int processId)
    {
        var state = new BindingSettlementServerState();
        var requestPayloads = new List<(string method, bool settleBindings)>();
        return await ConnectedWaitSessionBuilder.CreateAsync(
            processId,
            state,
            static (request, currentState) => Task.FromResult(BuildBindingSettlementResult(request, currentState)),
            request => requestPayloads.Add((request.Method, HasSettleBindingsFlag(request.Params))),
            requestPayloads);
    }

    internal static async Task<ConnectedWaitSession> CreateDelayedTriggerSessionAsync(int processId, int mutationDelayMs)
    {
        var state = new WaitServerState();
        return await ConnectedWaitSessionBuilder.CreateAsync(
            processId,
            state,
            (request, currentState) => BuildDelayedTriggerResultAsync(request, currentState, mutationDelayMs));
    }

    internal static async Task<ConnectedWaitSession> CreateDelayedAfterTriggerSnapshotSessionAsync(
        int processId,
        int mutationDelayMs,
        int afterTriggerSnapshotDelayMs)
    {
        var state = new DelayedAfterTriggerSnapshotState();
        return await ConnectedWaitSessionBuilder.CreateAsync(
            processId,
            state,
            (request, currentState) => BuildDelayedAfterTriggerSnapshotResultAsync(
                request,
                currentState,
                mutationDelayMs,
                afterTriggerSnapshotDelayMs));
    }

    private static async Task<object> BuildResultAsync(InspectorRequest request, WaitServerState state)
    {
        switch (request.Method)
        {
            case "wait_for_dp_change":
                await Task.Delay(750);
                return new
                {
                    success = true,
                    changed = false,
                    timedOut = true,
                    observedChange = false,
                    matchedExpectedValueAtStart = false,
                    completionReason = "TimedOut",
                    propertyName = "Text",
                    initialValue = state.CurrentValue,
                    initialBaseValueSource = "Local",
                    currentValue = state.CurrentValue,
                    baseValueSource = "Local",
                    elapsedMs = 750,
                    pollCount = 1
                };
            case "get_dp_value_source":
                return new
                {
                    success = true,
                    propertyName = "Text",
                    baseValueSource = "Local",
                    currentValue = state.CurrentValue,
                    effectiveValue = state.CurrentValue
                };
            case "modify_viewmodel":
                state.CurrentValue = ExtractRequestedValue(request.Params);
                return new
                {
                    success = true,
                    propertyName = "Name",
                    oldValue = "before",
                    newValue = state.CurrentValue
                };
            default:
                return new { success = true };
        }
    }

    private static object BuildBoundaryResult(InspectorRequest request, BoundaryWaitServerState state)
    {
        switch (request.Method)
        {
            case "get_dp_value_source":
                state.GetDpValueSourceCallCount++;
                state.CurrentValue = state.GetDpValueSourceCallCount >= 4 ? "after" : "before";
                return new
                {
                    success = true,
                    propertyName = "Text",
                    baseValueSource = "Local",
                    currentValue = state.CurrentValue,
                    effectiveValue = state.CurrentValue
                };
            default:
                return new { success = true };
        }
    }

    private static object BuildBindingSettlementResult(InspectorRequest request, BindingSettlementServerState state)
    {
        switch (request.Method)
        {
            case "get_dp_value_source":
                var settleBindings = HasSettleBindingsFlag(request.Params);
                var currentValue = settleBindings && state.PendingValue is not null
                    ? state.PendingValue
                    : state.VisibleValue;

                if (settleBindings && state.PendingValue is not null)
                {
                    state.VisibleValue = state.PendingValue;
                    state.PendingValue = null;
                }

                return new
                {
                    success = true,
                    propertyName = "Text",
                    baseValueSource = "Local",
                    currentValue,
                    effectiveValue = currentValue
                };
            case "modify_viewmodel":
                state.PendingValue = ExtractRequestedValue(request.Params);
                return new
                {
                    success = true,
                    propertyName = "SearchText",
                    oldValue = state.VisibleValue,
                    newValue = state.PendingValue
                };
            default:
                return new { success = true };
        }
    }

    private static async Task<object> BuildDelayedTriggerResultAsync(
        InspectorRequest request,
        WaitServerState state,
        int mutationDelayMs)
    {
        switch (request.Method)
        {
            case "get_dp_value_source":
                return new
                {
                    success = true,
                    propertyName = "Text",
                    baseValueSource = "Local",
                    currentValue = state.CurrentValue,
                    effectiveValue = state.CurrentValue
                };
            case "modify_viewmodel":
                await Task.Delay(mutationDelayMs);
                state.CurrentValue = ExtractRequestedValue(request.Params);
                return new
                {
                    success = true,
                    propertyName = "Name",
                    oldValue = "before",
                    newValue = state.CurrentValue
                };
            default:
                return new { success = true };
        }
    }

    private static async Task<object> BuildDelayedAfterTriggerSnapshotResultAsync(
        InspectorRequest request,
        DelayedAfterTriggerSnapshotState state,
        int mutationDelayMs,
        int afterTriggerSnapshotDelayMs)
    {
        switch (request.Method)
        {
            case "get_dp_value_source":
                if (state.TriggerCompleted)
                {
                    await Task.Delay(afterTriggerSnapshotDelayMs);
                }

                return new
                {
                    success = true,
                    propertyName = "Text",
                    baseValueSource = "Local",
                    currentValue = state.CurrentValue,
                    effectiveValue = state.CurrentValue
                };
            case "modify_viewmodel":
                await Task.Delay(mutationDelayMs);
                state.CurrentValue = ExtractRequestedValue(request.Params);
                state.TriggerCompleted = true;
                return new
                {
                    success = true,
                    propertyName = "Name",
                    oldValue = "before",
                    newValue = state.CurrentValue
                };
            default:
                return new { success = true };
        }
    }

    private static bool HasSettleBindingsFlag(JsonElement? @params)
    {
        return @params.HasValue
            && @params.Value.TryGetProperty("settleBindings", out var settleBindings)
            && settleBindings.ValueKind == JsonValueKind.True;
    }

    private static string ExtractRequestedValue(JsonElement? @params)
    {
        if (!@params.HasValue || !@params.Value.TryGetProperty("value", out var valueProperty))
        {
            return "after";
        }

        return valueProperty.ValueKind == JsonValueKind.String
            ? valueProperty.GetString() ?? "after"
            : valueProperty.GetRawText();
    }

    private sealed class WaitServerState
    {
        public string CurrentValue { get; set; } = "before";
    }

    private sealed class BindingSettlementServerState
    {
        public string VisibleValue { get; set; } = "before";
        public string? PendingValue { get; set; }
    }

    private sealed class BoundaryWaitServerState
    {
        public string CurrentValue { get; set; } = "before";
        public int GetDpValueSourceCallCount { get; set; }
    }

    private sealed class DelayedAfterTriggerSnapshotState
    {
        public string CurrentValue { get; set; } = "before";
        public bool TriggerCompleted { get; set; }
    }
}
