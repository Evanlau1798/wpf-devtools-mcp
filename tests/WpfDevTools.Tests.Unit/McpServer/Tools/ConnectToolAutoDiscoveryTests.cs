using System.Reflection;
using System.Text.Json;
using System.IO.Pipes;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("ProcessDiscovery")]
public sealed class ConnectToolAutoDiscoveryTests : IDisposable
{
    private string? _dummyBootstrapperPath;

    [Fact]
    public async Task Execute_WithoutProcessId_AndSingleWpfProcess_ShouldAutoConnect()
    {
        EnsureDummyBootstrapperExists();
        var sessionManager = new SessionManager();
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_{processId}";
        using var server = CreateServer(pipeName);
        var tool = CreateTool(
            sessionManager,
            detector: new FakeAutoDiscoveryProcessDetector(CreateProcessInfo(processId, "SingleApp")),
            injector: new FakeProcessInjector());

        var result = await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("processId").GetInt32().Should().Be(processId);
        json.GetProperty("autoDiscovered").GetBoolean().Should().BeTrue();
        json.GetProperty("candidateCount").GetInt32().Should().Be(1);
        sessionManager.TryGetActiveProcessId(out var activeProcessId).Should().BeTrue();
        activeProcessId.Should().Be(processId);
    }

    [Fact]
    public async Task Execute_WithoutProcessId_AndNoWpfProcesses_ShouldReturnStructuredError()
    {
        var tool = CreateTool(detector: new FakeAutoDiscoveryProcessDetector());

        var result = await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("NoWpfProcessesFound");
    }

    [Fact]
    public async Task Execute_WithoutProcessId_AndMultipleWpfProcesses_ShouldReturnCandidates()
    {
        var tool = CreateTool(detector: new FakeAutoDiscoveryProcessDetector(
            CreateProcessInfo(111, "AppA"),
            CreateProcessInfo(222, "AppB")));

        var result = await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("MultipleWpfProcessesFound");
        json.GetProperty("candidateCount").GetInt32().Should().Be(2);
        json.GetProperty("processes").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Execute_WithoutProcessId_AndLargestWorkingSetStrategy_ShouldAutoSelectLargestCandidate()
    {
        EnsureDummyBootstrapperExists();
        var sessionManager = new SessionManager();
        var smallProcessId = NextSyntheticProcessId();
        var largeProcessId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_{largeProcessId}";
        using var server = CreateServer(pipeName);
        var tool = CreateTool(
            sessionManager,
            detector: new FakeAutoDiscoveryProcessDetector(
                CreateProcessInfo(smallProcessId, "SmallApp"),
                CreateProcessInfo(largeProcessId, "LargeApp")),
            injector: new FakeProcessInjector(),
            workingSetResolver: processId => processId == largeProcessId ? 500_000_000 : 10_000_000);

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { selectionStrategy = "largest_working_set" }),
            CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("processId").GetInt32().Should().Be(largeProcessId);
        json.GetProperty("autoSelected").GetBoolean().Should().BeTrue();
        json.GetProperty("selectionReason").GetString().Should().Be("largest_working_set");
        json.GetProperty("candidateCount").GetInt32().Should().Be(2);
        json.GetProperty("processes").GetArrayLength().Should().Be(2);
        sessionManager.TryGetActiveProcessId(out var activeProcessId).Should().BeTrue();
        activeProcessId.Should().Be(largeProcessId);
    }

    [Fact]
    public async Task Execute_WithInvalidSelectionStrategy_ShouldReturnValidationError()
    {
        var processId = NextSyntheticProcessId();
        var tool = CreateTool(detector: new FakeAutoDiscoveryProcessDetector(CreateProcessInfo(processId, "SingleApp")));

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { selectionStrategy = "unknown" }),
            CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("selectionStrategy");
    }

    [Fact]
    public async Task Execute_WithNonStringSelectionStrategy_ShouldReturnInvalidArgument()
    {
        var processId = NextSyntheticProcessId();
        var tool = CreateTool(detector: new FakeAutoDiscoveryProcessDetector(CreateProcessInfo(processId, "SingleApp")));

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { selectionStrategy = 1 }),
            CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("selectionStrategy");
    }

    [Fact]
    public async Task Execute_WithoutWindowFilter_ShouldDefaultAutoDiscoveryToVisible()
    {
        EnsureDummyBootstrapperExists();
        var sessionManager = new SessionManager();
        var processId = NextSyntheticProcessId();
        using var server = CreateServer($"WpfDevTools_{processId}");
        var detector = new FakeAutoDiscoveryProcessDetector(CreateProcessInfo(processId, "SingleApp"));
        var tool = CreateTool(
            sessionManager,
            detector: detector,
            injector: new FakeProcessInjector());

        var result = await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        detector.RequestedWindowFilters.Should().Contain(ProcessWindowFilter.Visible);
    }

    [Fact]
    public async Task Execute_WithWindowFilterAll_ShouldForwardAllFilter()
    {
        EnsureDummyBootstrapperExists();
        var sessionManager = new SessionManager();
        var processId = NextSyntheticProcessId();
        using var server = CreateServer($"WpfDevTools_{processId}");
        var detector = new FakeAutoDiscoveryProcessDetector(CreateProcessInfo(processId, "SingleApp"));
        var tool = CreateTool(
            sessionManager,
            detector: detector,
            injector: new FakeProcessInjector());

        var result = await tool.ExecuteAsync(ToJsonElement(new { windowFilter = "all" }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        detector.RequestedWindowFilters.Should().Contain(ProcessWindowFilter.All);
    }

    [Fact]
    public async Task Execute_WithInvalidWindowFilter_ShouldReturnValidationError()
    {
        var processId = NextSyntheticProcessId();
        var tool = CreateTool(detector: new FakeAutoDiscoveryProcessDetector(CreateProcessInfo(processId, "SingleApp")));

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { windowFilter = "bad-filter" }),
            CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("windowFilter");
    }

    [Fact]
    public async Task Execute_WithNonStringWindowFilter_ShouldReturnInvalidArgument()
    {
        var processId = NextSyntheticProcessId();
        var tool = CreateTool(detector: new FakeAutoDiscoveryProcessDetector(CreateProcessInfo(processId, "SingleApp")));

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { windowFilter = true }),
            CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("windowFilter");
    }

    [Fact]
    public async Task Execute_WithoutProcessId_ShouldUseInitialAutoDiscoveryResolutionForSuccessPayload()
    {
        EnsureDummyBootstrapperExists();
        var sessionManager = new SessionManager();
        var initialProcessId = NextSyntheticProcessId();
        var changedProcessId = NextSyntheticProcessId();
        using var server = CreateServer($"WpfDevTools_{initialProcessId}");
        var detector = new SequencedProcessDetector(
            [CreateProcessInfo(initialProcessId, "InitialApp")],
            [CreateProcessInfo(changedProcessId, "ChangedApp")]);
        var tool = CreateTool(
            sessionManager,
            detector: detector,
            injector: new FakeProcessInjector());

        var result = await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("processId").GetInt32().Should().Be(initialProcessId);
        json.GetProperty("processName").GetString().Should().Be("InitialApp");
        json.GetProperty("autoDiscovered").GetBoolean().Should().BeTrue();
    }

    public void Dispose()
    {
    }

    private static ConnectTool CreateTool(
        SessionManager? sessionManager = null,
        WpfProcessDetector? detector = null,
        FakeProcessInjector? injector = null,
        Func<int, long>? workingSetResolver = null)
    {
        return new ConnectTool(
            sessionManager ?? new SessionManager(),
            injector ?? new FakeProcessInjector(),
            detector ?? new FakeAutoDiscoveryProcessDetector(),
            _ => { },
            () => false,
            workingSetResolver);
    }

    private void EnsureDummyBootstrapperExists()
    {
        _dummyBootstrapperPath = EnsureSharedDummyBootstrapperExists();
    }

    private static WpfProcessInfo CreateProcessInfo(int processId, string processName)
    {
        return new WpfProcessInfo
        {
            ProcessId = processId,
            ProcessName = processName,
            WindowTitle = processName + " Window",
            Architecture = ProcessArchitecture.X64,
            Runtime = TargetRuntime.NetCore,
            DotNetVersion = ".NET 8.0",
            IsWpfApplication = true,
            IsElevated = false
        };
    }

    private static NamedPipeServerStream CreateServer(string pipeName)
    {
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        _ = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            await Task.Delay(TimeSpan.FromSeconds(5));
        });

        return server;
    }

    private sealed class FakeAutoDiscoveryProcessDetector(params WpfProcessInfo[] processes) : WpfProcessDetector
    {
        private readonly IReadOnlyList<WpfProcessInfo> _processes = processes;
        internal List<ProcessWindowFilter> RequestedWindowFilters { get; } = [];

        public override IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses(ProcessWindowFilter windowFilter)
        {
            RequestedWindowFilters.Add(windowFilter);
            return _processes;
        }

        public override WpfProcessInfo? GetProcessInfo(int processId)
            => _processes.FirstOrDefault(process => process.ProcessId == processId);
    }

    private sealed class SequencedProcessDetector(
        IReadOnlyList<WpfProcessInfo> initialProcesses,
        IReadOnlyList<WpfProcessInfo> subsequentProcesses) : WpfProcessDetector
    {
        private int _enumerationCount;

        public override IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses(ProcessWindowFilter windowFilter)
        {
            var current = Interlocked.Increment(ref _enumerationCount) == 1
                ? initialProcesses
                : subsequentProcesses;
            return current;
        }

        public override WpfProcessInfo? GetProcessInfo(int processId)
        {
            var all = initialProcesses.Concat(subsequentProcesses);
            return all.FirstOrDefault(process => process.ProcessId == processId);
        }
    }

    private sealed class FakeProcessInjector : IProcessInjector
    {
        public InjectionError ValidateTarget(int processId) => InjectionError.None;

        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
            => InjectionResult.CreateSuccess(processId, dllPath);

        public InjectionResult InjectWithBootstrap(InjectionRequest request, CancellationToken cancellationToken = default)
            => InjectionResult.CreateSuccess(
                request.ProcessId,
                request.InspectorDllPath,
                bootstrapExitCode: 0,
                pipeName: request.ExpectedPipeName);
    }
}
