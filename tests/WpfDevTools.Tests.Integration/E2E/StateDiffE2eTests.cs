using FluentAssertions;
using System.Text.Json;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class StateDiffE2eTests
{
    private readonly McpE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public StateDiffE2eTests(McpE2eFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task GetStateDiff_AfterModifyViewModel_ShouldReportSemanticChanges()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var findResult = await _fixture.Client.CallToolAsync(
            "find_elements",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementName = "NameTextBox"
            });
        var runtimeElementId = findResult.GetProperty("results")[0].GetProperty("elementId").GetString();

        var capture = await _fixture.Client.CallToolAsync(
            "capture_state_snapshot",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = runtimeElementId,
                propertyNames = new[] { "Text" },
                viewModelPropertyNames = new[] { "Name" }
            });
        _output.WriteLine($"capture_state_snapshot => {capture.GetRawText()}");

        var snapshotId = capture.GetProperty("snapshotId").GetString();

        var mutate = await _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = runtimeElementId,
                propertyName = "Name",
                value = "State Diff Test"
            });
        mutate.GetProperty("success").GetBoolean().Should().BeTrue();

        var diff = await _fixture.Client.CallToolAsync(
            "get_state_diff",
            new
            {
                processId = _fixture.TestAppProcessId,
                snapshotId,
                trigger = "modify_viewmodel(Name)"
            });

        diff.GetProperty("success").GetBoolean().Should().BeTrue();
        diff.GetProperty("trigger").GetString().Should().Be("modify_viewmodel(Name)");
        diff.GetProperty("propertyChanges").GetArrayLength().Should().BeGreaterThan(0);
        diff.GetProperty("viewModelChanges").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CaptureStateSnapshot_ShouldExposeMutationSessionNavigationContextRef()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var capture = await _fixture.Client.CallToolAsync(
            "capture_state_snapshot",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyNames = new[] { "Visibility" }
            });

        capture.GetProperty("success").GetBoolean().Should().BeTrue();
        capture.TryGetProperty("navigation", out var navigation).Should().BeTrue();
        navigation.GetProperty("contextRefs")[0].GetProperty("type").GetString().Should().Be("mutation-session");
        navigation.GetProperty("contextRefs")[0].GetProperty("snapshotId").GetString().Should().NotBeNullOrWhiteSpace();
        navigation.GetProperty("recommended").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task BatchMutate_WithSnapshotAndDiff_ShouldReturnSequentialMutationResultsAndStateDiff()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var findResult = await _fixture.Client.CallToolAsync(
            "find_elements",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementName = "NameTextBox"
            });
        var runtimeElementId = findResult.GetProperty("results")[0].GetProperty("elementId").GetString();

        var batchResult = await _fixture.Client.CallToolAsync(
            "batch_mutate",
            new
            {
                processId = _fixture.TestAppProcessId,
                captureSnapshot = new
                {
                    elementId = runtimeElementId,
                    propertyNames = new[] { "Text" },
                    viewModelPropertyNames = new[] { "Name", "Age" }
                },
                includeDiff = true,
                mutations = new object[]
                {
                    new { tool = "modify_viewmodel", args = new { elementId = runtimeElementId, propertyName = "Name", value = "Batch State Diff" } },
                    new { tool = "modify_viewmodel", args = new { propertyName = "Age", value = 34 } }
                }
            });

        batchResult.GetProperty("success").GetBoolean().Should().BeTrue();
        batchResult.GetProperty("mutationCount").GetInt32().Should().Be(2);
        batchResult.GetProperty("executedMutationCount").GetInt32().Should().Be(2);
        batchResult.GetProperty("stateDiff").GetProperty("success").GetBoolean().Should().BeTrue();
        batchResult.GetProperty("stateDiff").GetProperty("viewModelChanges").GetArrayLength().Should().BeGreaterThan(0);
    }
}
