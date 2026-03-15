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

[Collection("LiveBootstrapIntegration")]
public sealed class BootstrapEventTraceIntegrationTests : IDisposable
{
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

        _testApp = StartTestApp();
        var sessionManager = new SessionManager();
        var connectTool = new ConnectTool(sessionManager, new ProcessInjector(), new WpfProcessDetector());
        var getLogicalTreeTool = new GetLogicalTreeTool(sessionManager);
        var getNamescopeTool = new GenericPipeTool(sessionManager, "get_namescope");
        var clickTool = new ClickElementTool(sessionManager);
        var traceTool = new TraceRoutedEventsTool(sessionManager);

        var connectResult = await ExecuteToolAsync(
            connectTool,
            new { processId = _testApp.Id });

        connectResult.GetProperty("success").GetBoolean().Should().BeTrue(connectResult.GetRawText());

        var logicalTree = await ExecuteToolAsync(
            getLogicalTreeTool,
            new { processId = _testApp.Id, depth = 3, summaryOnly = true, maxNodes = 64, maxChildrenPerNode = 24 });
        _output.WriteLine(logicalTree.GetRawText());

        var stylesTabId = GetNthElementId(logicalTree, type: "TabItem", ordinal: 4);
        stylesTabId.Should().NotBeNullOrEmpty("the 4th tab in MainWindow is Styles & Templates");
        _output.WriteLine($"Styles tab id: {stylesTabId}");

        var clickTabResult = await ExecuteToolAsync(
            clickTool,
            new { processId = _testApp.Id, elementId = stylesTabId });

        clickTabResult.GetProperty("success").GetBoolean().Should().BeTrue(clickTabResult.GetRawText());
        await Task.Delay(250);

        var namescope = await ExecuteToolAsync(
            getNamescopeTool,
            new { processId = _testApp.Id });

        var checkBoxId = GetNamedElementId(namescope, "EnableHighlightCheckBox");
        checkBoxId.Should().NotBeNullOrEmpty(namescope.GetRawText());
        _output.WriteLine($"EnableHighlightCheckBox id: {checkBoxId}");

        var traceStart = await ExecuteToolAsync(
            traceTool,
            new { processId = _testApp.Id, elementId = checkBoxId, eventName = "Click", mode = "start", duration = 2000 });

        traceStart.GetProperty("success").GetBoolean().Should().BeTrue(traceStart.GetRawText());

        var clickCheckBox = await ExecuteToolAsync(
            clickTool,
            new { processId = _testApp.Id, elementId = checkBoxId });

        clickCheckBox.GetProperty("success").GetBoolean().Should().BeTrue(clickCheckBox.GetRawText());
        await Task.Delay(150);

        var traceGet = await ExecuteToolAsync(
            traceTool,
            new { processId = _testApp.Id, mode = "get" });

        _output.WriteLine(traceGet.GetRawText());
        traceGet.GetProperty("success").GetBoolean().Should().BeTrue(traceGet.GetRawText());
        traceGet.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0, traceGet.GetRawText());
    }

    private async Task<JsonElement> ExecuteToolAsync(object tool, object args)
    {
        var method = tool.GetType().GetMethod("ExecuteAsync");
        method.Should().NotBeNull();

        var arguments = JsonSerializer.SerializeToElement(args);
        var task = method!.Invoke(tool, new object?[] { arguments, CancellationToken.None }) as Task<object>;
        task.Should().NotBeNull();

        var result = await task!;
        return JsonSerializer.SerializeToElement(result);
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
        var solutionDir = FindSolutionRoot();
        var candidates = new[]
        {
            Path.Combine(solutionDir, "tests", "WpfDevTools.Tests.TestApp", "bin", "Debug", "net8.0-windows", "WpfDevTools.Tests.TestApp.exe"),
            Path.Combine(solutionDir, "tests", "WpfDevTools.Tests.TestApp", "bin", "Release", "net8.0-windows", "WpfDevTools.Tests.TestApp.exe")
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("TestApp executable not found. Build tests/WpfDevTools.Tests.TestApp first.");
    }

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WpfDevTools.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Solution root not found");
    }

    private static Process StartTestApp()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = FindTestAppExe(),
            UseShellExecute = true
        });

        process.Should().NotBeNull();
        Thread.Sleep(3000);
        return process!;
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
