using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class MutationToolMetadataTests
{
    [Fact]
    public void SetDpValue_WhenDetailOmitted_ShouldReturnCompactCoreFields()
    {
        var result = MutationMetadataProbe.Apply(
            new
            {
                success = true,
                propertyName = "Width",
                oldValue = 50,
                newValue = 100
            },
            new
            {
                elementId = "Button_1",
                propertyName = "Width",
                value = 100
            },
            null,
            "Runtime-only mutation. Capture oldValue/newValue if you need manual restore after verification.");

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("propertyName").GetString().Should().Be("Width");
        json.GetProperty("oldValue").GetInt32().Should().Be(50);
        json.GetProperty("newValue").GetInt32().Should().Be(100);
        json.GetProperty("restoreRequired").GetBoolean().Should().BeTrue();
        json.GetProperty("restoreStatus").GetString().Should().Be("notRestored");
        json.GetProperty("restoreSuggestedAction").GetString().Should().Contain("restore_state_snapshot");
        json.TryGetProperty("requestedInput", out _).Should().BeFalse();
        json.TryGetProperty("effectiveInput", out _).Should().BeFalse();
        json.TryGetProperty("observedEffect", out _).Should().BeFalse();
        json.TryGetProperty("notes", out _).Should().BeFalse();
        json.TryGetProperty("usedFallback", out _).Should().BeFalse();
    }

    [Fact]
    public void SetDpValue_WithDetailVerbose_ShouldIncludeRequestedInputAndObservedEffectMetadata()
    {
        var result = MutationMetadataProbe.Apply(
            new
            {
                success = true,
                propertyName = "Width",
                oldValue = 50,
                newValue = 100
            },
            new
            {
                elementId = "Button_1",
                propertyName = "Width",
                value = 100
            },
            ToJsonElement(new { detail = "verbose" }),
            "Runtime-only mutation. Capture oldValue/newValue if you need manual restore after verification.");

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("propertyName").GetString().Should().Be("Width");
        json.GetProperty("effectiveInput").GetProperty("elementId").GetString().Should().Be("Button_1");
        json.GetProperty("observedEffect").GetProperty("oldValue").GetInt32().Should().Be(50);
        json.GetProperty("usedFallback").GetBoolean().Should().BeFalse();
        json.GetProperty("notes").GetString().Should().Contain("Runtime-only");
        json.GetProperty("restoreRequired").GetBoolean().Should().BeTrue();
        json.GetProperty("restoreStatus").GetString().Should().Be("notRestored");
    }

    [Fact]
    public void ClearDpValue_WithDetailVerbose_ShouldIncludeRequestedInputAndObservedEffectMetadata()
    {
        var result = MutationMetadataProbe.Apply(
            new
            {
                success = true,
                propertyName = "Width",
                hadLocalValue = true,
                clearedValue = 100,
                newValue = 50
            },
            new
            {
                elementId = "Button_1",
                propertyName = "Width"
            },
            ToJsonElement(new { detail = "verbose" }),
            "Runtime-only mutation. Use the observed old/new values for manual restore if later steps depend on the previous local value.");

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("propertyName").GetString().Should().Be("Width");
        json.GetProperty("effectiveInput").GetProperty("elementId").GetString().Should().Be("Button_1");
        json.GetProperty("observedEffect").GetProperty("hadLocalValue").GetBoolean().Should().BeTrue();
        json.GetProperty("usedFallback").GetBoolean().Should().BeFalse();
        json.GetProperty("notes").GetString().Should().Contain("manual restore");
    }

    [Fact]
    public void OverrideStyleSetter_WithDetailVerbose_ShouldIncludeRequestedInputAndObservedEffectMetadata()
    {
        var result = MutationMetadataProbe.Apply(
            new
            {
                success = true,
                propertyName = "Background",
                oldValue = "Blue",
                newValue = "Red"
            },
            new
            {
                elementId = "Button_1",
                propertyName = "Background",
                value = "Red"
            },
            ToJsonElement(new { detail = "verbose" }),
            "Runtime-only style override. Record the observed style values before using this in demos, troubleshooting, or regression flows.");

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("propertyName").GetString().Should().Be("Background");
        json.GetProperty("effectiveInput").GetProperty("value").GetString().Should().Be("Red");
        json.GetProperty("observedEffect").GetProperty("newValue").GetString().Should().Be("Red");
        json.GetProperty("usedFallback").GetBoolean().Should().BeFalse();
        json.GetProperty("notes").GetString().Should().Contain("style");
    }

    [Fact]
    public void SetDpValue_WithDetailVerboseAliasStandard_ShouldPreserveVerboseMetadata()
    {
        var result = MutationMetadataProbe.Apply(
            new
            {
                success = true,
                propertyName = "Width",
                oldValue = 50,
                newValue = 100
            },
            new
            {
                elementId = "Button_1",
                propertyName = "Width",
                value = 100
            },
            ToJsonElement(new { detail = "standard" }),
            "Runtime-only mutation. Capture oldValue/newValue if you need manual restore after verification.");

        var json = JsonSerializer.SerializeToElement(result);
        json.TryGetProperty("requestedInput", out _).Should().BeTrue();
        json.TryGetProperty("effectiveInput", out _).Should().BeTrue();
        json.TryGetProperty("observedEffect", out _).Should().BeTrue();
        json.TryGetProperty("notes", out _).Should().BeTrue();
        json.GetProperty("usedFallback").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void SetDpValue_CompactPayload_ShouldBeLessThanFortyPercentOfVerbosePayload()
    {
        var verbose = MutationMetadataProbe.Apply(
            new
            {
                success = true,
                propertyName = "Width",
                oldValue = 50,
                newValue = 100
            },
            new
            {
                elementId = "Button_1",
                propertyName = "Width",
                value = 100
            },
            ToJsonElement(new { detail = "verbose" }),
            "Runtime-only mutation. Capture oldValue/newValue if you need manual restore after verification.");

        var compact = MutationMetadataProbe.Apply(
            new
            {
                success = true,
                propertyName = "Width",
                oldValue = 50,
                newValue = 100
            },
            new
            {
                elementId = "Button_1",
                propertyName = "Width",
                value = 100
            },
            null,
            "Runtime-only mutation. Capture oldValue/newValue if you need manual restore after verification.");

        var verboseJson = JsonSerializer.Serialize(verbose);
        var compactJson = JsonSerializer.Serialize(compact);

        compactJson.Length.Should().BeLessThan((int)(verboseJson.Length * 0.4));
    }

    [Fact]
    public void SetDpValue_WithDetailCompact_ShouldOmitVerboseMetadataAndKeepCoreFields()
    {
        var result = MutationMetadataProbe.Apply(
            new
            {
                success = true,
                propertyName = "Width",
                oldValue = 50,
                newValue = 100
            },
            new
            {
                elementId = "Button_1",
                propertyName = "Width",
                value = 100
            },
            ToJsonElement(new { detail = "compact" }),
            "Runtime-only mutation. Capture oldValue/newValue if you need manual restore after verification.");

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("propertyName").GetString().Should().Be("Width");
        json.GetProperty("oldValue").GetInt32().Should().Be(50);
        json.GetProperty("newValue").GetInt32().Should().Be(100);
        json.TryGetProperty("requestedInput", out _).Should().BeFalse();
        json.TryGetProperty("effectiveInput", out _).Should().BeFalse();
        json.TryGetProperty("observedEffect", out _).Should().BeFalse();
        json.TryGetProperty("notes", out _).Should().BeFalse();
        json.TryGetProperty("usedFallback", out _).Should().BeFalse();
        json.GetProperty("restoreRequired").GetBoolean().Should().BeTrue();
        json.GetProperty("restoreStatus").GetString().Should().Be("notRestored");
    }

    [Fact]
    public void SetDpValue_WithDetailMinimal_ShouldKeepOnlySuccessPropertyNameAndNewValue()
    {
        var result = MutationMetadataProbe.Apply(
            new
            {
                success = true,
                propertyName = "Width",
                oldValue = 50,
                newValue = 100,
                requestedValue = 100,
                baseValueSource = "LocalValue",
                valueType = "Double"
            },
            new
            {
                elementId = "Button_1",
                propertyName = "Width",
                value = 100
            },
            ToJsonElement(new { detail = "minimal" }),
            "Runtime-only mutation. Capture oldValue/newValue if you need manual restore after verification.");

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("propertyName").GetString().Should().Be("Width");
        json.GetProperty("newValue").GetInt32().Should().Be(100);
        json.TryGetProperty("oldValue", out _).Should().BeFalse();
        json.TryGetProperty("requestedValue", out _).Should().BeFalse();
        json.TryGetProperty("baseValueSource", out _).Should().BeFalse();
        json.TryGetProperty("valueType", out _).Should().BeFalse();
        json.TryGetProperty("requestedInput", out _).Should().BeFalse();
        json.TryGetProperty("effectiveInput", out _).Should().BeFalse();
        json.TryGetProperty("observedEffect", out _).Should().BeFalse();
        json.TryGetProperty("notes", out _).Should().BeFalse();
        json.TryGetProperty("usedFallback", out _).Should().BeFalse();
        json.TryGetProperty("restoreRequired", out _).Should().BeFalse();
        json.TryGetProperty("restoreStatus", out _).Should().BeFalse();
    }

    [Fact]
    public void ModifyViewModel_WithDetailMinimal_ShouldKeepOnlySuccessPropertyNameAndNewValue()
    {
        var result = MutationMetadataProbe.Apply(
            new
            {
                success = true,
                propertyName = "Name",
                oldValue = "Alice",
                newValue = "Bob",
                propertyType = "System.String",
                canWrite = true,
                requestedValueType = "System.String",
                convertedValueType = "System.String"
            },
            new
            {
                elementId = "NameTextBox",
                propertyName = "Name",
                value = "Bob"
            },
            ToJsonElement(new { detail = "minimal" }),
            "Runtime-only ViewModel mutation. UI refresh still depends on INotifyPropertyChanged and any binding-side validation.");

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("propertyName").GetString().Should().Be("Name");
        json.GetProperty("newValue").GetString().Should().Be("Bob");
        json.TryGetProperty("oldValue", out _).Should().BeFalse();
        json.TryGetProperty("propertyType", out _).Should().BeFalse();
        json.TryGetProperty("canWrite", out _).Should().BeFalse();
        json.TryGetProperty("requestedValueType", out _).Should().BeFalse();
        json.TryGetProperty("convertedValueType", out _).Should().BeFalse();
        json.TryGetProperty("requestedInput", out _).Should().BeFalse();
        json.TryGetProperty("effectiveInput", out _).Should().BeFalse();
        json.TryGetProperty("observedEffect", out _).Should().BeFalse();
        json.TryGetProperty("notes", out _).Should().BeFalse();
        json.TryGetProperty("usedFallback", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SetDpValueTool_WithInvalidDetail_ShouldReturnStructuredError()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(50006);
        var tool = new SetDpValueTool(sessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId = 50006,
            elementId = "Button_1",
            propertyName = "Width",
            value = 100,
            detail = "full"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("detail");
    }

    private sealed class MutationMetadataProbe : PipeConnectedToolBase
    {
        private MutationMetadataProbe() : base(new SessionManager())
        {
        }

        public static object Apply(object result, object requestedInput, JsonElement? arguments, string notes)
        {
            var (_, error) = ParseMutationDetailMode(arguments);
            if (error != null)
            {
                return error;
            }

            var (mode, _) = ParseMutationDetailMode(arguments);
            return AddSuccessMetadata(result, requestedInput, notes, detailMode: mode);
        }
    }
}
