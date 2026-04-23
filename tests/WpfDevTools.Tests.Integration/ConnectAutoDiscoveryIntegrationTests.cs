using System.IO.Pipes;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using System.Threading;

namespace WpfDevTools.Tests.Integration;

public sealed class ConnectAutoDiscoverySelectionTests : IDisposable
{
    private static int _syntheticProcessId = 1_500_000_000;
    private string? _temporaryArtifactsRoot;
    private string? _dummyBootstrapperPath;

    [Fact]
    [Trait("Category", "SyntheticIntegration")]
    public async Task ConnectTool_WithoutProcessId_ShouldAutoConnectSingleCandidate()
    {
        EnsureDummyBootstrapperExists();
        var processId = Environment.ProcessId;
        using var server = CreateServer(processId);
        using var sessionManager = new SessionManager();
        var tool = CreateTool(
            sessionManager,
            new FakeProcessDetector(CreateProcessInfo(processId, "SingleApp")),
            new FakeProcessInjector());

        var result = await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("autoDiscovered").GetBoolean().Should().BeTrue();
        json.GetProperty("candidateCount").GetInt32().Should().Be(1);
        sessionManager.TryGetActiveProcessId(out var activeProcessId).Should().BeTrue();
        activeProcessId.Should().Be(processId);
    }

    [Fact]
    [Trait("Category", "SyntheticIntegration")]
    public async Task ConnectTool_WithoutProcessId_ShouldReturnCandidateListForMultipleProcesses()
    {
        using var sessionManager = new SessionManager();
        var firstProcessId = NextSyntheticProcessId();
        var secondProcessId = NextSyntheticProcessId();
        var tool = CreateTool(
            sessionManager,
            new FakeProcessDetector(
                CreateProcessInfo(firstProcessId, "AppA"),
                CreateProcessInfo(secondProcessId, "AppB")),
            new FakeProcessInjector());

        var result = await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("MultipleWpfProcessesFound");
        json.GetProperty("candidateCount").GetInt32().Should().Be(2);
        json.GetProperty("processes").GetArrayLength().Should().Be(2);
    }

    [Fact]
    [Trait("Category", "SyntheticIntegration")]
    public async Task ConnectTool_WithLargestWorkingSetStrategy_ShouldAutoSelectLargestCandidate()
    {
        EnsureDummyBootstrapperExists();
        var smallProcessId = NextSyntheticProcessId();
        var largeProcessId = Environment.ProcessId;
        using var server = CreateServer(largeProcessId);
        using var sessionManager = new SessionManager();
        var tool = CreateTool(
            sessionManager,
            new FakeProcessDetector(
                CreateProcessInfo(smallProcessId, "AppA"),
                CreateProcessInfo(largeProcessId, "AppB")),
            new FakeProcessInjector(),
            processId => processId == largeProcessId ? 900_000_000 : 100_000_000);

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { selectionStrategy = "largest_working_set" }),
            CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("processId").GetInt32().Should().Be(largeProcessId);
        json.GetProperty("autoSelected").GetBoolean().Should().BeTrue();
        json.GetProperty("selectionReason").GetString().Should().Be("largest_working_set");
        json.GetProperty("processes").GetArrayLength().Should().Be(2);
    }

    [Fact]
    [Trait("Category", "SyntheticIntegration")]
    public async Task ConnectTool_WithWindowFilterAll_ShouldForwardWindowFilter()
    {
        using var sessionManager = new SessionManager();
        var processId = NextSyntheticProcessId();
        var detector = new FakeProcessDetector(CreateProcessInfo(processId, "SingleApp"));
        var tool = CreateTool(
            sessionManager,
            detector,
            new FakeProcessInjector());

        var result = await tool.ExecuteAsync(ToJsonElement(new { windowFilter = "all" }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        detector.RequestedWindowFilters.Should().ContainSingle().Which.Should().Be(ProcessWindowFilter.All);
    }

    public void Dispose()
    {
        if (!string.IsNullOrWhiteSpace(_temporaryArtifactsRoot) && Directory.Exists(_temporaryArtifactsRoot))
        {
            try { Directory.Delete(_temporaryArtifactsRoot, recursive: true); } catch { }
        }
    }

    private ConnectTool CreateTool(
        SessionManager sessionManager,
        FakeProcessDetector detector,
        FakeProcessInjector injector,
        Func<int, long>? workingSetResolver = null)
    {
        return new ConnectTool(
            sessionManager,
            injector,
            detector,
            _ => { },
            () => false,
            workingSetResolver,
            bootstrapperCandidateResolver: _ => string.IsNullOrWhiteSpace(_dummyBootstrapperPath)
                ? []
                : [_dummyBootstrapperPath],
            pipeReadyProbe: new PipeReadyProbe((_, _) => false, () => DateTime.UtcNow, _ => { }),
            isRawInjectionTargetAllowed: _ => true);
    }

    private void EnsureDummyBootstrapperExists()
    {
        _temporaryArtifactsRoot ??= Path.Combine(Path.GetTempPath(), "WpfDevTools_ConnectAutoDiscovery_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporaryArtifactsRoot);
        _dummyBootstrapperPath = Path.Combine(_temporaryArtifactsRoot, "WpfDevTools.Bootstrapper.x64.dll");
        if (!File.Exists(_dummyBootstrapperPath))
        {
            File.WriteAllBytes(_dummyBootstrapperPath, Array.Empty<byte>());
        }
    }

    private static NamedPipeServerStream CreateServer(int processId)
    {
        var server = new NamedPipeServerStream(
            $"WpfDevTools_{processId}",
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        _ = Task.Run(async () =>
        {
            try
            {
                await server.WaitForConnectionAsync();

                var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);

                request.Should().NotBeNull();
                request!.Method.Should().Be("ping");

                var response = new InspectorResponse
                {
                    Id = request.Id,
                    CorrelationId = request.CorrelationId,
                    Result = JsonSerializer.SerializeToElement(new
                    {
                        success = true,
                        status = "pong",
                        processId,
                        protocolVersion = InspectorCompatibilityContract.ProtocolVersion,
                        buildFingerprint = InspectorCompatibilityContract.GetBuildFingerprint(typeof(NamedPipeClient))
                    }),
                    Error = null
                };

                await MessageFraming.WriteMessageAsync(
                    server,
                    JsonSerializer.Serialize(response),
                    CancellationToken.None);

                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        return server;
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

    private static JsonElement ToJsonElement(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static int NextSyntheticProcessId()
        => Interlocked.Increment(ref _syntheticProcessId);

    private sealed class FakeProcessDetector(params WpfProcessInfo[] processes) : WpfProcessDetector
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
