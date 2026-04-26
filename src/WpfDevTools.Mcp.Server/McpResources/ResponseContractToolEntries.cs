namespace WpfDevTools.Mcp.Server.McpResources;

internal static class ResponseContractToolEntries
{
    public static object[] GetHighValueTools(string resourceUri)
    {
        return new object[]
        {
            new
            {
                tool = "connect",
                contractName = "connect-result",
                canonicalPayloadField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                contractResource = resourceUri,
                topLevelFields = new[]
                {
                    "success",
                    "message",
                    "processId",
                    "processName",
                    "windowTitle",
                    "autoDiscovered",
                    "autoSelected",
                    "selectionReason",
                    "candidateCount",
                    "requiresElevationToConnect",
                    "canConnectFromCurrentServer",
                    "suggestedAction",
                    "processes"
                },
                nestedResponsePaths = new[]
                {
                    "processes[].processId",
                    "processes[].processName",
                    "processes[].windowTitle",
                    "processes[].requiresElevationToConnect",
                    "processes[].canConnectFromCurrentServer",
                    "processes[].connectionWarning"
                },
                requestParameters = new[]
                {
                    "processId",
                    "selectionStrategy",
                    "windowFilter"
                }
            },
            new
            {
                tool = "get_processes",
                contractName = "process-list",
                canonicalPayloadField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                contractResource = resourceUri,
                topLevelFields = new[]
                {
                    "success",
                    "processes",
                    "message"
                },
                nestedResponsePaths = new[]
                {
                    "processes[].processId",
                    "processes[].processName",
                    "processes[].windowTitle",
                    "processes[].runtime",
                    "processes[].requiresElevationToConnect",
                    "processes[].canConnectFromCurrentServer",
                    "processes[].connectionWarning"
                },
                requestParameters = new[]
                {
                    "nameFilter",
                    "windowFilter"
                }
            },
            new
            {
                tool = "get_bindings",
                contractName = "binding-inspection",
                canonicalPayloadField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                contractResource = resourceUri,
                topLevelFields = new[]
                {
                    "success",
                    "bindings",
                    "results",
                    "resultCount",
                    "successCount",
                    "failureCount"
                },
                nestedResponsePaths = new[]
                {
                    "bindings[].bindingType",
                    "bindings[].bindingPaths",
                    "bindings[].currentValue",
                    "results[].elementId",
                    "results[].bindings"
                },
                requestParameters = new[]
                {
                    "elementId",
                    "elementIds",
                    "recursive",
                    "statusFilter"
                }
            },
            new
            {
                tool = "get_binding_errors",
                contractName = "binding-errors",
                canonicalPayloadField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                contractResource = resourceUri,
                topLevelFields = new[]
                {
                    "success",
                    "errorCount",
                    "errors",
                    "pendingEvents",
                    "pendingEventCount",
                    "droppedEventCount",
                    "cleanupIncomplete",
                    "cleanupFailureMessage",
                    "cleanupFailureType",
                    "pendingEventsOrigin",
                    "pendingEventsMayIncludePriorContext",
                    "navigation",
                    "nextSteps"
                },
                nestedResponsePaths = new[]
                {
                    "errors[].timestamp",
                    "errors[].sourceKind",
                    "errors[].elementId",
                    "errors[].suggestedElementId",
                    "errors[].propertyName",
                    "errors[].bindingPath",
                    "pendingEvents[].eventType",
                    "pendingEvents[].elementId",
                    "pendingEvents[].propertyName"
                },
                requestParameters = new[]
                {
                    "maxErrors",
                    "sinceTimestamp",
                    "compact",
                    "navigation"
                }
            },
            new
            {
                tool = "drain_events",
                contractName = "pending-runtime-events",
                canonicalPayloadField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                contractResource = resourceUri,
                topLevelFields = new[]
                {
                    "success",
                    "pendingEventCount",
                    "droppedEventCount",
                    "cleanupIncomplete",
                    "cleanupFailureMessage",
                    "cleanupFailureType",
                    "pendingEvents"
                },
                nestedResponsePaths = new[]
                {
                    "pendingEvents[].eventType",
                    "pendingEvents[].elementId",
                    "pendingEvents[].propertyName",
                    "pendingEvents[].eventName",
                    "pendingEvents[].timestampUtc"
                },
                requestParameters = new[]
                {
                    "maxEvents",
                    "eventTypes",
                    "elementId",
                    "sinceTimestamp"
                },
                errorDataFields = new[]
                {
                    "errorData.replayPreserved",
                    "errorData.bufferedReplayEventCount"
                },
                recoveryFields = new[]
                {
                    "recovery.hint",
                    "recovery.suggestedAction"
                },
                semantics = new
                {
                    callerVisibleFiltersAppliedAfterMergedRead = true,
                    overflowEventsRetainedForNextDrain = true,
                    replayPreservedOnLiveFailure = true
                }
            },
            new
            {
                tool = "get_ui_summary",
                contractName = "ui-summary",
                canonicalPayloadField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                contractResource = resourceUri,
                topLevelFields = new[]
                {
                    "success",
                    "rootElementId",
                    "rootElementType",
                    "rootElementName",
                    "depth",
                    "depthMode",
                    "scopeVisibility",
                    "isCurrentlyVisible",
                    "summaryText",
                    "semanticNodeCount",
                    "nodes"
                },
                nestedResponsePaths = new[]
                {
                    "nodes[].elementId",
                    "nodes[].elementType",
                    "nodes[].elementName",
                    "nodes[].kind",
                    "nodes[].depth",
                    "nodes[].text",
                    "nodes[].currentValue",
                    "nodes[].annotations"
                },
                requestParameters = new[]
                {
                    "elementId",
                    "depth",
                    "depthMode",
                    "summaryOnly"
                }
            },
            new
            {
                tool = "get_element_snapshot",
                contractName = "element-snapshot",
                canonicalPayloadField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                contractResource = resourceUri,
                topLevelFields = new[]
                {
                    "success",
                    "elementId",
                    "elementType",
                    "elementName",
                    "dataContextType",
                    "properties",
                    "bindings",
                    "validationErrors",
                    "style",
                    "layout"
                },
                nestedResponsePaths = Array.Empty<string>(),
                requestParameters = new[]
                {
                    "elementId",
                    "includeProperties"
                }
            },
            new
            {
                tool = "get_form_summary",
                contractName = "form-summary",
                canonicalPayloadField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                contractResource = resourceUri,
                topLevelFields = new[]
                {
                    "success",
                    "formScope",
                    "scopeVisibility",
                    "isCurrentlyVisible",
                    "inputs",
                    "commands",
                    "summary"
                },
                nestedResponsePaths = new[]
                {
                    "summary.totalInputs",
                    "summary.emptyInputs",
                    "summary.errorCount",
                    "summary.validationSubmittable",
                    "summary.interactionSubmittable",
                    "summary.isSubmittable"
                },
                requestParameters = new[]
                {
                    "elementId",
                    "includeFramework"
                }
            }
        };
    }
}
