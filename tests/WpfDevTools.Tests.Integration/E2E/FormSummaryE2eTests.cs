using FluentAssertions;
using System.Text.Json;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class FormSummaryE2eTests
{
    private readonly McpE2eFixture _fixture;

    public FormSummaryE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetFormSummary_ShouldReportBasicControlsFormAsNotSubmittableInitially()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        await ResetFormAsync();
        var formId = await FindElementIdAsync("BasicControlsStackPanel");
        var result = await _fixture.Client.CallToolAsync(
            "get_form_summary",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = formId
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("summary").GetProperty("totalInputs").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        result.GetProperty("summary").GetProperty("isSubmittable").GetBoolean().Should().BeFalse();
        result.GetProperty("summary").GetProperty("errorCount").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("summary").TryGetProperty("validationSubmittable", out _).Should().BeTrue();
        result.GetProperty("summary").TryGetProperty("interactionSubmittable", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetFormSummary_ShouldReportBasicControlsFormAsSubmittableAfterValidInput()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        await _fixture.Client.CallToolAsync("modify_viewmodel", new
        {
            processId = _fixture.TestAppProcessId,
            propertyName = "Name",
            value = "Scene Summary User"
        });
        await _fixture.Client.CallToolAsync("modify_viewmodel", new
        {
            processId = _fixture.TestAppProcessId,
            propertyName = "Age",
            value = 28
        });

        var formId = await FindElementIdAsync("BasicControlsStackPanel");
        var result = await _fixture.Client.CallToolAsync(
            "get_form_summary",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = formId
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("summary").GetProperty("validationSubmittable").GetBoolean().Should().BeTrue();
        result.GetProperty("summary").GetProperty("interactionSubmittable").GetBoolean().Should().BeTrue();
        result.GetProperty("summary").GetProperty("isSubmittable").GetBoolean().Should().BeTrue();
        result.GetProperty("summary").GetProperty("errorCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetFormSummary_OnRootScope_ShouldNotDuplicateInputsOrCommands()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_form_summary",
            new
            {
                processId = _fixture.TestAppProcessId
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        var inputIds = result.GetProperty("inputs")
            .EnumerateArray()
            .Select(input => input.GetProperty("elementId").GetString())
            .ToArray();
        var commandIds = result.GetProperty("commands")
            .EnumerateArray()
            .Select(command => command.GetProperty("elementId").GetString())
            .ToArray();

        inputIds.Should().OnlyHaveUniqueItems();
        commandIds.Should().OnlyHaveUniqueItems();
        result.GetProperty("summary").GetProperty("totalInputs").GetInt32().Should().Be(inputIds.Length);
    }

    [Fact]
    public async Task GetFormSummary_OnRootScope_ShouldFilterFrameworkNoiseByDefault()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var defaultResult = await _fixture.Client.CallToolAsync(
            "get_form_summary",
            new
            {
                processId = _fixture.TestAppProcessId
            });
        var includeFrameworkResult = await _fixture.Client.CallToolAsync(
            "get_form_summary",
            new
            {
                processId = _fixture.TestAppProcessId,
                includeFramework = true
            });

        defaultResult.GetProperty("success").GetBoolean().Should().BeTrue();
        includeFrameworkResult.GetProperty("success").GetBoolean().Should().BeTrue();

        defaultResult.GetProperty("commands")
            .EnumerateArray()
            .Select(command => command.GetProperty("elementType").GetString())
            .Should()
            .NotContain("RepeatButton");

        includeFrameworkResult.GetProperty("commands")
            .EnumerateArray()
            .Select(command => command.GetProperty("elementType").GetString())
            .Should()
            .Contain("RepeatButton");
    }

    [Fact]
    public async Task BatchMutate_ShouldMakeBasicControlsFormSubmittableAfterSequentialMutations()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        await ResetFormAsync();

        var batch = await _fixture.Client.CallToolAsync(
            "batch_mutate",
            new
            {
                processId = _fixture.TestAppProcessId,
                mutations = new object[]
                {
                    new { tool = "modify_viewmodel", args = new { propertyName = "Name", value = "Batch User" } },
                    new { tool = "modify_viewmodel", args = new { propertyName = "Age", value = 28 } }
                }
            });

        batch.GetProperty("success").GetBoolean().Should().BeTrue();
        batch.GetProperty("executedMutationCount").GetInt32().Should().Be(2);

        var formId = await FindElementIdAsync("BasicControlsStackPanel");
        var result = await _fixture.Client.CallToolAsync(
            "get_form_summary",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = formId
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("summary").GetProperty("validationSubmittable").GetBoolean().Should().BeTrue();
        result.GetProperty("summary").GetProperty("interactionSubmittable").GetBoolean().Should().BeTrue();
        result.GetProperty("summary").GetProperty("isSubmittable").GetBoolean().Should().BeTrue();
        result.GetProperty("summary").GetProperty("errorCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task BatchMutate_ShouldAcceptStringifiedMutationsCompatibilityPayload()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var batch = await _fixture.Client.CallToolAsync(
            "batch_mutate",
            new
            {
                processId = _fixture.TestAppProcessId,
                mutations = JsonSerializer.Serialize(new object[]
                {
                    new { tool = "modify_viewmodel", args = new { propertyName = "Name", value = "Compat Batch User" } }
                })
            });

        batch.GetProperty("success").GetBoolean().Should().BeTrue(batch.GetRawText());
        batch.GetProperty("executedMutationCount").GetInt32().Should().Be(1);
        batch.GetProperty("mutations")[0].GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task BatchMutate_ShouldAcceptStringifiedCaptureSnapshotCompatibilityPayload()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var batch = await _fixture.Client.CallToolAsync(
            "batch_mutate",
            new
            {
                processId = _fixture.TestAppProcessId,
                captureSnapshot = JsonSerializer.Serialize(new
                {
                    viewModelPropertyNames = new[] { "Name" }
                }),
                includeDiff = true,
                mutations = new object[]
                {
                    new { tool = "modify_viewmodel", args = new { propertyName = "Name", value = "Compat Snapshot User" } }
                }
            });

        batch.GetProperty("success").GetBoolean().Should().BeTrue(batch.GetRawText());
        batch.GetProperty("snapshotId").GetString().Should().NotBeNullOrWhiteSpace();
        batch.GetProperty("stateDiff").GetProperty("success").GetBoolean().Should().BeTrue();
        batch.GetProperty("executedMutationCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetFormSummary_ShouldDisambiguateFocusWorkflowFallbackLabels()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var formId = await FindElementIdAsync("BasicControlsStackPanel");
        var result = await _fixture.Client.CallToolAsync(
            "get_form_summary",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = formId
            });

        var focusLabels = result.GetProperty("inputs")
            .EnumerateArray()
            .Where(input =>
            {
                var elementName = input.GetProperty("elementName").GetString();
                return elementName is "FocusStartTextBox" or "FocusNextTextBox";
            })
            .Select(input => input.GetProperty("label").GetString())
            .ToArray();

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        focusLabels.Should().Equal(
            "Focus Workflow / Focus Start",
            "Focus Workflow / Focus Next");
    }

    [Fact]
    public async Task GetFormSummary_ShouldDisambiguateSharedSectionHeadingFallbackLabels()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var formId = await FindElementIdAsync("BasicControlsStackPanel");
        var result = await _fixture.Client.CallToolAsync(
            "get_form_summary",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = formId
            });

        var labels = result.GetProperty("inputs")
            .EnumerateArray()
            .Where(input =>
            {
                var elementName = input.GetProperty("elementName").GetString();
                return elementName is "FocusBox1" or "FocusBox2" or "FocusBox3";
            })
            .Select(input => input.GetProperty("label").GetString())
            .ToArray();

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        labels.Should().HaveCount(3);
        labels.Should().OnlyHaveUniqueItems();
        labels.Should().Contain("Focus and Keyboard Testing / Focus Box 1");
        labels.Should().Contain("Focus and Keyboard Testing / Focus Box 2");
        labels.Should().Contain("Focus and Keyboard Testing / Focus Box 3");
    }

    private async Task ResetFormAsync()
    {
        await _fixture.Client.CallToolAsync("modify_viewmodel", new
        {
            processId = _fixture.TestAppProcessId,
            propertyName = "Name",
            value = ""
        });
        await _fixture.Client.CallToolAsync("modify_viewmodel", new
        {
            processId = _fixture.TestAppProcessId,
            propertyName = "Age",
            value = 0
        });
    }

    private async Task<string?> FindElementIdAsync(string elementName)
    {
        var result = await _fixture.Client.CallToolAsync(
            "find_elements",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementName
            });

        return result.GetProperty("results")[0].GetProperty("elementId").GetString();
    }
}
