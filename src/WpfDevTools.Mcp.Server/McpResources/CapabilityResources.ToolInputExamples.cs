using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace WpfDevTools.Mcp.Server.McpResources;

public static partial class CapabilityResources
{
    private const string ToolExamplesResourceUri = "wpf://contracts/tool-examples";

    [McpServerResource(
        Name = "wpf_tool_examples",
        Title = "Tool Input Examples",
        UriTemplate = ToolExamplesResourceUri,
        MimeType = "application/json")]
    [Description("Machine-readable input examples for complex WPF MCP tool calls, keyed by tool name.")]
    public static string GetToolExamples()
    {
        var resource = new
        {
            resourceUri = ToolExamplesResourceUri,
            version = "2026-05-26",
            purpose = "Concise structured examples for complex tool inputs; use these alongside tools/list schemas.",
            examplesByTool = new
            {
                connect = new object[]
                {
                    new
                    {
                        name = "Connect to an explicit allowlisted process",
                        policyGates = new[] { "WPFDEVTOOLS_MCP_ALLOWED_TARGETS" },
                        arguments = new
                        {
                            processId = 12345
                        }
                    },
                    new
                    {
                        name = "Auto-discover a single visible allowlisted process",
                        policyGates = new[] { "WPFDEVTOOLS_MCP_ALLOWED_TARGETS" },
                        arguments = new
                        {
                            selectionStrategy = "single_only",
                            windowFilter = "visible"
                        }
                    }
                },
                get_processes = new object[]
                {
                    new
                    {
                        name = "List visible allowlisted WPF processes",
                        arguments = new
                        {
                            windowFilter = "visible"
                        }
                    },
                    new
                    {
                        name = "Find an allowlisted process by name",
                        arguments = new
                        {
                            nameFilter = "TestApp",
                            windowFilter = "all"
                        }
                    }
                },
                get_ui_summary = new[]
                {
                    new
                    {
                        name = "Summarize the active window with semantic depth",
                        arguments = new
                        {
                            processId = 12345,
                            depthMode = "semantic",
                            summaryOnly = false
                        }
                    }
                },
                get_form_summary = new[]
                {
                    new
                    {
                        name = "Summarize a scoped form without framework internals",
                        arguments = new
                        {
                            processId = 12345,
                            elementId = "LoginForm",
                            includeFramework = false
                        }
                    }
                },
                get_element_snapshot = new[]
                {
                    new
                    {
                        name = "Inspect a focused element with extra DP probes",
                        arguments = new
                        {
                            processId = 12345,
                            elementId = "SaveButton",
                            includeProperties = new[] { "IsEnabled", "Visibility" }
                        }
                    }
                },
                get_namescope = new[]
                {
                    new
                    {
                        name = "Focused lookup from a known scope",
                        arguments = new
                        {
                            processId = 12345,
                            elementId = "MainWindow"
                        },
                        followUp = new
                        {
                            next = "Use returned names for get_element_snapshot or scoped find_elements before broad tree dumps."
                        }
                    }
                },
                get_bindings = new[]
                {
                    new
                    {
                        name = "Inspect active bindings under a subtree",
                        arguments = new
                        {
                            processId = 12345,
                            elementId = "EditorPanel",
                            recursive = true,
                            statusFilter = "Active"
                        }
                    }
                },
                get_binding_errors = new[]
                {
                    new
                    {
                        name = "Read compact binding errors with navigation guidance",
                        arguments = new
                        {
                            processId = 12345,
                            maxErrors = 20,
                            compact = true,
                            navigation = true
                        }
                    }
                },
                drain_events = new[]
                {
                    new
                    {
                        name = "Drain a bounded batch of binding and validation events",
                        arguments = new
                        {
                            processId = 12345,
                            maxEvents = 50,
                            eventTypes = new[] { "BindingError", "ValidationChange" }
                        }
                    }
                },
                batch_mutate = new object[]
                {
                    new
                    {
                        name = "Set one DependencyProperty with snapshot and diff",
                        policyGates = new[]
                        {
                            "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS",
                            "WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS"
                        },
                        arguments = new
                        {
                            processId = 12345,
                            elementId = "SearchTextBox",
                            captureSnapshot = new
                            {
                                propertyNames = new[] { "Text" },
                                includeFocus = true,
                                snapshotName = "before-search-text"
                            },
                            includeDiff = true,
                            mutations = new object[]
                            {
                                new
                                {
                                    tool = "set_dp_value",
                                    label = "Set search text",
                                    args = new
                                    {
                                        propertyName = "Text",
                                        value = "Ready"
                                    }
                                }
                            }
                        }
                    },
                    new
                    {
                        name = "Run ordered search edit with rollback snapshot and diff",
                        policyGates = new[]
                        {
                            "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS",
                            "WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS",
                            "WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION"
                        },
                        arguments = new
                        {
                            processId = 12345,
                            elementId = "SearchTextBox",
                            captureSnapshot = new
                            {
                                propertyNames = new[] { "Text" },
                                viewModelPropertyNames = new[] { "SearchText" },
                                includeFocus = true,
                                snapshotName = "before-batch-search"
                            },
                            includeDiff = true,
                            trigger = "after batch search edit",
                            mutations = new object[]
                            {
                                new
                                {
                                    tool = "focus_element",
                                    label = "Focus search box",
                                    args = new { }
                                },
                                new
                                {
                                    tool = "set_dp_value",
                                    label = "Set bound search text",
                                    args = new
                                    {
                                        propertyName = "Text",
                                        value = "Ready"
                                    }
                                }
                            }
                        }
                    },
                    new
                    {
                        name = "Submit stringified mutations from text-only clients",
                        policyGates = new[] { "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS" },
                        arguments = new
                        {
                            processId = 12345,
                            elementId = "SearchTextBox",
                            mutations = """
                                [
                                  {
                                    "tool": "set_dp_value",
                                    "label": "Set search text",
                                    "args": {
                                      "propertyName": "Text",
                                      "value": "Ready"
                                    }
                                  }
                                ]
                                """
                        }
                    }
                },
                wait_for_dp_change_after_mutation = new[]
                {
                    new
                    {
                        name = "Set Text and wait until the DP reaches the expected value",
                        policyGates = new[]
                        {
                            "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS",
                            "WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS"
                        },
                        arguments = new
                        {
                            processId = 12345,
                            elementId = "SearchTextBox",
                            propertyName = "Text",
                            expectedValue = "Ready",
                            timeoutMs = 5000,
                            pollIntervalMs = 100,
                            triggerMutation = new
                            {
                                tool = "set_dp_value",
                                args = new
                                {
                                    propertyName = "Text",
                                    value = "Ready"
                                }
                            }
                        }
                    }
                },
                element_screenshot = new object[]
                {
                    new
                    {
                        name = "Read screenshot metadata without image bytes",
                        policyGates = new[] { "WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS" },
                        arguments = new
                        {
                            processId = 12345,
                            elementId = "SaveButton",
                            outputMode = "metadata"
                        },
                        outputGuidance = new
                        {
                            noImageBytes = true,
                            pixelEvidenceMode = "file",
                            useWhen = "Use metadata for dimensions, format, and renderability checks only."
                        }
                    },
                    new
                    {
                        name = "Capture a file-backed screenshot and read the retained resource",
                        policyGates = new[] { "WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS" },
                        arguments = new
                        {
                            processId = 12345,
                            elementId = "SaveButton",
                            outputMode = "file",
                            maxWidth = 512
                        },
                        resourceFollowUp = new
                        {
                            when = "Use when the tool result includes resourceUri.",
                            call = "resources/read",
                            resourceUriTemplate = "wpf://screenshots/{screenshotId}"
                        },
                        outputGuidance = new
                        {
                            noImageBytes = false,
                            pixelEvidenceMode = "file",
                            useWhen = "Use file mode when pixel evidence is required without bloating the tool response."
                        }
                    }
                },
                capture_state_snapshot = new object[]
                {
                    new
                    {
                        name = "Capture restorable local DP state before mutation",
                        policyGates = new[]
                        {
                            "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS",
                            "WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS"
                        },
                        arguments = new
                        {
                            processId = 12345,
                            elementId = "EditorPanel",
                            propertyNames = new[] { "Text", "IsEnabled" },
                            includeFocus = true,
                            snapshotName = "before-editor-change"
                        }
                    },
                    new
                    {
                        name = "Capture bound Text rollback with source ViewModel state",
                        policyGates = new[]
                        {
                            "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS",
                            "WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS",
                            "WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION"
                        },
                        arguments = new
                        {
                            processId = 12345,
                            elementId = "SearchTextBox",
                            propertyNames = new[] { "Text" },
                            viewModelPropertyNames = new[] { "SearchText" },
                            includeFocus = true,
                            snapshotName = "before-bound-search-text"
                        }
                    }
                },
                get_state_diff = new[]
                {
                    new
                    {
                        name = "Compare current state against a retained snapshot",
                        arguments = new
                        {
                            processId = 12345,
                            snapshotId = "snapshot_20260526_editor_001",
                            trigger = "after editing sample text"
                        }
                    }
                },
                restore_state_snapshot = new[]
                {
                    new
                    {
                        name = "Restore a same-session runtime snapshot",
                        policyGates = new[]
                        {
                            "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS",
                            "WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS"
                        },
                        arguments = new
                        {
                            processId = 12345,
                            snapshotId = "snapshot_20260526_editor_001",
                            removeAfterRestore = true
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(resource, JsonResourceSerializerOptions);
    }
}
