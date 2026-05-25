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
                batch_mutate = new[]
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
                element_screenshot = new[]
                {
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
                        }
                    }
                },
                capture_state_snapshot = new[]
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
