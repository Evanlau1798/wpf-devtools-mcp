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
                    "error",
                    "errorCode",
                    "message",
                    "hint",
                    "processId",
                    "processName",
                    "windowTitle",
                    "reusedExistingHost",
                    "connectionSource",
                    "targetIsElevated",
                    "requiresExplicitTargetOptIn",
                    "autoDiscovered",
                    "autoSelected",
                    "selectionReason",
                    "candidateCount",
                    "redactedCandidateCount",
                    "policyEnvVar",
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
                    "processes[].secondaryWindowTitle",
                    "processes[].architecture",
                    "processes[].dotNetVersion",
                    "processes[].runtime",
                    "processes[].isElevated",
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
                    "message",
                    "redactedTargetCount",
                    "policyEnvVar"
                },
                nestedResponsePaths = new[]
                {
                    "processes[].processId",
                    "processes[].processName",
                    "processes[].windowTitle",
                    "processes[].secondaryWindowTitle",
                    "processes[].architecture",
                    "processes[].dotNetVersion",
                    "processes[].runtime",
                    "processes[].isElevated",
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
                    "pendingEventsSuggestedAction",
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
                topLevelFields = new[] { "success", "rootElementId", "rootElementType", "rootElementName", "depth", "depthMode", "scopeVisibility", "isCurrentlyVisible", "summaryText", "semanticNodeCount", "traversalNodeCount", "omittedNodeCount", "omittedSemanticNodeCount", "truncated", "truncationReasons", "payloadLimits", "nodes", "navigationNodes" },
                nestedResponsePaths = new[] { "payloadLimits.maxTraversalNodes", "payloadLimits.maxSemanticNodes", "payloadLimits.maxSummaryTextLength", "payloadLimits.maxStringValueLength", "nodes[].elementId", "nodes[].elementType", "nodes[].elementName", "nodes[].kind", "nodes[].depth", "nodes[].text", "nodes[].currentValue", "nodes[].annotations", "navigationNodes[].elementId", "navigationNodes[].kind" },
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
                topLevelFields = new[] { "success", "formScope", "scopeVisibility", "isCurrentlyVisible", "inputs", "commands", "traversalNodeCount", "omittedNodeCount", "omittedInputCount", "omittedCommandCount", "truncated", "truncationReasons", "payloadLimits", "summary" },
                nestedResponsePaths = new[] { "inputs[].elementId", "inputs[].elementName", "inputs[].currentValue", "inputs[].bindingPath", "inputs[].isEmpty", "commands[].elementId", "commands[].elementName", "commands[].text", "commands[].isPrimary", "commands[].isReady", "commands[].blockers", "payloadLimits.maxTraversalNodes", "payloadLimits.maxInputs", "payloadLimits.maxCommands", "payloadLimits.maxStringValueLength", "summary.totalInputs", "summary.emptyInputs", "summary.errorCount", "summary.validationSubmittable", "summary.interactionSubmittable", "summary.isSubmittable" },
                requestParameters = new[]
                {
                    "elementId",
                    "includeFramework"
                }
            },
            new
            {
                tool = "capture_state_snapshot",
                contractName = "state-snapshot-capture",
                canonicalPayloadField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                contractResource = resourceUri,
                topLevelFields = new[] { "success", "snapshotId", "snapshotName", "snapshotSummary", "snapshotCompleteness", "skippedDependencyProperties", "warnings" },
                nestedResponsePaths = new[]
                {
                    "snapshotSummary.dependencyPropertyCount",
                    "snapshotSummary.skippedDependencyPropertyCount",
                    "snapshotSummary.viewModelPropertyCount",
                    "snapshotSummary.capturedFocus",
                    "snapshotCompleteness.bindingErrorBaselineCaptured",
                    "snapshotCompleteness.validationBaselineCaptured",
                    "skippedDependencyProperties[].propertyName",
                    "skippedDependencyProperties[].reason",
                    "warnings[]"
                },
                requestParameters = new[] { "elementId", "propertyNames", "viewModelPropertyNames", "includeFocus", "snapshotName" }
            },
            new
            {
                tool = "get_state_diff",
                contractName = "state-diff",
                canonicalPayloadField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                contractResource = resourceUri,
                topLevelFields = new[] { "success", "snapshotId", "trigger", "durationMs", "propertyChanges", "viewModelChanges", "newBindingErrors", "resolvedBindingErrors", "validationChanges", "focusChange" },
                nestedResponsePaths = new[] { "propertyChanges[].propertyName", "viewModelChanges[].propertyName", "focusChange.changed" },
                requestParameters = new[] { "snapshotId", "trigger" }
            },
            new
            {
                tool = "restore_state_snapshot",
                contractName = "state-snapshot-restore",
                canonicalPayloadField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                contractResource = resourceUri,
                topLevelFields = new[] { "success", "snapshotId", "restoreIncomplete", "stateAfterTimeoutUnknown", "requiresReconnect", "processId", "timeoutSeconds", "retryAfterSeconds", "retryAfter", "availableTokens", "availableEvents", "restoredDependencyPropertyCount", "restoredDependencyProperties", "skippedDependencyPropertyCount", "skippedDependencyProperties", "restoredViewModelPropertyCount", "restoredViewModelProperties", "skippedViewModelPropertyCount", "skippedViewModelProperties", "restoredFocus", "warnings" },
                nestedResponsePaths = new[] { "restoredDependencyProperties[].propertyName", "skippedDependencyProperties[].reason", "restoredViewModelProperties[].verified" },
                requestParameters = new[] { "snapshotId", "removeAfterRestore" }
            },
            new
            {
                tool = "batch_mutate",
                contractName = "batch-mutation",
                canonicalPayloadField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                contractResource = resourceUri,
                topLevelFields = new[] { "success", "executionMode", "executionPolicy", "stopOnError", "snapshotId", "mutationCount", "executedMutationCount", "successfulMutationCount", "failedMutationCount", "skippedMutationCount", "stateAfterTimeoutUnknown", "requiresReconnect", "processId", "timeoutSeconds", "retryAfterSeconds", "retryAfter", "availableTokens", "availableEvents", "mutations", "stateDiff", "rollback", "recovery" },
                nestedResponsePaths = new[] { "mutations[].tool", "mutations[].success", "mutations[].stateAfterTimeoutUnknown", "stateDiff.snapshotId", "rollback.params.snapshotId", "recovery.tool", "recovery.retryAfterSeconds" },
                requestParameters = new[] { "captureSnapshot", "includeDiff", "mutations", "trigger" }
            },
            new
            {
                tool = "element_screenshot",
                contractName = "element-screenshot",
                canonicalPayloadField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                contractResource = resourceUri,
                topLevelFields = new[]
                {
                    "success",
                    "elementId",
                    "width",
                    "height",
                    "format",
                    "rendered",
                    "byteLength",
                    "screenshotId",
                    "outputMode",
                    "resourceUri",
                    "fileName",
                    "expiresAtUtc",
                    "localPathRedacted",
                    "sha256",
                    "mimeType",
                    "base64Image",
                    "maxInlineByteLength"
                },
                nestedResponsePaths = Array.Empty<string>(),
                requestParameters = new[]
                {
                    "elementId",
                    "outputMode",
                    "maxWidth",
                    "maxHeight"
                },
                outputVariants = new[]
                {
                    new
                    {
                        outputMode = "metadata",
                        rendered = false,
                        fields = new[]
                        {
                            "success",
                            "width",
                            "height",
                            "format",
                            "rendered",
                            "byteLength"
                        }
                    },
                    new
                    {
                        outputMode = "file",
                        rendered = true,
                        fields = new[]
                        {
                            "success",
                            "screenshotId",
                            "outputMode",
                            "resourceUri",
                            "fileName",
                            "expiresAtUtc",
                            "localPathRedacted",
                            "sha256",
                            "width",
                            "height",
                            "format",
                            "rendered",
                            "byteLength"
                        }
                    },
                    new
                    {
                        outputMode = "base64",
                        rendered = true,
                        fields = new[]
                        {
                            "success",
                            "base64Image",
                            "width",
                            "height",
                            "format",
                            "rendered",
                            "byteLength"
                        }
                    }
                },
                outputModeGuidance = new
                {
                    metadata = new
                    {
                        noImageBytes = true,
                        preferredForPixelEvidence = false,
                        useWhen = "Use for dimensions, format, renderability checks, and low-cost discovery only."
                    },
                    file = new
                    {
                        noImageBytes = false,
                        preferredForPixelEvidence = true,
                        resourceRead = "resources/read",
                        useWhen = "Use for normal pixel evidence; the result returns resourceUri and redacts local paths."
                    },
                    base64 = new
                    {
                        noImageBytes = false,
                        inlineOnlyForSmallImages = true,
                        preferredForPixelEvidence = false,
                        useWhen = "Use only for small inline captures when a resource read is not available."
                    }
                },
                errorDataFields = new[]
                {
                    "errorData.byteLength",
                    "errorData.maxInlineByteLength",
                    "errorData.width",
                    "errorData.height"
                }
            }
        };
    }
}
