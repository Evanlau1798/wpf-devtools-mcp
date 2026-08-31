using System.Text;
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
    public void PreJudge_ShouldWriteDurableReceipt()
    {
        using var fixture = new E2ERunEvidenceFixture();

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().Be(0, result.Stderr);
        File.Exists(Path.Combine(fixture.Root, "prejudge-receipt.json")).Should().BeTrue();
    }

    [Fact]
    public void PreJudge_ShouldNotRequirePostJudgeArtifacts()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.Mutate(manifest =>
        {
            var attempt = manifest["attempts"]![0]!.AsObject();
            attempt.Remove("judgeResultArtifactId");
            attempt.Remove("imageMappingArtifactId");
        });

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().Be(0, result.Stderr);
    }

    [Fact]
    public void PreJudge_ShouldRejectMissingBindingProof()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.SetArtifactText(
            "resultsListBindings",
            """
            {"controlId":"ResultsList","controlKind":"ListView","bindings":[
              {"property":"ItemsSource","status":"Active"}
            ]}
            """);

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
        fixture.SetArtifactText(
            "mcp-connect",
            """
            {"jsonrpc":"2.0","id":"connect","result":{"isError":true,"structuredContent":{"success":false}},"semanticPostcondition":{"passed":false}}
            """);

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

    [Fact]
    public void PreJudge_ShouldRejectInvalidRunnerJsonl()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.SetArtifactText("runnerEvents", "{\"type\":\"run.started\"}\nnot-json\n");

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("JSONL");
    }

    [Fact]
    public void PreJudge_ShouldRejectInvalidUtf8RunnerJsonl()
    {
        using var fixture = new E2ERunEvidenceFixture();
        var prefix = Encoding.UTF8.GetBytes("{\"type\":\"run.completed\",\"completed\":true,\"exitCode\":0,\"note\":\"");
        var suffix = Encoding.UTF8.GetBytes("\"}\n");
        fixture.SetArtifactBytes("runnerEvents", [.. prefix, 0xff, .. suffix]);

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("UTF-8");
    }

    [Theory]
    [InlineData("{\"type\":\"run.started\"}\n", "terminal")]
    [InlineData("{\"type\":\"run.completed\",\"completed\":true,\"exitCode\":1}\n", "manifest")]
    public void PreJudge_ShouldRejectMissingOrMismatchedTerminalRunnerEvent(
        string events,
        string expectedError)
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.SetArtifactText("runnerEvents", events);

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().ContainEquivalentOf(expectedError);
    }

    [Fact]
    public void PreJudge_ShouldRejectHeaderOnlyPng()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.SetArtifactBytes("referenceImage", CreateHeaderOnlyPng(1920, 1215));

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("PNG");
    }

    [Fact]
    public void PreJudge_ShouldRejectAttemptImageWithDecoyViewport()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.SetArtifactBytes("attemptReference", CreateHeaderOnlyPng(100, 100));

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().ContainEquivalentOf("attempt");
    }

    [Fact]
    public void PreJudge_ShouldRejectFrozenImageUnboundFromPreparedSource()
    {
        using var fixture = new E2ERunEvidenceFixture();
        var bytes = File.ReadAllBytes(fixture.GetArtifactPath("attemptReference"));
        fixture.SetArtifactBytes("attemptReference", [.. bytes, 0]);
        fixture.RefreshInputMapping();

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().ContainEquivalentOf("canonical source");
    }

    [Fact]
    public void PreJudge_ShouldRejectPlaceholderRuntimeArtifacts()
    {
        using var fixture = new E2ERunEvidenceFixture();

        fixture.SetArtifactText("interactionBefore", "{}");

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("controls");
    }

    [Fact]
    public void PreJudge_ShouldRejectControlOmittedFromCheckpointAndInventory()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.Mutate(manifest =>
        {
            manifest["interactive"]!["checkpoints"]![0]!["controls"]!.AsArray().RemoveAt(1);
            manifest["interactive"]!["inventory"]!.AsArray().RemoveAt(1);
        });

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("runtime inventory");
    }

    [Fact]
    public void PreJudge_ShouldRejectCheckpointInventoryKindMismatch()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.Mutate(manifest =>
            manifest["interactive"]!["inventory"]![0]!["controlKind"] = "ComboBox");

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("controlKind");
    }

    [Fact]
    public void PreJudge_ShouldRejectFailedRuntimeAction()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.SetArtifactText(
            "interactionAction",
            """
            {"actions":[
              {"id":"ResultsList","transport":"mcp-native","tool":"select_item","result":{"isError":true,"structuredContent":{"success":false}}},
              {"id":"PrimaryAction","transport":"mcp-native","tool":"invoke","result":{"isError":false,"structuredContent":{"success":true}}}
            ]}
            """);

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("successful MCP tool result");
    }

    [Fact]
    public void PreJudge_ShouldRejectPlaceholderStateDiff()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.SetArtifactText("stateDiff", "{}");

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("result");
    }

    [Fact]
    public void PreJudge_ShouldRejectRestoreWithoutMatchingReadback()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.SetArtifactText(
            "stateRestore",
            """
            {"result":{"isError":false,"structuredContent":{"success":true,"restoredSelection":true,"restoredState":true,"restoredFocus":true}},"readback":{"matchesBaseline":false}}
            """);

        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("readback");
    }

    private static byte[] CreateHeaderOnlyPng(int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    private static void WriteBigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }
}
