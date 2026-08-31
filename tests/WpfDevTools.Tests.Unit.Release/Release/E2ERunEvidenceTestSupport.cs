using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WpfDevTools.Tests.Unit.Release;

internal sealed class E2ERunEvidenceFixture : IDisposable
{
    public E2ERunEvidenceFixture()
    {
        Root = ReleaseScriptTestHarness.CreateTempDirectory();
        ManifestPath = Path.Combine(Root, "e2e-run-evidence.json");
        DecisionPath = Path.Combine(Root, "final-decision.json");
        WriteArtifact("runnerEvents", "runner/events.jsonl", "{\"type\":\"run.completed\"}\n");
        WritePngArtifact("referenceImage", "visual/reference.png", 1920, 1215);
        WritePngArtifact("candidateImage", "visual/candidate.png", 1920, 1215);
        WritePngArtifact("attemptReference", "attempts/1/inputs/reference.png", 1920, 1215);
        WritePngArtifact("attemptCandidate", "attempts/1/inputs/candidate.png", 1920, 1215);
        WriteArtifact("visualContract", "visual/contract.json", "{\"contract\":\"v1\"}");
        WriteArtifact("interactionBefore", "interaction/before.json", "{}");
        WriteArtifact("interactionAction", "interaction/action.json", "{}");
        WriteArtifact("interactionAfter", "interaction/after.json", "{}");
        WriteArtifact("stateDiff", "state/diff.json", "{}");
        WriteArtifact("stateRestore", "state/restore.json", "{}");
        WriteArtifact("cleanup", "cleanup/result.json", "{\"passed\":true}");
        WriteArtifact("judgeResult", "attempts/1/judge-result.json", CreateJudgeResult(9.8));
        WriteArtifact("inputMapping", "attempts/1/visual-judge-inputs.json", CreateInputMapping());
        WriteArtifact(
            "report",
            "report.md",
            "![reference](attempts/1/inputs/reference.png)\n" +
            "![candidate](attempts/1/inputs/candidate.png)\n");
        Save(CreateManifest());
    }

    public string Root { get; }

    public string ManifestPath { get; }

    public string DecisionPath { get; }

    public JsonObject Manifest => JsonNode.Parse(File.ReadAllText(ManifestPath))!.AsObject();

    public void Mutate(Action<JsonObject> mutate)
    {
        var manifest = Manifest;
        mutate(manifest);
        Save(manifest);
    }

    public string GetArtifactPath(string id)
    {
        var artifact = Manifest["artifacts"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(item => item["id"]!.GetValue<string>() == id);
        return Path.Combine(Root, artifact["path"]!.GetValue<string>());
    }

    public void SetArtifactText(string id, string content, bool includeBom = false)
    {
        var path = GetArtifactPath(id);
        File.WriteAllText(path, content, new UTF8Encoding(includeBom));
        Mutate(manifest =>
        {
            var artifact = manifest["artifacts"]!.AsArray()
                .Select(node => node!.AsObject())
                .Single(item => item["id"]!.GetValue<string>() == id);
            artifact["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        });
    }

    public void SetJudgeScore(double score) => SetArtifactText("judgeResult", CreateJudgeResult(score));

    public void AddSecondAttempt(string? contractHash = null)
    {
        Mutate(manifest =>
        {
            var first = manifest["attempts"]![0]!.AsObject();
            manifest["attempts"]!.AsArray().Add(new JsonObject
            {
                ["number"] = 2,
                ["repairKind"] = "aesthetic",
                ["visualContractHash"] = contractHash ?? manifest["visualContractHash"]!.GetValue<string>(),
                ["referenceArtifactId"] = first["referenceArtifactId"]!.GetValue<string>(),
                ["candidateArtifactId"] = first["candidateArtifactId"]!.GetValue<string>(),
                ["judgeResultArtifactId"] = first["judgeResultArtifactId"]!.GetValue<string>(),
                ["imageMappingArtifactId"] = first["imageMappingArtifactId"]!.GetValue<string>()
            });
        });
    }

    public static (int ExitCode, string Stdout, string Stderr) Run(
        E2ERunEvidenceFixture fixture,
        string phase)
    {
        var script = ReleaseScriptTestHarness.GetRepoFilePath("scripts/e2e/Test-E2ERunEvidence.ps1");
        return RunPwshScript(
            script,
            [
                "-Phase", phase,
                "-EvidenceRoot", fixture.Root,
                "-ManifestPath", fixture.ManifestPath,
                "-DecisionPath", fixture.DecisionPath
            ]);
    }

    public static (int ExitCode, string Stdout, string Stderr) RunPwshScript(
        string script,
        IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = ReleaseScriptTestHarness.GetRepoFilePath(".")
        };
        foreach (var argument in new[] { "-NoProfile", "-File", script }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    public void Dispose() => ReleaseScriptTestHarness.DeleteDirectory(Root);

    private JsonObject CreateManifest()
        => new()
        {
            ["schemaVersion"] = "wpfdevtools.e2e-run-evidence.v1",
            ["release"] = new JsonObject
            {
                ["version"] = "1.0.0-canary.1",
                ["tag"] = "v1.0.0-canary.1",
                ["assetName"] = "wpfdevtools-win-x64.zip",
                ["architecture"] = "win-x64",
                ["sourceUrl"] = "https://example.invalid/release",
                ["packageSha256"] = new string('a', 64)
            },
            ["artifacts"] = new JsonArray(Artifacts.Select(item => item.DeepClone()).ToArray()),
            ["runner"] = new JsonObject
            {
                ["completed"] = true,
                ["exitCode"] = 0,
                ["eventsArtifactId"] = "runnerEvents"
            },
            ["viewport"] = new JsonObject
            {
                ["scope"] = "app-window",
                ["referenceArtifactId"] = "referenceImage",
                ["candidateArtifactId"] = "candidateImage",
                ["workAreaWidth"] = 1920,
                ["workAreaHeight"] = 1215
            },
            ["visualContractHash"] = VisualContractHash,
            ["visualContractArtifactId"] = "visualContract",
            ["positiveMcpCalls"] = new JsonArray(
                "connect", "get_active_process", "get_ui_summary", "get_element_snapshot",
                "get_state_diff", "restore_state_snapshot")
                .Select(tool => new JsonObject
                {
                    ["tool"] = tool!.GetValue<string>(),
                    ["isError"] = false,
                    ["structuredContentSuccess"] = true,
                    ["semanticPostcondition"] = true
                }).Aggregate(new JsonArray(), (items, item) => { items.Add(item); return items; }),
            ["previewReadiness"] = new JsonObject
            {
                ["valid"] = true,
                ["buildSucceeded"] = true,
                ["hostStarted"] = true,
                ["screenshotInspectable"] = true,
                ["visualContractPassed"] = true,
                ["inspectionTruncated"] = false,
                ["attentionRequiredCount"] = 0
            },
            ["interactive"] = CreateInteractiveEvidence(),
            ["coreJourney"] = CreateCoreJourney(),
            ["stateSafety"] = new JsonObject
            {
                ["diffSucceeded"] = true,
                ["restoreSucceeded"] = true,
                ["diffArtifactId"] = "stateDiff",
                ["restoreArtifactId"] = "stateRestore"
            },
            ["attempts"] = new JsonArray(new JsonObject
            {
                ["number"] = 1,
                ["repairKind"] = "none",
                ["visualContractHash"] = VisualContractHash,
                ["referenceArtifactId"] = "attemptReference",
                ["candidateArtifactId"] = "attemptCandidate",
                ["judgeResultArtifactId"] = "judgeResult",
                ["imageMappingArtifactId"] = "inputMapping"
            }),
            ["report"] = new JsonObject
            {
                ["artifactId"] = "report",
                ["imageArtifactIds"] = new JsonArray("attemptReference", "attemptCandidate")
            },
            ["cleanup"] = new JsonObject
            {
                ["passed"] = true,
                ["artifactId"] = "cleanup"
            }
        };

    private static JsonObject CreateInteractiveEvidence()
    {
        var controls = new JsonArray(
            CreateCheckpointControl("ResultsList", "ListView"),
            CreateCheckpointControl("PrimaryAction", "Button"));
        return new JsonObject
        {
            ["checkpoints"] = new JsonArray(new JsonObject { ["name"] = "browse", ["controls"] = controls }),
            ["inventory"] = new JsonArray(
                CreateInventoryItem("ResultsList", "ListView", new JsonObject
                {
                    ["kind"] = "selector",
                    ["itemsSourceBound"] = true,
                    ["selectionBound"] = true
                }),
                CreateInventoryItem("PrimaryAction", "Button", new JsonObject
                {
                    ["kind"] = "command",
                    ["commandBound"] = true,
                    ["commandParameterBound"] = true
                }))
        };
    }

    private static JsonObject CreateCheckpointControl(string id, string kind)
        => new()
        {
            ["id"] = id,
            ["controlKind"] = kind,
            ["origin"] = "app-authored",
            ["identityKind"] = "x:Name",
            ["visible"] = true,
            ["enabled"] = true,
            ["hitTestable"] = true,
            ["loaded"] = true
        };

    private static JsonObject CreateInventoryItem(string id, string kind, JsonObject binding)
        => new()
        {
            ["id"] = id,
            ["controlKind"] = kind,
            ["binding"] = binding,
            ["interaction"] = new JsonObject
            {
                ["transport"] = "mcp-native",
                ["beforeArtifactId"] = "interactionBefore",
                ["actionArtifactId"] = "interactionAction",
                ["afterArtifactId"] = "interactionAfter",
                ["semanticPostcondition"] = true
            }
        };

    private static JsonObject CreateCoreJourney()
    {
        var result = new JsonObject();
        foreach (var name in new[]
                 {
                     "sceneLocated", "meaningfulSelection", "detailSelectionVmVerified",
                     "primaryBoundCommandExecuted", "visibleFeedbackVerified", "viewModelFeedbackVerified",
                     "secondaryInteractionVerified", "stateDiffCaptured", "restoreSucceeded",
                     "selectionRestored", "stateRestored", "focusRestored", "remainingControlsSmoked"
                 })
        {
            result[name] = true;
        }
        result["artifactIds"] = new JsonArray("interactionBefore", "interactionAction", "interactionAfter");
        return result;
    }

    private readonly List<JsonObject> Artifacts = [];

    private string VisualContractHash => Artifacts
        .Single(item => item["id"]!.GetValue<string>() == "visualContract")["sha256"]!
        .GetValue<string>();

    private string ArtifactHash(string id) => Artifacts
        .Single(item => item["id"]!.GetValue<string>() == id)["sha256"]!
        .GetValue<string>();

    private string CreateInputMapping()
        => JsonSerializer.Serialize(new
        {
            schemaVersion = "wpfdevtools.e2e-visual-judge-inputs.v1",
            mode = "reference",
            images = new object[]
            {
                new
                {
                    role = "reference",
                    frozenPath = "attempts/1/inputs/reference.png",
                    sha256 = ArtifactHash("attemptReference"),
                    byteLength = 24
                },
                new
                {
                    role = "candidate",
                    frozenPath = "attempts/1/inputs/candidate.png",
                    sha256 = ArtifactHash("attemptCandidate"),
                    byteLength = 24
                }
            }
        });

    private void WriteArtifact(string id, string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        AddArtifact(id, relativePath, path);
    }

    private void WritePngArtifact(string id, string relativePath, int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        AddArtifact(id, relativePath, path);
    }

    private void AddArtifact(string id, string relativePath, string path)
        => Artifacts.Add(new JsonObject
        {
            ["id"] = id,
            ["path"] = relativePath.Replace('\\', '/'),
            ["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
        });

    private static void WriteBigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static string CreateJudgeResult(double score)
        => JsonSerializer.Serialize(new
        {
            mode = "reference",
            qualityAxes = new
            {
                layoutBalance = score,
                visualHierarchy = score,
                readabilityContrast = score,
                controlStateCoherence = score,
                visualPolish = score
            },
            referenceAxes = new
            {
                regionGeometry = score,
                densityRhythm = score,
                navigationBrowseRhythm = score,
                mediaCardComposition = score
            },
            defects = Array.Empty<object>(),
            summary = "Image-grounded visual review."
        });

    private void Save(JsonObject manifest)
        => File.WriteAllText(
            ManifestPath,
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
}
