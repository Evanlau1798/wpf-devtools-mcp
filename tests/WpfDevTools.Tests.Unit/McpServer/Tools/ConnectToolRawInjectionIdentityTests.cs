using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Tests.Unit.Inspector;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class ConnectToolRawInjectionIdentityTests
{
    [Fact]
    public async Task Execute_WhenRawInjectionTargetPathChangesBeforeInjection_ShouldFailClosed()
    {
        _ = EnsureSharedDummyBootstrapperExists();
        var processId = NextSyntheticProcessId();
        const string initiallyAllowedPath = @"C:\Allowed\OriginalTarget.exe";
        const string replacementPath = @"C:\Denied\ReplacementTarget.exe";
        var injector = new FakeProcessInjector();
        var processDetector = new ChangingProcessDetector(
            processId,
            initiallyAllowedPath,
            replacementPath);
        var tool = new ConnectTool(
            new SessionManager(),
            injector,
            processDetector,
            _ => { },
            () => false,
            pipeReadyProbe: new PipeReadyProbe((_, _) => false, () => DateTime.UtcNow, _ => { }),
            isRawInjectionTargetAllowed: process => string.Equals(
                process.ExecutablePath,
                initiallyAllowedPath,
                StringComparison.OrdinalIgnoreCase),
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("errorCode").GetString().Should().Be("SecurityError");
        payload.GetProperty("requiresExplicitTargetOptIn").GetBoolean().Should().BeTrue();
        injector.InjectWithBootstrapCallCount.Should().Be(0);
        processDetector.GetProcessInfoCallCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Execute_WhenRawInjectionTargetPathChangesBeforeExistingHostReuse_ShouldFailClosed()
    {
        _ = EnsureSharedDummyBootstrapperExists();
        var processId = Environment.ProcessId;
        var pipeName = CreateUniquePipeName($"WpfDevTools_{processId}");
        var initialDirectory = Directory.CreateTempSubdirectory("wpf-devtools-initial-target-");
        var replacementDirectory = Directory.CreateTempSubdirectory("wpf-devtools-replacement-target-");
        var initiallyAllowedPath = Path.Combine(initialDirectory.FullName, "OriginalTarget.exe");
        var replacementPath = Path.Combine(replacementDirectory.FullName, "ReplacementTarget.exe");
        File.WriteAllBytes(initiallyAllowedPath, []);
        File.WriteAllBytes(replacementPath, []);
        var injector = new FakeProcessInjector();
        var processDetector = new ChangingProcessDetector(
            processId,
            initiallyAllowedPath,
            replacementPath);
        using var host = new InspectorHost(processId, pipeName);
        using var plaintextPolicy = UnsafePlaintextInspectorHostTestEnvironment.BeginScope();
        host.Start();

        try
        {
            var tool = new ConnectTool(
                new SessionManager(),
                injector,
                processDetector,
                _ => { },
                () => false,
                pipeReadyProbe: CreateExactPipeReadyProbe(pipeName),
                isRawInjectionTargetAllowed: process => string.Equals(
                    process.ExecutablePath,
                    initiallyAllowedPath,
                    StringComparison.OrdinalIgnoreCase),
                targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

            var payload = JsonSerializer.SerializeToElement(result);
            payload.GetProperty("success").GetBoolean().Should().BeFalse();
            payload.GetProperty("errorCode").GetString().Should().Be("SecurityError");
            payload.TryGetProperty("reusedExistingHost", out _).Should().BeFalse();
            injector.InjectWithBootstrapCallCount.Should().Be(0);
            processDetector.GetProcessInfoCallCount.Should().BeGreaterThanOrEqualTo(2);
        }
        finally
        {
            host.Stop();
            initialDirectory.Delete(recursive: true);
            replacementDirectory.Delete(recursive: true);
        }
    }

    private static PipeReadyProbe CreateExactPipeReadyProbe(string pipeName)
        => new(
            (pipePath, _) => string.Equals(pipePath, $@"\\.\pipe\{pipeName}", StringComparison.Ordinal),
            () => DateTime.UtcNow,
            _ => { },
            () => [pipeName]);

    private sealed class ChangingProcessDetector(
        int expectedProcessId,
        string firstExecutablePath,
        string secondExecutablePath) : WpfProcessDetector
    {
        public int GetProcessInfoCallCount { get; private set; }

        public override WpfProcessInfo? GetProcessInfo(int processId)
        {
            processId.Should().Be(expectedProcessId);
            GetProcessInfoCallCount++;
            return new WpfProcessInfo
            {
                ProcessId = processId,
                ProcessName = "TestApp",
                Architecture = ProcessArchitecture.X64,
                Runtime = TargetRuntime.NetCore,
                IsWpfApplication = true,
                ExecutablePath = GetProcessInfoCallCount == 1
                    ? firstExecutablePath
                    : secondExecutablePath
            };
        }
    }

    private sealed class FakeProcessInjector : IProcessInjector
    {
        public int InjectWithBootstrapCallCount { get; private set; }

        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
            => InjectionResult.CreateSuccess(processId, dllPath);

        public InjectionError ValidateTarget(int processId) => InjectionError.None;

        public InjectionResult InjectWithBootstrap(
            InjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            InjectWithBootstrapCallCount++;
            return InjectionResult.CreateSuccess(
                request.ProcessId,
                request.InspectorDllPath,
                bootstrapExitCode: 0,
                pipeName: request.ExpectedPipeName);
        }
    }

}
