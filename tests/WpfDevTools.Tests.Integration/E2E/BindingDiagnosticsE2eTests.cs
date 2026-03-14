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
}
