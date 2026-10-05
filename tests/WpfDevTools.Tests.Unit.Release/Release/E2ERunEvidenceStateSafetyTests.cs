using System.Text.Json.Nodes;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class E2ERunEvidenceStateSafetyTests
{
    [Fact]
    public void PreJudge_ShouldRejectReadIssuedBeforeRestoreResponse()
    {
        using var fixture = new E2ERunEvidenceFixture();
        var events = File.ReadAllLines(fixture.GetArtifactPath("stateRpcTransport"))
            .Select(line => JsonNode.Parse(line)!).ToList();
        var restoreResponse = events.FindIndex(item => item["direction"]!.GetValue<string>() == "received" &&
            item["message"]!["id"]!.GetValue<int>() == 5);
        (events[restoreResponse], events[restoreResponse + 1]) = (events[restoreResponse + 1], events[restoreResponse]);
        for (var i = 0; i < events.Count; i++) { events[i]["sequence"] = i + 1; }
        fixture.SetArtifactText("stateRpcTransport", string.Join('\n', events.Select(item => item.ToJsonString())) + "\n");
        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");
        result.ExitCode.Should().NotBe(0);
    }
    [Fact]
    public void PreJudge_ShouldRejectBaselineCallsReusedAsPostRestoreReadback()
    {
        using var fixture = new E2ERunEvidenceFixture();
        MutateArtifact(fixture, "stateRestore", root =>
            root["readback"]!["provenance"]!["actual"] = root["readback"]!["provenance"]!["baseline"]!.DeepClone());
        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");
        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public void PreJudge_ShouldRejectDifferentSnapshotInRestoreRequest()
    {
        using var fixture = new E2ERunEvidenceFixture();
        var lines = File.ReadAllLines(fixture.GetArtifactPath("stateRpcRequests"))
            .Select(line => JsonNode.Parse(line)!).ToArray();
        lines.Single(line => line["params"]!["name"]!.GetValue<string>() == "restore_state_snapshot")
            ["params"]!["arguments"]!["snapshotId"] = "other-snapshot";
        fixture.SetArtifactText("stateRpcRequests", string.Join('\n', lines.Select(line => line.ToJsonString())) + "\n");
        var restoreRequest = lines.Single(line => line["params"]!["name"]!.GetValue<string>() == "restore_state_snapshot");
        fixture.SynchronizeTransportMessage(restoreRequest["id"]!, restoreRequest["params"]!, "sent", "params");
        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");
        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public void PreJudge_ShouldRejectStateWithoutRawProvenance()
    {
        using var fixture = new E2ERunEvidenceFixture();
        MutateArtifact(fixture, "stateDiff", root => root.Remove("rawRpc"));
        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");
        result.ExitCode.Should().NotBe(0);
    }

    [Theory]
    [InlineData("tool", "\"get_viewmodel\"")]
    [InlineData("requestId", "99")]
    public void PreJudge_ShouldRejectIncorrectRawCallIdentity(string field, string json)
    {
        using var fixture = new E2ERunEvidenceFixture();
        var call = JsonNode.Parse(File.ReadAllText(fixture.GetArtifactPath("stateDiff")))!;
        call[field] = JsonNode.Parse(json);
        fixture.SetArtifactText("stateDiff", call.ToJsonString());
        Reject(fixture, "provenance");
    }

    [Fact]
    public void PreJudge_ShouldRejectResultDifferentFromRawResponse()
    {
        using var fixture = new E2ERunEvidenceFixture();
        var call = JsonNode.Parse(File.ReadAllText(fixture.GetArtifactPath("stateDiff")))!;
        call["result"]!["structuredContent"]!["extraClaim"] = true;
        fixture.SetArtifactText("stateDiff", call.ToJsonString());
        Reject(fixture, "unaltered raw");
    }

    [Fact]
    public void PreJudge_ShouldRejectDuplicateRawResponseIdentity()
    {
        using var fixture = new E2ERunEvidenceFixture();
        var raw = File.ReadAllText(fixture.GetArtifactPath("stateRpcResponses"));
        fixture.SetArtifactText("stateRpcResponses", raw + raw);
        Reject(fixture, "exactly one");
    }

    [Fact]
    public void PreJudge_ShouldRejectEqualButInventedReadbackValues()
    {
        using var fixture = new E2ERunEvidenceFixture();
        MutateArtifact(fixture, "stateRestore", root =>
        {
            root["readback"]!["baseline"]!["state"]!["Status"] = "Invented";
            root["readback"]!["actual"]!["state"]!["Status"] = "Invented";
        });
        Reject(fixture, "Readback provenance");
    }

    [Fact]
    public void Final_ShouldRejectRawProvenanceChangedAfterPreJudge()
    {
        using var fixture = new E2ERunEvidenceFixture();
        E2ERunEvidenceFixture.Run(fixture, "PreJudge").ExitCode.Should().Be(0);
        var raw = File.ReadAllText(fixture.GetArtifactPath("stateRpcResponses"));
        fixture.SetArtifactText("stateRpcResponses", raw + "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\"}\n");
        var result = E2ERunEvidenceFixture.Run(fixture, "Final");
        result.ExitCode.Should().NotBe(0);
        File.ReadAllText(fixture.DecisionPath).Should().Contain("receipt");
    }

    [Fact]
    public void PreJudge_ShouldRejectLegacyClaimOnlyEvidence()
    {
        using var fixture = new E2ERunEvidenceFixture();
        fixture.SetArtifactText("stateDiff", """
            {"result":{"isError":false,"structuredContent":{"success":true,"changeCount":2}}}
            """);
        fixture.SetArtifactText("stateRestore", """
            {"result":{"isError":false,"structuredContent":{"success":true,"restoredSelection":true,"restoredState":true,"restoredFocus":true}},"readback":{"matchesBaseline":true}}
            """);
        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");
        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public void PreJudge_ShouldAcceptActualDiffAndVerifiedRestoreContract()
    {
        using var fixture = new E2ERunEvidenceFixture();
        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");
        result.ExitCode.Should().Be(0, result.Stderr);
    }

    [Theory]
    [InlineData("selection", "SelectedIndex", "1")]
    [InlineData("state", "Status", "Completed")]
    [InlineData("focus", "focusedElementId", "OtherControl")]
    public void PreJudge_ShouldRejectUnrestoredReadbackDespiteTrueFlag(string group, string property, string value)
    {
        using var fixture = new E2ERunEvidenceFixture();
        MutateArtifact(fixture, "stateRestore", root => root["readback"]!["actual"]![group]![property] = value);
        Reject(fixture, "readback");
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("state")]
    [InlineData("focus")]
    public void PreJudge_ShouldRejectEmptyReadbackCategory(string group)
    {
        using var fixture = new E2ERunEvidenceFixture();
        MutateArtifact(fixture, "stateRestore", root =>
        {
            root["readback"]!["baseline"]![group] = new JsonObject();
            root["readback"]!["actual"]![group] = new JsonObject();
        });
        Reject(fixture, "readback");
    }

    [Fact]
    public void PreJudge_ShouldRejectEmptyDiffRegardlessOfClaimedCount()
    {
        using var fixture = new E2ERunEvidenceFixture();
        MutateArtifact(fixture, "stateDiff", root =>
        {
            var content = root["result"]!["structuredContent"]!;
            content["propertyChanges"] = new JsonArray();
            content["viewModelChanges"] = new JsonArray();
            content["changeCount"] = 99;
        });
        Reject(fixture, "runtime change");
    }

    [Theory]
    [InlineData("restoredDependencyProperties", "verified", "false")]
    [InlineData("restoredViewModelProperties", "currentValue", "\"wrong\"")]
    [InlineData("restoredViewModelProperties", "propertyName", "\"Unrelated\"")]
    public void PreJudge_ShouldRejectUnverifiedOrUnrelatedRestore(string array, string field, string json)
    {
        using var fixture = new E2ERunEvidenceFixture();
        MutateArtifact(fixture, "stateRestore", root => root["result"]!["structuredContent"]![array]![0]![field] = JsonNode.Parse(json));
        Reject(fixture, "restore");
    }

    [Fact]
    public void PreJudge_ShouldRejectSkippedRestoreOrIncorrectCount()
    {
        using var fixture = new E2ERunEvidenceFixture();
        MutateArtifact(fixture, "stateRestore", root => root["result"]!["structuredContent"]!["restoredViewModelPropertyCount"] = 2);
        Reject(fixture, "restore");
        MutateArtifact(fixture, "stateRestore", root =>
        {
            root["result"]!["structuredContent"]!["restoredViewModelPropertyCount"] = 1;
            root["result"]!["structuredContent"]!["skippedViewModelPropertyCount"] = 1;
        });
        Reject(fixture, "restore");
    }

    [Fact]
    public void PreJudge_ShouldAcceptVerifiedUnchangedReadOnlyProperty()
    {
        using var fixture = new E2ERunEvidenceFixture();
        MutateArtifact(fixture, "stateRestore", root =>
        {
            var content = root["result"]!["structuredContent"]!;
            content["skippedViewModelPropertyCount"] = 1;
            content["skippedViewModelProperties"] = JsonNode.Parse("""
                [{"propertyName":"ReadOnlyDetail","verified":true,"expectedValue":"A","currentValue":"A"}]
                """);
        });
        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");
        result.ExitCode.Should().Be(0, result.Stderr);
    }

    internal static void MutateArtifact(E2ERunEvidenceFixture fixture, string id, Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(File.ReadAllText(fixture.GetArtifactPath(id)))!.AsObject();
        mutate(root);
        fixture.SetArtifactText(id, root.ToJsonString());
        fixture.SynchronizeStateResponse(root);
    }

    private static void Reject(E2ERunEvidenceFixture fixture, string error)
    {
        var result = E2ERunEvidenceFixture.Run(fixture, "PreJudge");
        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().ContainEquivalentOf(error);
    }
}
