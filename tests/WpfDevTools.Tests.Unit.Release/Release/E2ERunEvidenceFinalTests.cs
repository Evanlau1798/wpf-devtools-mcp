using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class E2ERunEvidenceFinalTests
{
    [Fact]
    public void Final_ShouldRequirePreJudgeReceipt()
    {
        using var fixture = new E2ERunEvidenceFixture();

        var result = E2ERunEvidenceFixture.Run(fixture, "Final");

        result.ExitCode.Should().NotBe(0);
        using var decision = JsonDocument.Parse(File.ReadAllText(fixture.DecisionPath));
        DecisionReasons(decision.RootElement).Should()
            .Contain(reason => reason.Contains("PreJudge receipt", StringComparison.Ordinal));
    }

    [Fact]
    public void Final_ShouldWriteExactPassingDecision()
    {
        using var fixture = new E2ERunEvidenceFixture();

        var result = E2ERunEvidenceFixture.RunFinal(fixture);

        result.ExitCode.Should().Be(0, result.Stderr + result.Stdout);
        using var decision = JsonDocument.Parse(File.ReadAllText(fixture.DecisionPath));
        var root = decision.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "runnerCompleted",
            "operationalGatesPassed",
            "visualQualified",
            "overallResult",
            "reasons",
            "repairBudgetExhausted");
        root.GetProperty("runnerCompleted").GetBoolean().Should().BeTrue();
        root.GetProperty("operationalGatesPassed").GetBoolean().Should().BeTrue();
        root.GetProperty("visualQualified").GetBoolean().Should().BeTrue();
        root.GetProperty("overallResult").GetString().Should().Be("PASS");
        root.GetProperty("repairBudgetExhausted").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Final_ShouldRejectChangedVisualContractHashOnRepairAttempt()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.AddSecondAttempt(new string('b', 64));

        var decision = RunFailedFinal(fixture);

        DecisionReasons(decision).Should().Contain(reason => reason.Contains("contract hash changed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Final_ShouldRejectReportMissingRequiredImage()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.SetArtifactText("report", "![reference](attempts/1/inputs/reference.png)\n");

        var decision = RunFailedFinal(fixture);

        DecisionReasons(decision).Should().Contain(reason => reason.Contains("report", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Final_ShouldFailVisualGateWhenRunnerExitedZero()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.SetJudgeScore(9.5);

        var decision = RunFailedFinal(fixture);

        decision.GetProperty("runnerCompleted").GetBoolean().Should().BeTrue();
        decision.GetProperty("operationalGatesPassed").GetBoolean().Should().BeTrue();
        decision.GetProperty("visualQualified").GetBoolean().Should().BeFalse();
        decision.GetProperty("overallResult").GetString().Should().Be("FAIL");
    }

    [Fact]
    public void Final_ShouldMarkRepairBudgetExhaustedAfterSecondVisualFailure()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.AddSecondAttempt(secondScore: 9.4);

        var decision = RunFailedFinal(fixture);

        decision.GetProperty("operationalGatesPassed").GetBoolean().Should().BeTrue();
        decision.GetProperty("visualQualified").GetBoolean().Should().BeFalse();
        decision.GetProperty("repairBudgetExhausted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Final_ShouldRejectSecondAttemptWhenFirstAlreadyQualified()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.AddSecondAttempt(firstScore: 9.8, secondScore: 9.8);

        var decision = RunFailedFinal(fixture);

        DecisionReasons(decision).Should()
            .Contain(reason => reason.Contains("attempt 1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Final_ShouldRejectInvalidFirstJudgeResultBeforeSecondAttempt()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.AddSecondAttempt();
        fixture.SetArtifactText("judgeResult", "{}");

        var decision = RunFailedFinal(fixture);

        DecisionReasons(decision).Should()
            .Contain(reason => reason.Contains("mode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Final_ShouldRejectFailedCleanupGate()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.Mutate(manifest => manifest["cleanup"]!["passed"] = false);

        var decision = RunFailedFinal(fixture);

        DecisionReasons(decision).Should().Contain(reason => reason.Contains("cleanup", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Final_ShouldRejectImageMappingHashMismatch()
    {
        using var fixture = new E2ERunEvidenceFixture();
        var referenceLength = new FileInfo(fixture.GetArtifactPath("attemptReference")).Length;
        var candidateLength = new FileInfo(fixture.GetArtifactPath("attemptCandidate")).Length;
        fixture.SetArtifactText(
            "inputMapping",
            $$"""
            {"schemaVersion":"wpfdevtools.e2e-visual-judge-inputs.v1","mode":"reference","images":[
              {"role":"reference","sourceArtifactId":"referenceImage","frozenPath":"inputs/reference.png","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","byteLength":{{referenceLength}}},
              {"role":"candidate","sourceArtifactId":"candidateImage","frozenPath":"inputs/candidate.png","sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","byteLength":{{candidateLength}}}
            ]}
            """);

        var decision = RunFailedFinal(fixture);

        DecisionReasons(decision).Should().Contain(reason => reason.Contains("mapping", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Final_ShouldRejectOperationalEvidenceChangedAfterPreJudge()
    {
        using var fixture = new E2ERunEvidenceFixture();
        E2ERunEvidenceFixture.Run(fixture, "PreJudge").ExitCode.Should().Be(0);
        fixture.Mutate(manifest => manifest["release"]!["tag"] = "v1.0.0-tampered");

        var result = E2ERunEvidenceFixture.Run(fixture, "Final");

        result.ExitCode.Should().NotBe(0);
        using var decision = JsonDocument.Parse(File.ReadAllText(fixture.DecisionPath));
        DecisionReasons(decision.RootElement).Should()
            .Contain(reason => reason.Contains("receipt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Final_ShouldRequirePostJudgeArtifacts()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.Mutate(manifest => manifest["attempts"]![0]!.AsObject().Remove("imageMappingArtifactId"));

        var decision = RunFailedFinal(fixture);

        DecisionReasons(decision).Should().Contain(reason => reason.Contains("imageMappingArtifactId", StringComparison.Ordinal));
    }

    [Fact]
    public void Final_ShouldWriteFixedDecisionForMalformedManifest()
    {
        using var fixture = new E2ERunEvidenceFixture();
        E2ERunEvidenceFixture.Run(fixture, "PreJudge").ExitCode.Should().Be(0);
        File.WriteAllText(fixture.ManifestPath, "{");

        var result = E2ERunEvidenceFixture.Run(fixture, "Final");

        result.ExitCode.Should().NotBe(0);
        AssertFixedFailDecision(fixture.DecisionPath);
    }

    [Fact]
    public void Final_ShouldWriteFixedDecisionWhenAttemptsAreMissing()
    {
        using var fixture = new E2ERunEvidenceFixture();
        E2ERunEvidenceFixture.Run(fixture, "PreJudge").ExitCode.Should().Be(0);
        fixture.Mutate(manifest => manifest.Remove("attempts"));

        var result = E2ERunEvidenceFixture.Run(fixture, "Final");

        result.ExitCode.Should().NotBe(0);
        AssertFixedFailDecision(fixture.DecisionPath);
    }

    private static JsonElement RunFailedFinal(E2ERunEvidenceFixture fixture)
    {
        var result = E2ERunEvidenceFixture.RunFinal(fixture);
        result.ExitCode.Should().NotBe(0);
        File.Exists(fixture.DecisionPath).Should().BeTrue(result.Stderr);
        return JsonDocument.Parse(File.ReadAllText(fixture.DecisionPath)).RootElement.Clone();
    }

    private static string[] DecisionReasons(JsonElement decision)
        => decision.GetProperty("reasons").EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static void AssertFixedFailDecision(string path)
    {
        File.Exists(path).Should().BeTrue();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "runnerCompleted", "operationalGatesPassed", "visualQualified",
            "overallResult", "reasons", "repairBudgetExhausted");
        root.GetProperty("overallResult").GetString().Should().Be("FAIL");
        root.GetProperty("reasons").GetArrayLength().Should().BeGreaterThan(0);
    }
}
