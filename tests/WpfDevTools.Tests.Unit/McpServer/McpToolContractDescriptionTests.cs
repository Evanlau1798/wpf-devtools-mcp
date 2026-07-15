using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using FluentAssertions.Execution;
using WpfDevTools.Mcp.Server;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpTools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolContractDescriptionTests
{
    private static readonly (Type ToolType, string MethodName)[] RuntimeNavigationTools =
    [
        (typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindingErrors)),
        (typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindingMismatches)),
        (typeof(MvvmMcpTools), nameof(MvvmMcpTools.GetValidationErrors)),
        (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.DiagnoseVisibility)),
        (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetInteractionReadiness)),
        (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetFormSummary)),
        (typeof(InteractionMcpTools), nameof(InteractionMcpTools.ClickElement)),
        (typeof(MvvmMcpTools), nameof(MvvmMcpTools.ExecuteCommand)),
        (typeof(MvvmMcpTools), nameof(MvvmMcpTools.ModifyViewModel)),
        (typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.SetDpValue)),
        (typeof(EventMcpTools), nameof(EventMcpTools.FireRoutedEvent)),
        (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetUiSummary)),
        (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetElementSnapshot)),
        (typeof(StateMcpTools), nameof(StateMcpTools.CaptureStateSnapshot)),
        (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetStateDiff))
    ];

    [Fact]
    public void ToolDescriptions_ShouldMentionUpdatedContractTerms()
    {
        var expectations = new (Type ToolType, string MethodName, string Term)[]
        {
            (typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.SetDpValue), "requestedValue"),
            (typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.ClearDpValue), "hadLocalValue"),
            (typeof(StyleMcpTools), nameof(StyleMcpTools.OverrideStyleSetter), "oldValue"),
            (typeof(MvvmMcpTools), nameof(MvvmMcpTools.ExecuteCommand), "commandName"),
            (typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.GetDpValueSource), "rawBaseValueSource"),
            (typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindingValueChain), "LocalDataContext"),
            (typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.GetRenderStats), "isWarmedUp"),
            (typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.GetRenderStats), "confidence"),
            (typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.GetRenderStats), "warmUp"),
            (typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.FindBindingLeaks), "potentialLeaks"),
            (typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.FindBindingLeaks), "samplingDurationMs"),
            (typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.FindBindingLeaks), "warmUp"),
            (typeof(TreeMcpTools), nameof(TreeMcpTools.GetWindows), "isMainWindow"),
            (typeof(TreeMcpTools), nameof(TreeMcpTools.GetWindows), "index, title, type, isActive, isVisible, isMainWindow, elementId"),
            (typeof(TreeMcpTools), nameof(TreeMcpTools.GetNamescope), "inactive tabs"),
            (typeof(EventMcpTools), nameof(EventMcpTools.GetEventHandlers), "mayBeIncomplete"),
            (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetInteractionReadiness), "commandReadiness"),
            (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetInteractionReadiness), "CommandParameterValueRedacted"),
            (typeof(InteractionMcpTools), nameof(InteractionMcpTools.SimulateKeyboard), "focusChanged"),
            (typeof(StateMcpTools), nameof(StateMcpTools.RestoreStateSnapshot), "skippedViewModelPropertyCount"),
            (typeof(StateMcpTools), nameof(StateMcpTools.RestoreStateSnapshot), "skippedViewModelProperties"),
            (typeof(StateMcpTools), nameof(StateMcpTools.RestoreStateSnapshot), "restoredDependencyProperties"),
            (typeof(StateMcpTools), nameof(StateMcpTools.RestoreStateSnapshot), "restoredViewModelProperties"),
            (typeof(MvvmMcpTools), nameof(MvvmMcpTools.GetViewModel), "canWrite"),
            (typeof(MvvmMcpTools), nameof(MvvmMcpTools.ModifyViewModel), "requestedValueType"),
            (typeof(MvvmMcpTools), nameof(MvvmMcpTools.GetValidationErrors), "logical and visual descendants"),
            (typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindingErrors), "validation rule errors"),
            (typeof(StyleMcpTools), nameof(StyleMcpTools.GetAppliedStyles), "localResourceReferences"),
            (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetUiSummary), "summaryOnly"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "canConnectFromCurrentServer"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "connectionWarning"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "WPFDEVTOOLS_MCP_ALLOWED_TARGETS"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "allowlisted targets"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "redactedTargetCount"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "policyEnvVar"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "counted before nameFilter"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "filtering side channel"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "suggestedAction"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "requiresElevationToConnect"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "autoDiscovered"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "redactedCandidateCount"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "selectionStrategy"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "largest_working_set"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "windowFilter"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "windowFilter"),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "foreground"),
            (typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.ListUiBlockPacks), "role"),
            (typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.ListUiBlockPacks), "required")
        };

        using var aggregateScope = new AssertionScope();
        foreach (var (toolType, methodName, term) in expectations)
        {
            using var scope = new AssertionScope($"{toolType.Name}.{methodName} [{term}]");
            GetDescription(toolType, methodName).Should().Contain(term);
        }
    }

    [Fact]
    public void RuntimeNavigationToolDescriptions_ShouldKeepNextStepsOutOfPayloadExamples()
    {
        foreach (var (toolType, methodName) in RuntimeNavigationTools)
        {
            var description = GetDescription(toolType, methodName);

            description.Should().NotContain("\"nextSteps\"");
            description.Should().NotContain("nextSteps: [");
        }
    }

    [Fact]
    public void ToolDescriptionExamples_ShouldBeStrictJsonObjects()
    {
        var failures = new List<string>();
        foreach (var (toolName, description) in GetAllToolDescriptions())
        {
            foreach (var example in ExtractExamples(description))
            {
                try
                {
                    using var document = JsonDocument.Parse(
                        example,
                        new JsonDocumentOptions
                        {
                            AllowTrailingCommas = false,
                            CommentHandling = JsonCommentHandling.Disallow
                        });

                    document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
                }
                catch (Exception ex)
                {
                    failures.Add($"{toolName}: {example} ({ex.Message})");
                }
            }
        }

        failures.Should().BeEmpty("tool-call examples are copied by MCP clients and must be strict JSON, not JavaScript-like object literals");
    }

    [Fact]
    public void AiFacingResponseSummaries_ShouldUseBulletSummariesInsteadOfSchemaSketches()
    {
        var failures = new List<string>();
        foreach (var (name, text) in GetAllAiFacingContractTexts())
        {
            if (text.Contains("RESPONSE FORMAT:", StringComparison.Ordinal))
            {
                failures.Add($"{name}: uses RESPONSE FORMAT instead of RESPONSE SUMMARY");
            }

            if (text.Contains("SCHEMA SKETCH", StringComparison.Ordinal))
            {
                failures.Add($"{name}: uses SCHEMA SKETCH instead of RESPONSE SUMMARY or bullet-list prose");
            }

            if (Regex.IsMatch(text, @"\b[A-Za-z_][A-Za-z0-9_]*\?:", RegexOptions.CultureInvariant))
            {
                failures.Add($"{name}: uses TypeScript optional markers");
            }

            var responseSummary = ExtractHeadingBlock(text, "RESPONSE SUMMARY:");
            if (!string.IsNullOrWhiteSpace(responseSummary)
                && Regex.IsMatch(responseSummary, @"(?m)^\s{0,6}(?!- )[A-Za-z_][A-Za-z0-9_]*\s*:",
                    RegexOptions.CultureInvariant))
            {
                failures.Add($"{name}: RESPONSE SUMMARY uses unbulleted pseudo-object fields");
            }

            if (!string.IsNullOrWhiteSpace(responseSummary)
                && Regex.IsMatch(responseSummary, @"(?m)^\s*\{", RegexOptions.CultureInvariant))
            {
                failures.Add($"{name}: RESPONSE SUMMARY uses object-literal shape lines");
            }

            if (!string.IsNullOrWhiteSpace(responseSummary)
                && Regex.IsMatch(responseSummary, @"(?m)^\s*-\s*\{", RegexOptions.CultureInvariant))
            {
                failures.Add($"{name}: RESPONSE SUMMARY uses bulleted object-literal shape lines");
            }
        }

        ServerInstructions.Value.Should().NotContain("All tools return JSON: {",
            "server-level response guidance should be prose plus contract-resource references, not JavaScript-like object literals");
        ServerInstructions.Value.Should().NotContain("On error: {",
            "error response guidance should not look like a copy-paste JSON example");
        failures.Should().BeEmpty("AI-facing response shapes should be bullet summaries or strict JSON, not TypeScript-like schema sketches");
    }

    [Fact]
    public void AiFacingRequestFormatBlocks_ShouldNotUsePseudoJsonSyntax()
    {
        var failures = new List<string>();
        foreach (var (name, text) in GetAllAiFacingContractTexts())
        {
            var requestFormat = ExtractHeadingBlock(text, "REQUEST FORMAT:");
            if (string.IsNullOrWhiteSpace(requestFormat))
            {
                continue;
            }

            if (requestFormat.Contains("?:", StringComparison.Ordinal))
            {
                failures.Add($"{name}: REQUEST FORMAT uses TypeScript optional markers");
            }

            if (Regex.IsMatch(requestFormat, @"(?m)^\s{0,6}[A-Za-z_][A-Za-z0-9_]*\??\s*:"))
            {
                failures.Add($"{name}: REQUEST FORMAT uses unquoted object keys");
            }

            if (Regex.IsMatch(requestFormat, @"\{\s*[A-Za-z_][A-Za-z0-9_]*\s*(?:,|\})"))
            {
                failures.Add($"{name}: REQUEST FORMAT uses JavaScript object shorthand");
            }
        }

        failures.Should().BeEmpty(
            "REQUEST FORMAT blocks are copied as tool-call arguments, so they must be strict JSON or be renamed to a non-copyable schema sketch");
    }

    [Fact]
    public void MutationAndInteractionToolDescriptions_ShouldPreferVerboseAndCompactWording()
    {
        var tools = new[]
        {
            (typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.SetDpValue)),
            (typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.ClearDpValue)),
            (typeof(StyleMcpTools), nameof(StyleMcpTools.OverrideStyleSetter)),
            (typeof(MvvmMcpTools), nameof(MvvmMcpTools.ExecuteCommand)),
            (typeof(MvvmMcpTools), nameof(MvvmMcpTools.ModifyViewModel)),
            (typeof(InteractionMcpTools), nameof(InteractionMcpTools.ClickElement)),
            (typeof(EventMcpTools), nameof(EventMcpTools.FireRoutedEvent))
        };

        using var aggregateScope = new AssertionScope();
        foreach (var (toolType, methodName) in tools)
        {
            using var scope = new AssertionScope($"{toolType.Name}.{methodName}");
            var description = GetDescription(toolType, methodName);
            description.Should().Contain("detail");
            description.Should().Contain("compact");
            description.Should().Contain("verbose");
            description.Should().Contain("standard");
        }
    }

    [Fact]
    public void AiFacingToolDescriptions_ShouldIncludeUseWhenAndDoNotUseGuidance()
    {
        var tools = new[]
        {
            (typeof(MutationBatchMcpTools), nameof(MutationBatchMcpTools.BatchMutate)),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetActiveProcess)),
            (typeof(BindingMcpTools), nameof(BindingMcpTools.GetAffectedElements))
        };

        using var aggregateScope = new AssertionScope();
        foreach (var (toolType, methodName) in tools)
        {
            using var scope = new AssertionScope($"{toolType.Name}.{methodName}");
            var description = GetDescription(toolType, methodName);
            description.Should().Contain("USE WHEN:");
            description.Should().Contain("DO NOT USE:");
        }
    }

    [Fact]
    public void ConnectDescription_ShouldGuideSceneFirstFollowUps_AndDirectAutoDiscoveryOverrides()
    {
        var description = GetDescription(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect));

        description.Should().Contain("get_ui_summary");
        description.Should().Contain("get_element_snapshot");
        description.Should().Contain("get_form_summary");
        description.Should().Contain("connect(windowFilter='all')");
        description.Should().Contain("connect(selectionStrategy='largest_working_set', windowFilter='all')");
    }

    [Fact]
    public void GetProcessesDescription_ShouldDocumentEveryWindowFilterValue()
    {
        var description = GetDescription(typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses));

        foreach (var value in new[] { "visible", "all", "foreground" })
        {
            description.Should().Contain($"'{value}'");
        }
    }

    [Fact]
    public void HighValueToolDescriptions_ShouldSeparateMachineReadableContractsFromRequestOptions()
    {
        var tools = new[]
        {
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses)),
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect)),
            (typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindings)),
            (typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindingErrors)),
            (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetUiSummary)),
            (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetElementSnapshot)),
            (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetFormSummary))
        };

        using var aggregateScope = new AssertionScope();
        foreach (var (toolType, methodName) in tools)
        {
            using var scope = new AssertionScope($"{toolType.Name}.{methodName}");
            var description = GetDescription(toolType, methodName);
            description.Should().Contain("structuredContent");
            description.Should().Contain("content[0].text");
            description.Should().Contain("wpf://contracts/response");
            description.Should().NotContain("RESPONSE FORMAT:");
            description.Should().NotContain("ERRORS:");
            description.Should().Contain("RESPONSE FIELDS:");
            description.Should().Contain("REQUEST OPTIONS:");
        }
    }

    [Fact]
    public void ForceBindingUpdateDescription_ShouldMatchDefaultSourceDirection()
    {
        var description = GetDescription(typeof(BindingMcpTools), nameof(BindingMcpTools.ForceBindingUpdate));
        var directionParameter = typeof(BindingMcpTools)
            .GetMethod(nameof(BindingMcpTools.ForceBindingUpdate), BindingFlags.Public | BindingFlags.Static)!
            .GetParameters()
            .Single(parameter => parameter.Name == "direction");
        var directionDescription = directionParameter.GetCustomAttribute<DescriptionAttribute>()!.Description;

        description.Should().Contain("By default, pushes the current UI target value to the binding source");
        description.Should().NotContain("triggers UpdateSource and UpdateTarget");
        directionDescription.Should().Contain("Default: Source");
        directionDescription.Should().NotContain("Default: both");
    }

    [Fact]
    public void GetFormSummaryDescription_ShouldDescribeNestedSummarySubmittabilityFields()
    {
        var description = GetDescription(typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetFormSummary));

        description.Should().Contain("summary.validationSubmittable");
        description.Should().Contain("summary.interactionSubmittable");
        description.Should().Contain("summary.isSubmittable");
        description.Should().NotContain("RESPONSE FIELDS: formScope, inputs, commands, summary, validationSubmittable",
            "submittability fields are nested under summary rather than emitted at the top level");
    }

    private static string GetDescription(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();

        var description = method!.GetCustomAttribute<DescriptionAttribute>();
        description.Should().NotBeNull();
        return description!.Description;
    }

    private static IEnumerable<(string ToolName, string Description)> GetAllToolDescriptions()
    {
        return typeof(ProcessMcpTools).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => new
            {
                Tool = method.GetCustomAttribute<McpServerToolAttribute>(),
                Description = method.GetCustomAttribute<DescriptionAttribute>()
            })
            .Where(item => item.Tool is not null
                && !string.IsNullOrWhiteSpace(item.Tool.Name)
                && item.Description is not null)
            .Select(item => (item.Tool!.Name!, item.Description!.Description));
    }

    private static IEnumerable<(string Name, string Text)> GetAllAiFacingContractTexts()
    {
        foreach (var (toolName, description) in GetAllToolDescriptions())
        {
            yield return ($"tool:{toolName}", description);
        }

        yield return ("server-instructions", ServerInstructions.Value);
    }

    private static IEnumerable<string> ExtractExamples(string description)
    {
        var inExamples = false;
        foreach (var rawLine in description.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line == "EXAMPLES:")
            {
                inExamples = true;
                continue;
            }

            if (!inExamples)
            {
                continue;
            }

            if (line.Length == 0)
            {
                yield break;
            }

            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                yield break;
            }

            yield return line[2..].Trim();
        }
    }

    private static string ExtractHeadingBlock(string text, string heading)
    {
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        start += heading.Length;
        var nextHeading = Regex.Match(
            text[start..],
            @"(?m)^[A-Z][A-Z0-9 /()'-]+:",
            RegexOptions.CultureInvariant);

        return nextHeading.Success
            ? text.Substring(start, nextHeading.Index)
            : text[start..];
    }
}
