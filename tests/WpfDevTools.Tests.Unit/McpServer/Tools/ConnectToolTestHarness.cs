using Xunit;
using FluentAssertions;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Pipes;
using System.Reflection;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Security;
using WpfDevTools.Tests.Unit.Inspector;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public partial class ConnectToolTests
{
    private string? _dummyBootstrapperPath;

    private static ConnectTool CreateTool(
        SessionManager? sessionManager = null,
        FakeProcessInjector? injector = null,
        WpfProcessDetector? processDetector = null,
        Action<string>? dllPathValidator = null,
        Func<bool>? isCurrentProcessElevated = null,
        PipeReadyProbe? pipeReadyProbe = null,
        Func<WpfProcessInfo, bool>? isRawInjectionTargetAllowed = null,
        Func<WpfProcessInfo, McpTargetAuthorization>? targetPolicy = null,
        Func<int, TimeSpan, CancellationToken, Task<NamedPipeConnectFailure>>? connectInjectedSessionAsync = null,
        TimeSpan? connectTimeout = null)
    {
        return new ConnectTool(
            sessionManager: sessionManager ?? new SessionManager(),
            injector: injector ?? new FakeProcessInjector(),
            processDetector: processDetector ?? new FakeProcessDetector(),
            dllPathValidator: dllPathValidator ?? (_ => { }),
            isCurrentProcessElevated: isCurrentProcessElevated ?? (() => false),
            workingSetResolver: null,
            inspectorCandidateResolver: null,
            bootstrapperCandidateResolver: null,
            pipeReadyProbe: pipeReadyProbe,
            isRawInjectionTargetAllowed: isRawInjectionTargetAllowed ?? (_ => true),
                targetPolicy: targetPolicy ?? ConnectToolTestPolicies.AllowAllTargets,
            connectInjectedSessionAsync: connectInjectedSessionAsync,
            connectTimeout: connectTimeout);
    }

    private void EnsureDummyBootstrapperExists()
    {
        _dummyBootstrapperPath = EnsureSharedDummyBootstrapperExists();
    }

    public void Dispose()
    {
    }

    private static PipeReadyProbe CreateExactPipeReadyProbe(string pipeName)
        => new(
            (pipePath, _) => string.Equals(pipePath, $@"\\.\pipe\{pipeName}", StringComparison.Ordinal),
            () => DateTime.UtcNow,
            _ => { },
            () => [pipeName]);

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate solution root for ConnectTool test.");
    }

    private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        ReplaceSessionManagerPipeClient(sessionManager, processId, replacement);
    }

    private sealed class FakeProcessDetector(
        bool isElevated = false,
        string? executablePath = null,
        ProcessArchitecture architecture = ProcessArchitecture.X64) : WpfProcessDetector
    {
        public override WpfProcessInfo? GetProcessInfo(int processId)
        {
            return new WpfProcessInfo
            {
                ProcessId = processId,
                ProcessName = "TestApp",
                Architecture = architecture,
                Runtime = TargetRuntime.NetCore,
                IsWpfApplication = true,
                IsElevated = isElevated,
                ExecutablePath = executablePath
            };
        }
    }

    private static string CreateSdkOnlyExecutablePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-sdk-only-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var executablePath = Path.Combine(directory, "TestApp.exe");
        File.WriteAllBytes(executablePath, Array.Empty<byte>());
        return executablePath;
    }

    private static void DeleteSdkOnlyExecutablePath(string executablePath)
    {
        if (File.Exists(executablePath))
        {
            File.Delete(executablePath);
        }

        var directory = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class EmptyProcessDetector : WpfProcessDetector
    {
        public override IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses(ProcessWindowFilter windowFilter)
            => [];
    }

    private class FakeProcessInjector : IProcessInjector
    {
        public InjectionError ValidationResult { get; init; } = InjectionError.None;
        public bool ShouldFailInjection { get; init; }
        public string InjectionErrorMessage { get; init; } = "Injection failed";
        public BootstrapStage? FailedStage { get; init; }
        public int? FailedExitCode { get; init; }
        public InjectionError FailedError { get; init; } = InjectionError.BootstrapFailed;
        public int InjectWithBootstrapCallCount { get; private set; }
        public CancellationToken LastInjectWithBootstrapCancellationToken { get; private set; }
        public InjectionRequest? LastInjectionRequest { get; private set; }
        public bool CertificateFileExistedAtInjection { get; private set; }
        public bool PasswordFileExistedAtInjection { get; private set; }
        public Func<InjectionRequest, CancellationToken, InjectionResult>? InjectWithBootstrapHandler { get; init; }

        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
        {
            if (ShouldFailInjection)
            {
                return InjectionResult.CreateFailure(processId, InjectionError.Unknown, InjectionErrorMessage);
            }
            return InjectionResult.CreateSuccess(processId, dllPath);
        }

        public InjectionError ValidateTarget(int processId)
        {
            return ValidationResult;
        }

        public InjectionResult InjectWithBootstrap(
            InjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            InjectWithBootstrapCallCount++;
            LastInjectionRequest = request;
            LastInjectWithBootstrapCancellationToken = cancellationToken;
            if (!string.IsNullOrWhiteSpace(request.CertificateDirectory))
            {
                CertificateFileExistedAtInjection = File.Exists(Path.Combine(request.CertificateDirectory, "server.pfx"));
                PasswordFileExistedAtInjection = File.Exists(Path.Combine(request.CertificateDirectory, "server.pwd"));
            }

            if (InjectWithBootstrapHandler != null)
            {
                return InjectWithBootstrapHandler(request, cancellationToken);
            }

            if (ShouldFailInjection)
            {
                return InjectionResult.CreateFailure(
                    request.ProcessId,
                    FailedError,
                    InjectionErrorMessage,
                    failedAtStage: FailedStage,
                    bootstrapExitCode: FailedExitCode);
            }
            return InjectionResult.CreateSuccess(
                request.ProcessId,
                request.InspectorDllPath,
                bootstrapExitCode: 0,
                pipeName: request.ExpectedPipeName);
        }
    }

    private sealed class BootstrapStartsPipeInjector : FakeProcessInjector, IDisposable
    {
        public BootstrapStartsPipeInjector()
        {
            InjectWithBootstrapHandler = StartPipeServer;
        }

        private InspectorHost? _host;

        private InjectionResult StartPipeServer(InjectionRequest request, CancellationToken cancellationToken)
        {
            _host = new InspectorHost(request.ProcessId, request.ExpectedPipeName);
            using var plaintextPolicy = UnsafePlaintextInspectorHostTestEnvironment.BeginScope();
            _host.Start();

            return InjectionResult.CreateSuccess(
                request.ProcessId,
                request.InspectorDllPath,
                bootstrapExitCode: 0,
                pipeName: request.ExpectedPipeName);
        }

        public void Dispose()
        {
            _host?.Stop();
            _host?.Dispose();
        }
    }
}
