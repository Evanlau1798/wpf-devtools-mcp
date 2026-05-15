using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfAndBootstrapIntegration")]
public sealed class BootstrapEventTraceIntegrationTests : IDisposable
{
    private static readonly TimeSpan LiveTestAppStartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LiveTraceTimeout = TimeSpan.FromSeconds(60);
    private readonly ITestOutputHelper _output;
    private Process? _testApp;

    public BootstrapEventTraceIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TraceRoutedEvents_AfterBootstrapInjection_ShouldCaptureGoldenSampleCheckBoxClick()
    {
        BootstrapperArtifactLocator.HasNativeBootstrapper(AppContext.BaseDirectory).Should().BeTrue(
            "the live bootstrap smoke test requires native bootstrapper artifacts; build src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj first");

        using var testTimeoutCts = new CancellationTokenSource(LiveTraceTimeout);

        _testApp = StartTestApp();
        using var sessionManager = new SessionManager();
        var connectTool = new ConnectTool(sessionManager, new ProcessInjector(), new WpfProcessDetector(),
            dllPathValidator: TrustedLocalReleaseSignatureSkip.ValidateDllPath,
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: _ => new McpTargetAuthorization(true, null, null));
        var getLogicalTreeTool = new GetLogicalTreeTool(sessionManager);
        var getNamescopeTool = new GenericPipeTool(sessionManager, "get_namescope");
        var getInteractionReadinessTool = new GetInteractionReadinessTool(sessionManager);
        var clickTool = new ClickElementTool(sessionManager);
        var traceTool = new TraceRoutedEventsTool(sessionManager);

        var connectResult = await ExecuteToolAsync(
            connectTool,
            new { processId = _testApp.Id },
            testTimeoutCts.Token);

        connectResult.GetProperty("success").GetBoolean().Should().BeTrue(connectResult.GetRawText());

        var logicalTree = await ExecuteToolAsync(
            getLogicalTreeTool,
            new { processId = _testApp.Id, depth = 3, summaryOnly = true, maxNodes = 64, maxChildrenPerNode = 24 },
            testTimeoutCts.Token);
        _output.WriteLine(logicalTree.GetRawText());

        var stylesTabId = GetNthElementId(logicalTree, type: "TabItem", ordinal: 4);
        stylesTabId.Should().NotBeNullOrEmpty("the 4th tab in MainWindow is Styles & Templates");
        _output.WriteLine($"Styles tab id: {stylesTabId}");

        var clickTabResult = await ExecuteToolAsync(
            clickTool,
            new { processId = _testApp.Id, elementId = stylesTabId },
            testTimeoutCts.Token);

        clickTabResult.GetProperty("success").GetBoolean().Should().BeTrue(clickTabResult.GetRawText());
        var namescope = await ExecuteToolAsync(
            getNamescopeTool,
            new { processId = _testApp.Id },
            testTimeoutCts.Token);
        var checkBoxId = GetNamedElementId(namescope, "EnableHighlightCheckBox");
        checkBoxId.Should().NotBeNullOrEmpty(namescope.GetRawText());
        await WaitForInteractionReadinessAsync(
            connectTool,
            getInteractionReadinessTool,
            _testApp,
            checkBoxId!,
            testTimeoutCts.Token);
        _output.WriteLine($"EnableHighlightCheckBox id: {checkBoxId}");

        var traceStart = await ExecuteToolAsync(
            traceTool,
            new { processId = _testApp.Id, elementId = checkBoxId, eventName = "Click", mode = "start", duration = 2000 },
            testTimeoutCts.Token);

        traceStart.GetProperty("success").GetBoolean().Should().BeTrue(traceStart.GetRawText());

        var clickCheckBox = await ExecuteToolAsync(
            clickTool,
            new { processId = _testApp.Id, elementId = checkBoxId },
            testTimeoutCts.Token);

        clickCheckBox.GetProperty("success").GetBoolean().Should().BeTrue(clickCheckBox.GetRawText());
        var traceGet = await WaitForTraceEventAsync(traceTool, _testApp.Id, testTimeoutCts.Token);

        _output.WriteLine(traceGet.GetRawText());
        traceGet.GetProperty("success").GetBoolean().Should().BeTrue(traceGet.GetRawText());
        traceGet.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0, traceGet.GetRawText());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TraceRoutedEvents_AfterBootstrapInjection_ShouldCaptureGoldenSampleEventTraceLabClick()
    {
        BootstrapperArtifactLocator.HasNativeBootstrapper(AppContext.BaseDirectory).Should().BeTrue(
            "the live bootstrap smoke test requires native bootstrapper artifacts; build src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj first");

        using var testTimeoutCts = new CancellationTokenSource(LiveTraceTimeout);

        _testApp = StartTestApp();
        using var sessionManager = new SessionManager();
        var connectTool = new ConnectTool(sessionManager, new ProcessInjector(), new WpfProcessDetector(),
            dllPathValidator: TrustedLocalReleaseSignatureSkip.ValidateDllPath,
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: _ => new McpTargetAuthorization(true, null, null));
        var getNamescopeTool = new GenericPipeTool(sessionManager, "get_namescope");
        var getInteractionReadinessTool = new GetInteractionReadinessTool(sessionManager);
        var clickTool = new ClickElementTool(sessionManager);
        var traceTool = new TraceRoutedEventsTool(sessionManager);

        var connectResult = await ExecuteToolAsync(connectTool, new { processId = _testApp.Id }, testTimeoutCts.Token);
        connectResult.GetProperty("success").GetBoolean().Should().BeTrue(connectResult.GetRawText());

        var namescope = await ExecuteToolAsync(
            getNamescopeTool,
            new { processId = _testApp.Id },
            testTimeoutCts.Token);

        var tabId = GetNamedElementId(namescope, "EventTraceLabTab");
        tabId.Should().NotBeNullOrEmpty(namescope.GetRawText());
        var buttonId = GetNamedElementId(namescope, "EventStormButton");
        buttonId.Should().NotBeNullOrEmpty(namescope.GetRawText());

        var clickTabResult = await ExecuteToolAsync(
            clickTool,
            new { processId = _testApp.Id, elementId = tabId },
            testTimeoutCts.Token);
        clickTabResult.GetProperty("success").GetBoolean().Should().BeTrue(clickTabResult.GetRawText());
        await WaitForInteractionReadinessAsync(
            connectTool,
            getInteractionReadinessTool,
            _testApp,
            buttonId!,
            testTimeoutCts.Token);

        var traceStart = await ExecuteToolAsync(
            traceTool,
            new { processId = _testApp.Id, elementId = buttonId, eventName = "Click", mode = "start", duration = 2000 },
            testTimeoutCts.Token);
        traceStart.GetProperty("success").GetBoolean().Should().BeTrue(traceStart.GetRawText());

        var clickButton = await ExecuteToolAsync(
            clickTool,
            new { processId = _testApp.Id, elementId = buttonId },
            testTimeoutCts.Token);
        clickButton.GetProperty("success").GetBoolean().Should().BeTrue(clickButton.GetRawText());

        var traceGet = await WaitForTraceEventAsync(traceTool, _testApp.Id, testTimeoutCts.Token);

        traceGet.GetProperty("success").GetBoolean().Should().BeTrue(traceGet.GetRawText());
        traceGet.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0, traceGet.GetRawText());
    }

    private async Task<JsonElement> WaitForInteractionReadinessAsync(
        object connectTool,
        object getInteractionReadinessTool,
        Process targetProcess,
        string elementId,
        CancellationToken cancellationToken)
    {
        var reconnectAttempted = false;

        return await ConditionWaiter.WaitForAsync(
            async () =>
            {
                var readinessPayload = await ExecuteToolAsync(
                    getInteractionReadinessTool,
                    new { processId = targetProcess.Id, elementId, interactionType = "Click" },
                    cancellationToken);

                if (!reconnectAttempted
                    && IsNotConnected(readinessPayload)
                    && IsTargetProcessStillRunning(targetProcess))
                {
                    reconnectAttempted = true;
                    await ExecuteToolAsync(connectTool, new { processId = targetProcess.Id }, cancellationToken);

                    readinessPayload = await ExecuteToolAsync(
                        getInteractionReadinessTool,
                        new { processId = targetProcess.Id, elementId, interactionType = "Click" },
                        cancellationToken);
                }

                return readinessPayload;
            },
            IsInteractionReady,
            TimeSpan.FromSeconds(10),
            $"Timed out waiting for get_interaction_readiness to report element {elementId} as ready after activating the target tab.");
    }

    private async Task<JsonElement> WaitForTraceEventAsync(
        object traceTool,
        int processId,
        CancellationToken cancellationToken)
    {
        return await ConditionWaiter.WaitForAsync(
            () => ExecuteToolAsync(traceTool, new { processId, mode = "get" }, cancellationToken),
            payload => payload.GetProperty("eventCount").GetInt32() > 0,
            TimeSpan.FromSeconds(2),
            "Timed out waiting for trace_routed_events(mode='get') to capture the fired event.");
    }

    private async Task<JsonElement> ExecuteToolAsync(object tool, object args, CancellationToken cancellationToken)
    {
        var method = tool.GetType().GetMethod("ExecuteAsync");
        method.Should().NotBeNull();

        var arguments = JsonSerializer.SerializeToElement(args);
        var task = method!.Invoke(tool, new object?[] { arguments, cancellationToken }) as Task<object>;
        task.Should().NotBeNull();

        var result = await task!.WaitAsync(cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    private static bool IsInteractionReady(JsonElement payload)
        => TryGetBoolean(payload, "success", out var success)
            && success
            && TryGetBoolean(payload, "isReady", out var isReady)
            && isReady;

    private static bool IsNotConnected(JsonElement payload)
        => TryGetString(payload, "errorCode", out var errorCode)
            && string.Equals(errorCode, "NotConnected", StringComparison.Ordinal);

    private static bool IsTargetProcessStillRunning(Process targetProcess)
    {
        try
        {
            targetProcess.Refresh();
            return !targetProcess.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryGetBoolean(JsonElement payload, string propertyName, out bool value)
    {
        if (payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryGetString(JsonElement payload, string propertyName, out string? value)
    {
        if (payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static string? GetNthElementId(JsonElement payload, string type, int ordinal)
    {
        var columns = payload.GetProperty("columns").EnumerateArray().Select(column => column.GetString()).ToArray();
        var typeIndex = Array.IndexOf(columns, "type");
        var elementIdIndex = Array.IndexOf(columns, "elementId");
        var matchedCount = 0;

        foreach (var node in payload.GetProperty("nodes").EnumerateArray())
        {
            var values = node.EnumerateArray().ToArray();
            if (!string.Equals(values[typeIndex].GetString(), type, StringComparison.Ordinal))
            {
                continue;
            }

            matchedCount++;
            if (matchedCount == ordinal)
            {
                return values[elementIdIndex].GetString();
            }
        }

        return null;
    }

    private static string? GetNamedElementId(JsonElement payload, string name)
    {
        foreach (var namedElement in payload.GetProperty("namedElements").EnumerateArray())
        {
            if (string.Equals(namedElement.GetProperty("name").GetString(), name, StringComparison.Ordinal))
            {
                return namedElement.GetProperty("elementId").GetString();
            }
        }

        return null;
    }

    private static string FindTestAppExe()
    {
        return TestAppProcessLauncher.FindTestAppExe();
    }

    private static Process StartTestApp()
    {
        return TestAppProcessLauncher.StartAndWaitForMainWindow(FindTestAppExe(), LiveTestAppStartupTimeout);
    }

    public void Dispose()
    {
        if (_testApp != null && !_testApp.HasExited)
        {
            _testApp.Kill();
            _testApp.WaitForExit(5000);
            _testApp.Dispose();
        }
    }
}
