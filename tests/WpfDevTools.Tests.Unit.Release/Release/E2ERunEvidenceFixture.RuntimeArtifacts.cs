using System.Text.Json;
using System.Text.Json.Nodes;

namespace WpfDevTools.Tests.Unit.Release;

internal sealed partial class E2ERunEvidenceFixture
{
    private static readonly string[] RequiredMcpTools =
    [
        "connect", "get_active_process", "get_ui_summary", "get_element_snapshot",
        "get_state_diff", "restore_state_snapshot"
    ];

    private void WriteRuntimeArtifacts()
    {
        foreach (var tool in RequiredMcpTools)
        {
            WriteArtifact($"mcp-{tool}", $"mcp/{tool}.json", CreateSuccessfulCall(tool));
        }
        WriteArtifact("runtimeInventory", "interaction/runtime-inventory.json", CreateRuntimeInventory());
        WriteArtifact("resultsListBindings", "interaction/results-list-bindings.json",
            CreateBindingEvidence("ResultsList", "ListView", "ItemsSource", "SelectedItem"));
        WriteArtifact("primaryActionBindings", "interaction/primary-action-bindings.json",
            CreateBindingEvidence("PrimaryAction", "Button", "Command", "CommandParameter"));
        WriteArtifact("interactionBefore", "interaction/before.json", CreateInteractionState("Item A", "Idle"));
        WriteArtifact("interactionAction", "interaction/action.json", CreateInteractionActions());
        WriteArtifact("interactionAfter", "interaction/after.json", CreateInteractionState("Item B", "Completed"));
        WriteArtifact("stateDiff", "state/diff.json", JsonSerializer.Serialize(new
        {
            result = new { isError = false, structuredContent = new { success = true, changeCount = 2 } }
        }));
        WriteArtifact("stateRestore", "state/restore.json", JsonSerializer.Serialize(new
        {
            result = new
            {
                isError = false,
                structuredContent = new
                {
                    success = true,
                    restoredSelection = true,
                    restoredState = true,
                    restoredFocus = true
                }
            },
            readback = new { matchesBaseline = true }
        }));
    }

    private static JsonArray CreatePositiveMcpCalls()
        => new(RequiredMcpTools.Select(tool => (JsonNode)new JsonObject
        {
            ["tool"] = tool,
            ["artifactId"] = $"mcp-{tool}"
        }).ToArray());

    private static string CreateSuccessfulCall(string tool)
        => JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = tool,
            result = new { isError = false, structuredContent = new { success = true } },
            semanticPostcondition = new { passed = true }
        });

    private static string CreateRuntimeInventory()
        => JsonSerializer.Serialize(new
        {
            checkpoints = new[]
            {
                new
                {
                    name = "browse",
                    controls = new[]
                    {
                        RuntimeControl("ResultsList", "ListView"),
                        RuntimeControl("PrimaryAction", "Button")
                    }
                }
            }
        });

    private static object RuntimeControl(string id, string kind)
        => new
        {
            id,
            controlKind = kind,
            origin = "app-authored",
            identityKind = "x:Name",
            visible = true,
            enabled = true,
            hitTestable = true,
            loaded = true
        };

    private static string CreateBindingEvidence(string id, string kind, params string[] properties)
        => JsonSerializer.Serialize(new
        {
            controlId = id,
            controlKind = kind,
            bindings = properties.Select(property => new { property, status = "Active" })
        });

    private static string CreateInteractionState(string selection, string feedback)
        => JsonSerializer.Serialize(new
        {
            controls = new object[]
            {
                new
                {
                    id = "ResultsList",
                    controlKind = "ListView",
                    state = new { semanticValue = selection, viewModelValue = selection }
                },
                new
                {
                    id = "PrimaryAction",
                    controlKind = "Button",
                    state = new { semanticValue = feedback, visibleFeedback = feedback, viewModelValue = feedback }
                }
            }
        });

    private static string CreateInteractionActions()
        => JsonSerializer.Serialize(new
        {
            actions = new object[]
            {
                SuccessfulAction("ResultsList", "select_item"),
                SuccessfulAction("PrimaryAction", "invoke")
            }
        });

    private static object SuccessfulAction(string controlId, string tool)
        => new
        {
            id = controlId,
            transport = "mcp-native",
            tool,
            result = new { isError = false, structuredContent = new { success = true } }
        };
}
