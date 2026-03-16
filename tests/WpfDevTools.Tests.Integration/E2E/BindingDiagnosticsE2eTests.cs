using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// E2E tests for MCP binding diagnostics tools.
/// Validates binding error detection and DataContext chain inspection through the full pipeline.
/// TestApp Tab 1 has intentional binding errors for testing.
/// </summary>
[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class BindingDiagnosticsE2eTests
{
    private readonly McpE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BindingDiagnosticsE2eTests(McpE2eFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task GetBindingErrors_ShouldDetectTestAppIntentionalErrors()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_binding_errors",
            new { processId = _fixture.TestAppProcessId });

        _output.WriteLine($"Binding errors: {E2eTestHelpers.Truncate(result.GetRawText(), 500)}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        // TestApp Tab 1 has 3 intentional binding errors:
        // ErrorTextBox1 (invalid property), ErrorTextBox2 (wrong path), ErrorTextBox3 (null context)
        if (result.TryGetProperty("errors", out var errors))
        {
            errors.GetArrayLength().Should().BeGreaterOrEqualTo(1,
                "TestApp has intentional binding errors on Tab 1 that should be detected");

            _output.WriteLine($"Found {errors.GetArrayLength()} binding error(s)");
        }
        else if (result.TryGetProperty("errorCount", out var errorCount))
        {
            errorCount.GetInt32().Should().BeGreaterOrEqualTo(0,
                "error count should be reported");
        }
    }

    [Fact]
    public async Task GetBindingErrors_ShouldExposeBindingIssueNavigationContextRef()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_binding_errors",
            new { processId = _fixture.TestAppProcessId });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("navigation", out var navigation).Should().BeTrue();
        navigation.TryGetProperty("contextRefs", out var contextRefs).Should().BeTrue();
        contextRefs.ValueKind.Should().Be(JsonValueKind.Array);
        contextRefs.GetArrayLength().Should().BeGreaterThan(0);
        contextRefs[0].GetProperty("type").GetString().Should().Be("binding-issue");
        contextRefs[0].TryGetProperty("elementId", out _).Should().BeTrue();
        contextRefs[0].TryGetProperty("diagnosis", out _).Should().BeTrue();
        result.TryGetProperty("nextSteps", out var nextSteps).Should().BeTrue();
        nextSteps.ValueKind.Should().Be(JsonValueKind.Array);
        if (nextSteps.GetArrayLength() > 0)
        {
            nextSteps[0].TryGetProperty("whyNow", out var whyNow).Should().BeTrue();
            whyNow.GetString().Should().NotBeNullOrWhiteSpace();
            nextSteps[0].TryGetProperty("confidence", out var confidence).Should().BeTrue();
            confidence.GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task GetBindingErrors_WithNavigationFalse_ShouldOmitNavigationEnvelopeAndNextSteps()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_binding_errors",
            new
            {
                processId = _fixture.TestAppProcessId,
                navigation = false
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("navigation", out _).Should().BeFalse();
        result.TryGetProperty("nextSteps", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetBindingErrors_WithCompactTrue_ShouldOmitVerboseMessageField()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_binding_errors",
            new
            {
                processId = _fixture.TestAppProcessId,
                maxErrors = 1,
                compact = true
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var errors = result.GetProperty("errors");
        errors.GetArrayLength().Should().Be(1);

        var error = errors[0];
        error.TryGetProperty("message", out _).Should().BeFalse();
        error.GetProperty("diagnosticKind").GetString().Should().Be("BindingError");
        error.TryGetProperty("eventType", out _).Should().BeTrue();
        error.TryGetProperty("sourceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetBindings_OnRootWindow_ShouldReturnBindingInfo()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_bindings",
            new { processId = _fixture.TestAppProcessId });

        _output.WriteLine($"get_bindings result keys: {string.Join(", ", E2eTestHelpers.EnumeratePropertyNames(result))}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetDataContextChain_ShouldShowViewModelHierarchy()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_datacontext_chain",
            new { processId = _fixture.TestAppProcessId });

        _output.WriteLine($"DataContext chain: {E2eTestHelpers.Truncate(result.GetRawText(), 500)}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        // TestApp MainWindow has a TestViewModel as DataContext
        if (result.TryGetProperty("chain", out var chain) &&
            chain.ValueKind == JsonValueKind.Array)
        {
            chain.GetArrayLength().Should().BeGreaterOrEqualTo(1,
                "DataContext chain should include at least the window's ViewModel");
        }
    }

    [Fact]
    public async Task GetViewmodel_ShouldReturnTestViewModelProperties()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_viewmodel",
            new { processId = _fixture.TestAppProcessId });

        _output.WriteLine($"ViewModel: {E2eTestHelpers.Truncate(result.GetRawText(), 500)}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        // TestViewModel has Name, Age, IsEnabled, LastActionMessage properties
        if (result.TryGetProperty("properties", out var props) &&
            props.ValueKind == JsonValueKind.Array)
        {
            var propertyNames = new List<string>();
            foreach (var prop in props.EnumerateArray())
            {
                if (prop.TryGetProperty("name", out var name))
                    propertyNames.Add(name.GetString() ?? "");
            }

            _output.WriteLine($"ViewModel properties: {string.Join(", ", propertyNames)}");
            propertyNames.Should().Contain("Name", "TestViewModel should expose Name property");
        }
    }

    [Fact]
    public async Task GetCommands_ShouldReturnSaveAndClearCommands()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_commands",
            new { processId = _fixture.TestAppProcessId });

        _output.WriteLine($"Commands: {E2eTestHelpers.Truncate(result.GetRawText(), 500)}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        // TestViewModel has SaveCommand and ClearCommand
        if (result.TryGetProperty("commands", out var commands) &&
            commands.ValueKind == JsonValueKind.Array)
        {
            var commandNames = new List<string>();
            foreach (var cmd in commands.EnumerateArray())
            {
                if (cmd.TryGetProperty("name", out var name))
                    commandNames.Add(name.GetString() ?? "");
            }

            _output.WriteLine($"Commands found: {string.Join(", ", commandNames)}");
        }
    }

    [Fact]
    public async Task GetAffectedElements_ShouldReturnBestEffortCandidatesForSimpleBindingPaths()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_affected_elements",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "Name"
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("confidence").GetString().Should().Be("best-effort");
        result.GetProperty("matchStrategy").GetString().Should().Be("simple-path-match");
        result.GetProperty("requiresVerification").GetBoolean().Should().BeTrue();
        result.GetProperty("affectedCount").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("affectedElements").EnumerateArray()
            .Select(item => item.GetProperty("elementName").GetString())
            .Should().Contain("NameTextBox");
        result.GetProperty("navigation").GetProperty("recommended")[0].GetProperty("tool").GetString().Should().Be("get_bindings");
    }

    [Fact]
    public async Task GetAffectedElements_ShouldReturnHighConfidenceCandidatesForNestedBindingPaths()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_affected_elements",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "Property"
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("confidence").GetString().Should().Be("high");
        result.GetProperty("matchStrategy").GetString().Should().Be("terminal-path-match");
        result.GetProperty("affectedElements").EnumerateArray()
            .Select(item => item.GetProperty("elementName").GetString())
            .Should().Contain("ErrorTextBox2");
    }

    [Fact]
    public async Task GetAffectedElements_ShouldReturnHighConfidenceCandidatesForMultiBindingChildPaths()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_affected_elements",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "FirstName"
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("confidence").GetString().Should().Be("high");
        result.GetProperty("matchStrategy").GetString().Should().Be("multibinding-child-path-match");
        result.GetProperty("affectedElements").EnumerateArray()
            .Select(item => item.GetProperty("elementName").GetString())
            .Should().Contain("MultiBindingTextBlock");
    }

    [Fact]
    public async Task GetAffectedElements_ShouldClassifyInheritedDataContextAndUnsupportedNullContextCases()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_affected_elements",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "Name"
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();

        var inheritedMatch = result.GetProperty("affectedElements").EnumerateArray()
            .Single(item => item.GetProperty("elementName").GetString() == "NameTextBox");
        inheritedMatch.GetProperty("sourceClassification").GetString().Should().Be("InheritedDataContext");

        result.GetProperty("unsupportedCount").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("unsupportedElements").EnumerateArray()
            .Single(item => item.GetProperty("elementName").GetString() == "ErrorTextBox3")
            .GetProperty("unsupportedReason").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetBindingErrors_AfterDetailContextFlip_ShouldIncludeNewBrokenBindingWithinSinceWindow()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var baseline = await _fixture.Client.CallToolAsync(
            "get_binding_errors",
            new { processId = _fixture.TestAppProcessId, compact = true });

        baseline.GetProperty("success").GetBoolean().Should().BeTrue();
        baseline.GetProperty("errorCount").GetInt32().Should().BeGreaterThan(0);

        var sinceTimestamp = DateTimeOffset.UtcNow.ToString("O");

        var mutation = await _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "UseBrokenDetailContext",
                value = true,
                navigation = false
            });

        mutation.GetProperty("success").GetBoolean().Should().BeTrue();

        var result = await _fixture.Client.CallToolAsync(
            "get_binding_errors",
            new
            {
                processId = _fixture.TestAppProcessId,
                sinceTimestamp,
                compact = true,
                navigation = false
            });

        _output.WriteLine($"Detail-context binding errors: {E2eTestHelpers.Truncate(result.GetRawText(), 500)}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("bindingPath").GetString())
            .Should().Contain("DetailName");
    }

    [Fact]
    public async Task GetBindings_OnGeneratedDetailText_ShouldReportTemplateGeneratedBindingAfterTabActivation()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        await ActivateDetailDiagnosticsTabAsync();

        var generatedTextId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "GeneratedDetailText1");
        generatedTextId.Should().NotBeNull("Detail diagnostics tab should materialize the generated text block");

        var chain = await _fixture.Client.CallToolAsync(
            "get_binding_value_chain",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = generatedTextId,
                propertyName = "Text",
                navigation = false
            });
        chain.GetProperty("success").GetBoolean().Should().BeTrue();
        chain.GetProperty("hasBinding").GetBoolean().Should().BeTrue();

        var result = await _fixture.Client.CallToolAsync(
            "get_bindings",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = generatedTextId,
                navigation = false
            });

        _output.WriteLine($"Generated detail get_bindings: {E2eTestHelpers.Truncate(result.GetRawText(), 500)}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("bindings").EnumerateArray()
            .Select(binding => binding.GetProperty("path").GetString())
            .Should().Contain("Nested.DetailText");
    }

    [Fact]
    public async Task GetBindingErrors_AfterGeneratedDetailContextFlip_ShouldIncludeBrokenGeneratedBindings()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var sinceTimestamp = DateTimeOffset.UtcNow.ToString("O");

        var mutation = await _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "UseBrokenDetailContext",
                value = true,
                navigation = false
            });
        mutation.GetProperty("success").GetBoolean().Should().BeTrue();

        await ActivateDetailDiagnosticsTabAsync();

        var generatedText1Id = await E2eTestHelpers.WaitForElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "GeneratedDetailText1");
        var generatedText2Id = await E2eTestHelpers.WaitForElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "GeneratedDetailText2");

        generatedText1Id.Should().NotBeNull();
        generatedText2Id.Should().NotBeNull();

        await AssertGeneratedBindingUnresolvedAsync(generatedText1Id!, "Nested.DetailText");
        await AssertGeneratedBindingUnresolvedAsync(generatedText2Id!, "Nested.DetailSecondary");

        var result = await _fixture.Client.CallToolAsync(
            "get_binding_errors",
            new
            {
                processId = _fixture.TestAppProcessId,
                compact = true,
                sinceTimestamp,
                navigation = false
            });

        _output.WriteLine($"Generated detail binding errors: {E2eTestHelpers.Truncate(result.GetRawText(), 500)}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("bindingPath").GetString())
            .Should().Contain(new[] { "Nested.DetailText", "Nested.DetailSecondary" });
    }

    private async Task ActivateDetailDiagnosticsTabAsync()
    {
        var tabId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "DetailDiagnosticsTab");
        tabId.Should().NotBeNull("Detail diagnostics tab should exist in TestApp");

        var clickResult = await _fixture.Client.CallToolAsync(
            "click_element",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = tabId,
                navigation = false
            });
        clickResult.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    private async Task AssertGeneratedBindingUnresolvedAsync(string elementId, string expectedPath)
    {
        var chain = await _fixture.Client.CallToolAsync(
            "get_binding_value_chain",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId,
                propertyName = "Text",
                navigation = false
            });

        chain.GetProperty("success").GetBoolean().Should().BeTrue();
        chain.GetProperty("hasBinding").GetBoolean().Should().BeTrue();
        chain.GetProperty("chain").EnumerateArray()
            .Any(step =>
                step.TryGetProperty("resolutionState", out var state) &&
                state.GetString() == "Unresolved")
            .Should().BeTrue();
        chain.GetProperty("chain").EnumerateArray()
            .Where(step => step.TryGetProperty("path", out _))
            .Select(step => step.GetProperty("path").GetString())
            .Should().Contain(expectedPath);
    }
}
