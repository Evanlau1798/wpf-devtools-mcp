using FluentAssertions;

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
