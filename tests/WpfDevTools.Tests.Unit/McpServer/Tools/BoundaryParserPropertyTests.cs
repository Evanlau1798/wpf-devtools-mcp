using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class BoundaryParserPropertyTests
{
    [Fact]
    public void ProcessWindowFilters_WithUnknownFuzzInputs_ShouldFailClosedToVisible()
    {
        foreach (var value in UnknownFuzzStrings())
        {
            var parsed = ProcessWindowFilters.TryParse(value, out var filter);

            parsed.Should().BeFalse(value);
            filter.Should().Be(ProcessWindowFilter.Visible, value);
        }
    }

    [Fact]
    public void ProcessDiscoverySelectionStrategies_WithUnknownFuzzInputs_ShouldFailClosedToSingleOnly()
    {
        foreach (var value in UnknownFuzzStrings())
        {
            var parsed = ProcessDiscoverySelectionStrategies.TryParse(value, out var strategy);

            parsed.Should().BeFalse(value);
            strategy.Should().Be(ProcessDiscoverySelectionStrategy.SingleOnly, value);
        }
    }

    [Theory]
    [InlineData("elementId", BoundaryStringLimits.MaxElementIdLength, true)]
    [InlineData("elementId", BoundaryStringLimits.MaxElementIdLength + 1, false)]
    [InlineData("propertyName", BoundaryStringLimits.MaxLabelLength, true)]
    [InlineData("propertyName", BoundaryStringLimits.MaxLabelLength + 1, false)]
    [InlineData("captureSnapshot", BoundaryStringLimits.MaxStringifiedJsonArgumentLength, true)]
    [InlineData("captureSnapshot", BoundaryStringLimits.MaxStringifiedJsonArgumentLength + 1, false)]
    [InlineData("customPayload", BoundaryStringLimits.MaxStringArgumentLength, true)]
    [InlineData("customPayload", BoundaryStringLimits.MaxStringArgumentLength + 1, false)]
    public void BoundaryParameterValidator_ShouldApplyConfiguredStringLimits(
        string propertyName,
        int length,
        bool expectedSuccess)
    {
        var args = JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            [propertyName] = new('x', length)
        });

        var success = BoundaryParameterValidator.TryValidateStringBoundaries(args, out var error);

        success.Should().Be(expectedSuccess);
        (error is null).Should().Be(expectedSuccess);
    }

    [Fact]
    public void JsonCompatibilityPayloadParser_WithFuzzedStringPayloads_ShouldNeverThrow()
    {
        foreach (var value in InvalidJsonPayloads())
        {
            var root = JsonSerializer.SerializeToElement(new Dictionary<string, string>
            {
                ["captureSnapshot"] = value
            });

            var parsed = JsonCompatibilityPayloadParser.TryParseOptionalObjectProperty(
                root,
                "captureSnapshot",
                out _,
                out var hasValue,
                out var errorMessage);

            parsed.Should().BeFalse(value);
            hasValue.Should().BeTrue(value);
            errorMessage.Should().NotBeNullOrWhiteSpace(value);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("[123]")]
    [InlineData("[{\"tool\":\"unsupported\"}]")]
    [InlineData("[{\"tool\":\"modify_viewmodel\",\"args\":[]}]")]
    public async Task BatchMutateParser_WithInvalidStringifiedMutations_ShouldRejectBeforeExecuting(
        string serializedMutations)
    {
        var invoked = false;
        var tool = new BatchMutateTool(
            new SessionManager(),
            (_, _, _) =>
            {
                invoked = true;
                return Task.FromResult<object>(new { success = true });
            },
            (_, _) => Task.FromResult<object>(new { success = true, snapshotId = "snapshot_1" }),
            (_, _) => Task.FromResult<object>(new { success = true }));
        var args = JsonSerializer.SerializeToElement(new
        {
            processId = 12345,
            mutations = serializedMutations
        });

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(args, CancellationToken.None));

        invoked.Should().BeFalse();
        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString()
            .Should().BeOneOf("InvalidArgument", "MissingRequiredParameter");
    }

    [Fact]
    public async Task ResponseNormalizer_WithCompatibilityRecoveryFields_ShouldProjectCanonicalRecovery()
    {
        foreach (var payload in FuzzedErrorPayloads())
        {
            var result = await ToolCallHelper.ExecuteAndWrapAsync(
                (_, _) => Task.FromResult<object>(payload),
                args: null,
                CancellationToken.None);

            result.IsError.Should().BeTrue();
            var structured = result.StructuredContent!.Value;
            structured.GetProperty("success").GetBoolean().Should().BeFalse();
            structured.TryGetProperty("recovery", out var recovery).Should().BeTrue();
            recovery.ValueKind.Should().Be(JsonValueKind.Object);
        }
    }

    [Fact]
    public void RawInjectionPathNormalization_WithRejectedPathFuzz_ShouldFailClosedWithoutThrowing()
    {
        foreach (var path in RejectedPathFuzz())
        {
            var normalized = RawInjectionTargetPolicy.TryNormalizeAbsolutePath(
                path,
                candidate => PhysicalPathResolution.Resolved(candidate),
                out var normalizedPath);

            normalized.Should().BeFalse(path ?? "<null>");
            normalizedPath.Should().BeEmpty();
        }
    }

    private static IEnumerable<string> UnknownFuzzStrings()
    {
        yield return " visible ";
        yield return "largest";
        yield return "single-only";
        yield return "foreground\0";
        yield return "ALL!";
        yield return "İNVISIBLE";
        yield return new string('x', 512);
    }

    private static IEnumerable<string> InvalidJsonPayloads()
    {
        yield return string.Empty;
        yield return " ";
        yield return "not-json";
        yield return "[1,2,3]";
        yield return "\"string\"";
        yield return "{";
    }

    private static IEnumerable<Dictionary<string, object?>> FuzzedErrorPayloads()
    {
        yield return new()
        {
            ["success"] = false,
            ["error"] = "failed",
            ["errorCode"] = "InvalidArgument",
            ["hint"] = "Retry with a smaller value."
        };
        yield return new()
        {
            ["success"] = false,
            ["error"] = "timeout",
            ["errorCode"] = "Timeout",
            ["requiresReconnect"] = true,
            ["processId"] = 12345
        };
        yield return new()
        {
            ["success"] = false,
            ["error"] = "rate limited",
            ["errorCode"] = "RateLimited",
            ["retryAfterSeconds"] = 3,
            ["availableTokens"] = 0
        };
    }

    private static IEnumerable<string?> RejectedPathFuzz()
    {
        yield return null;
        yield return string.Empty;
        yield return "relative\\Target.exe";
        yield return @"\\server\share\Target.exe";
        yield return @"\\?\UNC\server\share\Target.exe";
        yield return @"\\?\GLOBALROOT\Device\Mup\server\share\Target.exe";
        yield return @"\\.\GLOBALROOT\Device\Mup\server\share\Target.exe";
    }
}
