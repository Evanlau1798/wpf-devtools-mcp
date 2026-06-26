using System.Text.Json;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Schema;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Tests for ToolCallHelper - the bridge between MCP SDK tool methods
/// and existing tool ExecuteAsync implementations.
/// </summary>
[Collection("ToolCallHelperState")]
public partial class ToolCallHelperTests
{
    // === ExecuteAndWrapAsync Tests ===

    [Fact]
    public async Task ExecuteAndWrapAsync_WithSuccessResult_ShouldReturnNonErrorResult()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = true, message = "OK" }),
            null,
            CancellationToken.None);

        result.Should().NotBeNull();
        result.IsError.Should().BeFalse();
        result.Content.Should().HaveCount(1);
        result.StructuredContent.Should().NotBeNull();
        result.StructuredContent!.Value.TryGetProperty("nextSteps", out var nextSteps).Should().BeTrue();
        nextSteps.ValueKind.Should().Be(JsonValueKind.Array);
        nextSteps.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithErrorResult_ShouldSetIsErrorTrue()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = false, error = "Not connected" }),
            null,
            CancellationToken.None);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
        result.Content.Should().HaveCount(1);
        result.StructuredContent.Should().NotBeNull();
        result.StructuredContent!.Value.TryGetProperty("nextSteps", out var nextSteps).Should().BeTrue();
        nextSteps.ValueKind.Should().Be(JsonValueKind.Array);
        nextSteps.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithArgs_ShouldPassArgsToExecute()
    {
        JsonElement? receivedArgs = null;

        var args = ToolCallHelper.BuildJsonArgs(("processId", 999));
        await ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) =>
            {
                receivedArgs = a;
                return Task.FromResult<object>(new { success = true });
            },
            args,
            CancellationToken.None);

        receivedArgs.Should().NotBeNull();
        receivedArgs!.Value.TryGetProperty("processId", out var pid).Should().BeTrue();
        pid.GetInt32().Should().Be(999);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ShouldKeepExistingResponseFields_WhenAddingNextSteps()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true, message = "OK", count = 42 }),
            null,
            CancellationToken.None);

        var structured = result.StructuredContent!.Value;
        structured.GetProperty("success").GetBoolean().Should().BeTrue();
        structured.GetProperty("message").GetString().Should().Be("OK");
        structured.GetProperty("count").GetInt32().Should().Be(42);
        structured.TryGetProperty("nextSteps", out var nextSteps).Should().BeTrue();
        nextSteps.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ShouldDeriveNextStepsFromNavigationRecommended()
    {
        var registry = new ToolNavigationRegistry();
        registry.Register("known_tool", _ => new ToolNavigationEnvelope(
            [
                new ToolNextStep(
                    "get_datacontext_chain",
                    ToolCallHelper.BuildJsonArgs(("elementId", "TextBox_1"))!.Value,
                    "Inspect the DataContext inheritance for the failing element.",
                    ToolNextStepKind.Diagnostic,
                    1)
            ],
            [
                new ToolNextStep(
                    "get_bindings",
                    ToolCallHelper.BuildJsonArgs(("elementId", "TextBox_1"))!.Value,
                    "Inspect the binding declaration directly.",
                    ToolNextStepKind.Diagnostic,
                    2)
            ],
            ["get_bindings"],
            [
                ToolNavigationReference.Create(
                    "binding-issue",
                    ("elementId", "TextBox_1"),
                    ("propertyName", "Text"),
                    ("diagnosis", "PathMismatch"))
            ]));
        using var toolCallHelperScope = ToolCallHelper.BeginTestScope(
            navigationPlanner: new ToolNavigationPlanner(registry));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true, errorCount = 1 }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345)),
            CancellationToken.None,
            toolName: "known_tool");

        var structured = result.StructuredContent!.Value;
        var navigation = structured.GetProperty("navigation");
        navigation.GetProperty("recommended")[0].GetProperty("tool").GetString().Should().Be("get_datacontext_chain");
        navigation.GetProperty("alternatives")[0].GetProperty("tool").GetString().Should().Be("get_bindings");
        navigation.GetProperty("prefetchTools")[0].GetString().Should().Be("get_bindings");
        navigation.GetProperty("contextRefs")[0].GetProperty("type").GetString().Should().Be("binding-issue");
        structured.GetProperty("nextSteps")[0].GetProperty("tool").GetString().Should().Be("get_datacontext_chain");
        structured.GetProperty("nextSteps").GetRawText().Should().Be(navigation.GetProperty("recommended").GetRawText());
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ShouldPreserveLegacyNextStepsConsumers_WhenNavigationExists()
    {
        var registry = new ToolNavigationRegistry();
        registry.Register("known_tool", _ => new ToolNavigationEnvelope(
            [
                new ToolNextStep(
                    "get_bindings",
                    ToolCallHelper.BuildJsonArgs(("elementId", "TextBox_1"))!.Value,
                    "Inspect the binding declaration directly.",
                    ToolNextStepKind.Diagnostic,
                    1)
            ],
            [],
            [],
            []));
        using var toolCallHelperScope = ToolCallHelper.BeginTestScope(
            navigationPlanner: new ToolNavigationPlanner(registry));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true }),
            null,
            CancellationToken.None,
            toolName: "known_tool");

        result.StructuredContent!.Value.TryGetProperty("nextSteps", out var nextSteps).Should().BeTrue();
        nextSteps.ValueKind.Should().Be(JsonValueKind.Array);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_bindings");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ShouldSerializeResultAsJson()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = true, count = 42 }),
            null,
            CancellationToken.None);

        var textContent = result.Content[0] as ModelContextProtocol.Protocol.TextContentBlock;
        textContent.Should().NotBeNull();
        var textPayload = JsonSerializer.Deserialize<JsonElement>(textContent!.Text);
        textPayload.GetProperty("success").GetBoolean().Should().BeTrue();
        textPayload.GetProperty("count").GetInt64().Should().Be(42);
        textPayload.GetProperty("hasStructuredContent").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ShouldRespectCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<object>(new { success = true });
            },
            null,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenToolExceedsTimeout_ShouldReturnTimeoutError()
    {
        // Arrange: Create a tool that exceeds the configured timeout
        Func<JsonElement?, CancellationToken, Task<object>> slowTool = async (args, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new { success = true };
        };

        // Act: Execute with no external cancellation
        var result = await ToolCallHelper.ExecuteAndWrapAsync(slowTool, null, CancellationToken.None, timeoutSeconds: 1);

        // Assert: Should return timeout error
        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
        var textContent = result.Content[0] as ModelContextProtocol.Protocol.TextContentBlock;
        textContent.Should().NotBeNull();
        textContent!.Text.Should().Contain("timed out");
        textContent.Text.Should().Contain("1 seconds");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenProcessIdOmittedAndSessionManagerCaptured_ShouldUseActiveProcessForTimeoutRecovery()
    {
        using var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);

        Func<JsonElement?, CancellationToken, Task<object>> slowTool = async (_, ct) =>
        {
            sessionManager.HasSession(12345).Should().BeTrue();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new { success = true };
        };

        var result = await ToolCallHelper.ExecuteAndWrapAsync(slowTool, null, CancellationToken.None, timeoutSeconds: 1);

        result.IsError.Should().BeTrue();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
        payload.GetProperty("processId").GetInt32().Should().Be(12345);
        payload.GetProperty("recovery").GetProperty("processId").GetInt32().Should().Be(12345);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenPublicConnectTimesOut_ShouldNotRequireReconnect()
    {
        Func<JsonElement?, CancellationToken, Task<object>> slowTool = async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new { success = true };
        };

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            slowTool,
            ToolCallHelper.BuildJsonArgs(("processId", 12345)),
            CancellationToken.None,
            timeoutSeconds: 1,
            toolName: "connect");

        result.IsError.Should().BeTrue();
        var payload = result.StructuredContent!.Value;
        payload.TryGetProperty("requiresReconnect", out _).Should().BeFalse();
        payload.TryGetProperty("stateAfterTimeoutUnknown", out _).Should().BeFalse();
        payload.TryGetProperty("processId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenExternalCancellation_ShouldPropagateCorrectly()
    {
        // Arrange: Create a tool that respects cancellation
        Func<JsonElement?, CancellationToken, Task<object>> cancellableTool = async (args, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new { success = true };
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act & Assert: Should throw OperationCanceledException (not return timeout error)
        var act = () => ToolCallHelper.ExecuteAndWrapAsync(cancellableTool, null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // === IsToolResultError Tests ===

    [Fact]
    public void IsToolResultError_JsonElement_WithSuccessFalse_ShouldReturnTrue()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("""{"success":false,"error":"fail"}""");
        ToolCallHelper.IsToolResultError(element).Should().BeTrue();
    }

    [Fact]
    public void IsToolResultError_JsonElement_WithSuccessTrue_ShouldReturnFalse()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("""{"success":true}""");
        ToolCallHelper.IsToolResultError(element).Should().BeFalse();
    }

    [Fact]
    public void IsToolResultError_JsonElement_WithNoSuccessField_ShouldReturnFalse()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("""{"data":"test"}""");
        ToolCallHelper.IsToolResultError(element).Should().BeFalse();
    }

    [Fact]
    public void IsToolResultError_JsonElement_WithNonObjectKind_ShouldReturnFalse()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("""[1,2,3]""");
        ToolCallHelper.IsToolResultError(element).Should().BeFalse();
    }

    // === Structured error contract tests ===

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenToolThrowsException_ShouldReturnErrorWithErrorCode()
    {
        // Arrange: Tool throws a non-cancellation exception
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new InvalidOperationException("Something went wrong internally");

        // Act
        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        // Assert: Should include errorCode for machine recovery
        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("success").GetBoolean().Should().BeFalse();
        structured.GetProperty("errorCode").GetString().Should().Be("OperationError");
        structured.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenToolThrowsArgumentException_ShouldReturnInvalidArgumentCode()
    {
        // Arrange
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new ArgumentException("Invalid parameter value");

        // Act
        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenCryptographicException_ShouldReturnSecurityErrorCode()
    {
        // Arrange: Simulates signature verification failure
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new System.Security.Cryptography.CryptographicException("localized OS error text");

        // Act
        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        // Assert: Error code should be SecurityError, message should NOT contain raw localized text
        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("SecurityError");
        structured.GetProperty("error").GetString().Should().NotContain("localized OS error text");
        structured.GetProperty("hint").GetString().Should().Contain("SHA256SUMS.txt");
        structured.GetProperty("suggestedAction").GetString().Should().Contain("-PackageArchivePath");
        structured.GetProperty("suggestedAction").GetString().Should().Contain("-TrustedReleaseMetadataDirectory");
        structured.GetProperty("recovery").GetProperty("suggestedAction").GetString().Should().Contain("original archive");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenDllPathValidatorFailsAfterWinVerifyTrust_ShouldReturnSecurityErrorCode()
    {
        var previousVerifier = DllPathValidator.WinVerifyTrustOverrideForTesting;
        var previousTrustedLocalDevelopmentBuild = DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting;
        var previousSignerThumbprint = Environment.GetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
        var previousSignerSubject = Environment.GetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT");
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var dllPath = Path.Combine(tempDirectory, "WpfDevTools.Inspector.dll");
        var verifyMethod = typeof(DllPathValidator).GetMethod(
            "VerifyAuthenticodeSignature",
            BindingFlags.NonPublic | BindingFlags.Static);
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(dllPath, "unsigned");

        try
        {
            verifyMethod.Should().NotBeNull(
                "the tests should exercise the real DllPathValidator signature verifier instead of stubbing away the validation path");
            DllPathValidator.WinVerifyTrustOverrideForTesting = _ => 0;
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = false;
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT", "TESTSIGNER00000000000000000000000000000000");
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT", null);

            Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            {
                try
                {
                    verifyMethod!.Invoke(null, new object[] { dllPath, tempDirectory });
                    return Task.FromResult<object>(new { success = true });
                }
                catch (TargetInvocationException ex) when (ex.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
                }
            };

            var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

            result.IsError.Should().BeTrue();
            var structured = result.StructuredContent!.Value;
            structured.GetProperty("errorCode").GetString().Should().Be("SecurityError");
            structured.GetProperty("error").GetString().Should().Be("Security verification failed");
        }
        finally
        {
            DllPathValidator.WinVerifyTrustOverrideForTesting = previousVerifier;
            DllPathValidator.TrustedLocalDevelopmentBuildOverrideForTesting = previousTrustedLocalDevelopmentBuild;
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT", previousSignerThumbprint);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT", previousSignerSubject);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenUnknownException_ShouldReturnInternalErrorCode()
    {
        // Arrange: Unknown exception type should not leak raw message
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new NotSupportedException("internal implementation detail");

        // Act
        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        // Assert: Should use InternalError code and hide raw message
        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("InternalError");
        structured.GetProperty("error").GetString().Should().NotContain("internal implementation detail");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenFileNotFound_ShouldReturnFileNotFoundCode()
    {
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new FileNotFoundException("secret file path info");

        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("FileNotFound");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenAccessDenied_ShouldReturnAccessDeniedCode()
    {
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new UnauthorizedAccessException("detailed access info");

        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("AccessDenied");
    }

    [Fact]
    public void ClassifyException_WhenTimeoutException_ShouldReturnTimeoutCode()
    {
        var (errorCode, message) = ToolCallHelper.ClassifyException(new TimeoutException("raw timeout detail"));

        errorCode.Should().Be("Timeout");
        message.Should().Be("Operation timed out");
    }

}
