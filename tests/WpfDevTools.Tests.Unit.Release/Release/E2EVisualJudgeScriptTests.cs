using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class E2EVisualJudgeScriptTests
{
    [Fact]
    public void ValidateOnly_ShouldApplyBlindVisualQualityThresholds()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var clean = RunDecision(
                tempRoot,
                "clean",
                CreateJudgeResult(9.8, referenceScore: 9.7),
                "reference");
            clean.GetProperty("qualified").GetBoolean().Should().BeTrue();
            clean.GetProperty("visualQuality").GetDouble().Should().Be(9.8);
            clean.GetProperty("referenceFidelity").GetDouble().Should().Be(9.7);

            var material = RunDecision(
                tempRoot,
                "material",
                CreateJudgeResult(
                    9.9,
                    referenceScore: 9.9,
                    defects:
                    [
                        new
                        {
                            severity = "material",
                            category = "layout",
                            evidence = "A large accidental empty region separates the primary surfaces.",
                            bounds = new { x = 0.1, y = 0.4, width = 0.8, height = 0.3 }
                        }
                    ]),
                "reference");
            material.GetProperty("qualified").GetBoolean().Should().BeFalse();
            material.GetProperty("visualQuality").GetDouble().Should().Be(9.5);
            material.GetProperty("referenceFidelity").GetDouble().Should().Be(9.5);
            material.GetProperty("severityCap").GetDouble().Should().Be(9.5);

            var blocking = RunDecision(
                tempRoot,
                "blocking",
                CreateJudgeResult(
                    9.9,
                    referenceScore: null,
                    mode: "standalone",
                    defects:
                    [
                        new
                        {
                            severity = "blocking",
                            category = "readability",
                            evidence = "The selected label is unreadable against its background.",
                            bounds = new { x = 0.2, y = 0.1, width = 0.2, height = 0.05 }
                        }
                    ]),
                "standalone");
            blocking.GetProperty("qualified").GetBoolean().Should().BeFalse();
            blocking.GetProperty("visualQuality").GetDouble().Should().Be(9.0);
            blocking.GetProperty("referenceFidelity").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ValidateOnly_ShouldRejectModeThatDoesNotMatchImageInputs()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var resultPath = Path.Combine(tempRoot, "judge.json");
            var decisionPath = Path.Combine(tempRoot, "decision.json");
            File.WriteAllText(resultPath, JsonSerializer.Serialize(
                CreateJudgeResult(9.9, referenceScore: null, mode: "standalone")));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                VisualJudgeScriptPath,
                [
                    "-JudgeResultPath", resultPath,
                    "-DecisionPath", decisionPath,
                    "-ExpectedMode", "reference",
                    "-ValidateOnly"
                ]);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("does not match");
            File.Exists(decisionPath).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Invocation_ShouldUseBlindInputsAndFailClosedOnStaleOrToolEvents()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var referencePath = Path.Combine(tempRoot, "reference.png");
            var finalPath = Path.Combine(tempRoot, "final.png");
            File.WriteAllBytes(referencePath, [137, 80, 78, 71]);
            File.WriteAllBytes(finalPath, [137, 80, 78, 71]);

            var cleanFake = WriteFakeCodex(tempRoot, "clean", forbiddenToolEvent: false);
            var cleanEvidence = Path.Combine(tempRoot, "clean-evidence");
            var cleanResult = ReleaseScriptTestHarness.RunPowerShellScript(
                VisualJudgeScriptPath,
                [
                    "-FinalImagePath", finalPath,
                    "-ReferenceImagePath", referencePath,
                    "-EvidenceRoot", cleanEvidence,
                    "-CodexExecutable", cleanFake
                ]);

            cleanResult.ExitCode.Should().Be(0, cleanResult.Stderr);
            using var arguments = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(tempRoot, "clean-arguments.json")));
            var values = arguments.RootElement.EnumerateArray().Select(value => value.GetString()!).ToArray();
            Array.IndexOf(values, referencePath).Should().BeLessThan(Array.IndexOf(values, finalPath));
            values.Should().Contain(["--ignore-user-config", "--ignore-rules", "--ephemeral"]);
            values[1].Should().NotContain("9.5");
            var judgeWorkingDirectory = values[Array.IndexOf(values, "--cd") + 1];
            judgeWorkingDirectory.Should().StartWith(Path.GetTempPath());
            judgeWorkingDirectory.StartsWith(
                    ReleaseScriptTestHarness.GetRepoFilePath("."),
                    StringComparison.OrdinalIgnoreCase)
                .Should().BeFalse();

            var staleEvidence = Path.Combine(tempRoot, "stale-evidence");
            Directory.CreateDirectory(staleEvidence);
            File.WriteAllText(Path.Combine(staleEvidence, "visual-judge-result.json"), "{}");
            var staleResult = ReleaseScriptTestHarness.RunPowerShellScript(
                VisualJudgeScriptPath,
                [
                    "-FinalImagePath", finalPath,
                    "-EvidenceRoot", staleEvidence,
                    "-CodexExecutable", cleanFake
                ]);
            staleResult.ExitCode.Should().NotBe(0);
            staleResult.Stderr.Should().Contain("already exists");

            var forbiddenFake = WriteFakeCodex(tempRoot, "forbidden", forbiddenToolEvent: true);
            var forbiddenEvidence = Path.Combine(tempRoot, "forbidden-evidence");
            var forbiddenResult = ReleaseScriptTestHarness.RunPowerShellScript(
                VisualJudgeScriptPath,
                [
                    "-FinalImagePath", finalPath,
                    "-EvidenceRoot", forbiddenEvidence,
                    "-CodexExecutable", forbiddenFake
                ]);
            forbiddenResult.ExitCode.Should().NotBe(0);
            forbiddenResult.Stderr.Should().Contain("tool event");
            File.Exists(Path.Combine(forbiddenEvidence, "visual-judge-decision.json")).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static readonly string VisualJudgeScriptPath =
        ReleaseScriptTestHarness.GetRepoFilePath("scripts/e2e/Invoke-E2EVisualJudge.ps1");

    private static JsonElement RunDecision(
        string tempRoot,
        string name,
        object judgeResult,
        string expectedMode)
    {
        var resultPath = Path.Combine(tempRoot, name + "-judge.json");
        var decisionPath = Path.Combine(tempRoot, name + "-decision.json");
        File.WriteAllText(resultPath, JsonSerializer.Serialize(judgeResult));

        var result = ReleaseScriptTestHarness.RunPowerShellScript(
            VisualJudgeScriptPath,
            [
                "-JudgeResultPath", resultPath,
                "-DecisionPath", decisionPath,
                "-ExpectedMode", expectedMode,
                "-ValidateOnly"
            ]);

        result.ExitCode.Should().Be(0, result.Stderr);
        File.Exists(decisionPath).Should().BeTrue();
        return JsonDocument.Parse(File.ReadAllText(decisionPath)).RootElement.Clone();
    }

    private static string WriteFakeCodex(string tempRoot, string name, bool forbiddenToolEvent)
    {
        var scriptPath = Path.Combine(tempRoot, name + "-codex.ps1");
        var argumentsPath = Path.Combine(tempRoot, name + "-arguments.json");
        var eventLine = forbiddenToolEvent
            ? """'{"type":"item.completed","item":{"type":"command_execution"}}'"""
            : """'{"type":"item.completed","item":{"type":"agent_message"}}'""";
        var escapedArgumentsPath = argumentsPath.Replace("'", "''", StringComparison.Ordinal);
        File.WriteAllText(
            scriptPath,
            string.Join(
                Environment.NewLine,
                "[System.IO.File]::WriteAllText(",
                $"    '{escapedArgumentsPath}',",
                "    ($args | ConvertTo-Json),",
                "    [System.Text.UTF8Encoding]::new($false))",
                "$outputIndex = [Array]::IndexOf($args, '--output-last-message')",
                "$resultPath = $args[$outputIndex + 1]",
                """$result = '{"mode":"reference","qualityAxes":{"layoutBalance":9.8,"visualHierarchy":9.8,"readabilityContrast":9.8,"controlStateCoherence":9.8,"visualPolish":9.8},"referenceAxes":{"regionGeometry":9.8,"densityRhythm":9.8,"navigationBrowseRhythm":9.8,"mediaCardComposition":9.8},"defects":[],"summary":"Clean image-grounded result."}'""",
                "[System.IO.File]::WriteAllText($resultPath, $result, [System.Text.UTF8Encoding]::new($false))",
                """'{"type":"thread.started","thread_id":"fake"}'""",
                """'{"type":"turn.started"}'""",
                eventLine,
                """'{"type":"turn.completed","usage":{"input_tokens":100,"output_tokens":20}}'""",
                "$global:LASTEXITCODE = 0"));
        return scriptPath;
    }

    private static object CreateJudgeResult(
        double qualityScore,
        double? referenceScore,
        string mode = "reference",
        object[]? defects = null)
        => new
        {
            mode,
            qualityAxes = new
            {
                layoutBalance = qualityScore,
                visualHierarchy = qualityScore,
                readabilityContrast = qualityScore,
                controlStateCoherence = qualityScore,
                visualPolish = qualityScore
            },
            referenceAxes = referenceScore.HasValue
                ? new
                {
                    regionGeometry = referenceScore.Value,
                    densityRhythm = referenceScore.Value,
                    navigationBrowseRhythm = referenceScore.Value,
                    mediaCardComposition = referenceScore.Value
                }
                : null,
            defects = defects ?? [],
            summary = "Image-grounded visual review."
        };
}
