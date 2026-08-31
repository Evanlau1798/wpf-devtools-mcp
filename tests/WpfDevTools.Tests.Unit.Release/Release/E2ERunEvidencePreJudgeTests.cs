using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class E2ERunEvidencePreJudgeTests
{
    [Fact]
    public void PreJudge_ShouldAcceptCompleteOperationalEvidence()
    {
        using var fixture = new E2ERunEvidenceFixture();

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().Be(0, result.Stderr);
    }

    [Fact]
    public void PreJudge_ShouldRejectMissingBindingProof()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.Mutate(manifest =>
            manifest["interactive"]!["inventory"]![0]!["binding"]!["selectionBound"] = false);

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("binding");
    }

    [Fact]
    public void PreJudge_ShouldRejectEligibleControlMissingFromInventory()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.Mutate(manifest => manifest["interactive"]!["inventory"]!.AsArray().RemoveAt(1));

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("eligible control");
    }

    [Fact]
    public void PreJudge_ShouldRejectPositiveMcpError()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.Mutate(manifest => manifest["positiveMcpCalls"]![0]!["isError"] = true);

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("positive MCP");
    }

    [Fact]
    public void PreJudge_ShouldRejectFailedReadinessGate()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.Mutate(manifest => manifest["previewReadiness"]!["hostStarted"] = false);

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("readiness");
    }

    [Fact]
    public void PreJudge_ShouldRejectArtifactHashMismatch()
    {
        using var fixture = new E2ERunEvidenceFixture();
        File.AppendAllText(fixture.GetArtifactPath("referenceImage"), "tampered");

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("SHA-256");
    }
}
