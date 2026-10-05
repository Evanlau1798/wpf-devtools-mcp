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
            result = new
            {
                isError = false,
                structuredContent = new
                {
                    success = true,
                    snapshotId = "snapshot-generic",
                    propertyChanges = new[] { new { elementId = "ResultsList", propertyName = "SelectedIndex", beforeValue = "0", afterValue = "1" } },
                    viewModelChanges = new[] { new { elementId = "ResultsList", propertyName = "Status", beforeValue = "Idle", afterValue = "Completed" } },
                    newBindingErrors = Array.Empty<object>(),
                    resolvedBindingErrors = Array.Empty<object>(),
                    validationChanges = Array.Empty<object>(),
                    focusChange = new { changed = false }
                }
            }
        }));
        WriteArtifact("stateRestore", "state/restore.json", JsonSerializer.Serialize(new
        {
            result = new
            {
                isError = false,
                structuredContent = new
                {
                    success = true,
                    restoredDependencyPropertyCount = 1,
                    restoredDependencyProperties = new[] { new { propertyName = "SelectedIndex", verified = true, expectedValue = "0", currentValue = "0" } },
                    skippedDependencyPropertyCount = 0,
                    skippedDependencyProperties = Array.Empty<object>(),
                    restoredViewModelPropertyCount = 1,
                    restoredViewModelProperties = new[] { new { propertyName = "Status", verified = true, expectedValue = "Idle", currentValue = "Idle" } },
                    skippedViewModelPropertyCount = 0,
                    skippedViewModelProperties = Array.Empty<object>(),
                    restoredFocus = true,
                    warnings = Array.Empty<object>()
                }
            },
            readback = new
            {
                matchesBaseline = true,
                baseline = new { selection = new { SelectedIndex = "0" }, state = new { Status = "Idle" }, focus = new { focusedElementId = "ResultsList" } },
                actual = new { selection = new { SelectedIndex = "0" }, state = new { Status = "Idle" }, focus = new { focusedElementId = "ResultsList" } }
            }
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

    // Synthetic protocol fixtures, not evidence of an actual E2E execution.
    private void InitializeStateProvenance()
    {
        var requests = new List<JsonObject>();
        var responses = new List<JsonObject>();
        var provenance = new JsonObject();
        foreach (var phase in new[] { "baseline", "actual" })
        {
            if (phase == "actual")
            {
                foreach (var (id, tool) in new[] { ("stateDiff", "get_state_diff"), ("stateRestore", "restore_state_snapshot") })
                {
                    var original = JsonNode.Parse(File.ReadAllText(GetArtifactPath(id)))!.AsObject();
                    var call = CreateStateRpcCall(tool, requests.Count + 1, original["result"]!.DeepClone().AsObject(), requests, responses);
                    if (id == "stateRestore") { call["readback"] = original["readback"]!.DeepClone(); }
                    SetArtifactText(id, call.ToJsonString());
                }
            }
            var groups = new JsonObject();
            foreach (var category in new[] { "selection", "state", "focus" })
            {
                var tool = category switch
                {
                    "selection" => "get_element_snapshot",
                    "state" => "get_viewmodel",
                    _ => "get_focus_state"
                };
                var (property, pointer, content) = category switch
                {
                    "selection" => ("SelectedIndex", "/structuredContent/properties/SelectedIndex/currentValue",
                        """{"success":true,"properties":{"SelectedIndex":{"currentValue":"0"}}}"""),
                    "state" => ("Status", "/structuredContent/properties/0/value",
                        """{"success":true,"properties":[{"name":"Status","value":"Idle"}]}"""),
                    _ => ("focusedElementId", "/structuredContent/focusedElementId",
                        """{"success":true,"focusedElementId":"ResultsList"}""")
                };
                var id = $"{phase}-{category}";
                var result = new JsonObject { ["isError"] = false, ["structuredContent"] = JsonNode.Parse(content) };
                var call = CreateStateRpcCall(tool, requests.Count + 1, result, requests, responses);
                WriteArtifact(id, $"state/{id}.json", call.ToJsonString());
                groups[category] = new JsonObject
                {
                    [property] = new JsonObject { ["callArtifactId"] = id, ["valuePointer"] = pointer }
                };
            }
            provenance[phase] = groups;
        }
        var restore = JsonNode.Parse(File.ReadAllText(GetArtifactPath("stateRestore")))!;
        restore["readback"]!["provenance"] = provenance;
        SetArtifactText("stateRestore", restore.ToJsonString());
        WriteArtifact("stateRpcRequests", "state/raw-requests.jsonl", string.Join('\n', requests.Select(item => item.ToJsonString())) + "\n");
        WriteArtifact("stateRpcResponses", "state/raw-responses.jsonl", string.Join('\n', responses.Select(item => item.ToJsonString())) + "\n");
        var events = requests.Zip(responses).SelectMany((pair, index) => new[]
        {
            new JsonObject { ["sequence"] = index * 2 + 1, ["direction"] = "sent", ["message"] = pair.First.DeepClone() },
            new JsonObject { ["sequence"] = index * 2 + 2, ["direction"] = "received", ["message"] = pair.Second.DeepClone() }
        });
        WriteArtifact("stateRpcTransport", "state/transport.jsonl", string.Join('\n', events.Select(item => item.ToJsonString())) + "\n");
        Mutate(manifest => manifest["artifacts"] = new JsonArray(Artifacts.Select(item => item.DeepClone()).ToArray()));
    }

    private static JsonObject CreateStateRpcCall(string tool, int requestId, JsonObject result,
        List<JsonObject> requests, List<JsonObject> responses)
    {
        var arguments = tool is "get_state_diff" or "restore_state_snapshot"
            ? new JsonObject { ["snapshotId"] = "snapshot-generic" }
            : tool == "get_focus_state" ? new JsonObject() : new JsonObject { ["elementId"] = "ResultsList" };
        requests.Add(new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = requestId, ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = arguments }
        });
        responses.Add(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = requestId, ["result"] = result.DeepClone() });
        return new JsonObject
        {
            ["schemaVersion"] = "wpfdevtools.e2e-mcp-call-evidence.v1", ["tool"] = tool,
            ["requestId"] = requestId, ["result"] = result,
            ["rawRpc"] = new JsonObject { ["requestsArtifactId"] = "stateRpcRequests", ["responsesArtifactId"] = "stateRpcResponses", ["transportArtifactId"] = "stateRpcTransport" }
        };
    }

    internal void SynchronizeStateResponse(JsonObject call)
    {
        var lines = File.ReadAllLines(GetArtifactPath("stateRpcResponses"))
            .Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        var response = lines.Single(item => JsonNode.DeepEquals(item["id"], call["requestId"]));
        response["result"] = call["result"]!.DeepClone();
        SetArtifactText("stateRpcResponses", string.Join('\n', lines.Select(item => item.ToJsonString())) + "\n");
        SynchronizeTransportMessage(call["requestId"]!, call["result"]!, "received", "result");
    }

    internal void SynchronizeTransportMessage(JsonNode id, JsonNode value, string direction, string field)
    {
        var events = File.ReadAllLines(GetArtifactPath("stateRpcTransport"))
            .Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        var match = events.Single(item => item["direction"]!.GetValue<string>() == direction &&
            JsonNode.DeepEquals(item["message"]!["id"], id));
        match["message"]![field] = value.DeepClone();
        SetArtifactText("stateRpcTransport", string.Join('\n', events.Select(item => item.ToJsonString())) + "\n");
    }
}
