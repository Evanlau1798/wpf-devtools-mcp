using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TimingSensitive")]
public sealed class ConnectToolRawInjectionPolicyErrorCodeTests
{
    [Fact]
    public async Task Execute_WhenRawInjectionAllowlistIsMalformed_ShouldReturnPolicyErrorWithoutMetadata()
    {
        EnsureSharedDummyBootstrapperExists();
        var targetDirectory = Directory.CreateTempSubdirectory("wpf-devtools-malformed-raw-policy-");
        var executablePath = Path.Combine(targetDirectory.FullName, "TestApp.exe");
        File.WriteAllBytes(executablePath, []);

        try
        {
            using var allowlistScope = new EnvironmentVariableScope(
                McpServerConfiguration.RawInjectionAllowedTargetsEnvVar,
                @"relative\app.exe");
            var injector = new FakeProcessInjector();
            using var sessionManager = new SessionManager();
            var tool = new ConnectTool(sessionManager, injector,
                new FakeProcessDetector(executablePath),
                _ => { }, () => false, targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

            var result = await tool.ExecuteAsync(
                ToJsonElement(new { processId = NextSyntheticProcessId() }),
                CancellationToken.None);

            var resultText = JsonSerializer.Serialize(result);
            resultText.Should().NotContain("TestApp");
            resultText.Should().NotContain("wpf-devtools-malformed-raw-policy");
            var resultJson = JsonSerializer.Deserialize<JsonElement>(resultText);
            resultJson.GetProperty("errorCode").GetString().Should().Be("InvalidPolicyConfiguration", resultText);
            resultJson.GetProperty("allowlistEnvVar").GetString().Should().Be(McpServerConfiguration.RawInjectionAllowedTargetsEnvVar);
            injector.InjectWithBootstrapCallCount.Should().Be(0);
        }
        finally
        {
            targetDirectory.Delete(recursive: true);
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable(_name, _originalValue);
    }

    private sealed class FakeProcessDetector(string executablePath) : WpfProcessDetector
    {
        public override WpfProcessInfo? GetProcessInfo(int processId)
            => new()
            {
                ProcessId = processId,
                ProcessName = "TestApp",
                WindowTitle = "TestApp Window",
                Architecture = ProcessArchitecture.X64,
                Runtime = TargetRuntime.NetCore,
                IsWpfApplication = true,
                ExecutablePath = executablePath
            };
    }

    private sealed class FakeProcessInjector : IProcessInjector
    {
        public int InjectWithBootstrapCallCount { get; private set; }

        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
            => InjectionResult.CreateSuccess(processId, dllPath);

        public InjectionError ValidateTarget(int processId)
            => InjectionError.None;

        public InjectionResult InjectWithBootstrap(InjectionRequest request, CancellationToken cancellationToken = default)
        {
            InjectWithBootstrapCallCount++;
            return InjectionResult.CreateSuccess(request.ProcessId, request.InspectorDllPath);
        }
    }
}
