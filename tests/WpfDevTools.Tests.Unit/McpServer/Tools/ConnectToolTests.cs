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
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TimingSensitive")]
public partial class ConnectToolTests : IDisposable
{
    [Fact]
    public async Task Execute_WithInvalidProcessId_ShouldReturnError()
    {
        var tool = new ConnectTool(new SessionManager());
        var parameters = new { processId = 999999 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_WithMissingProcessId_ShouldReturnAutoDiscoveryError()
    {
        var tool = CreateTool(processDetector: new EmptyProcessDetector());
        var parameters = new { };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("NoWpfProcessesFound");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Execute_WithNonPositiveProcessId_ShouldReturnValidationError(int invalidProcessId)
    {
        var tool = new ConnectTool(new SessionManager());
        var parameters = new { processId = invalidProcessId };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("positive integer");
    }

    [Fact]
    public async Task Execute_WithNonWpfProcess_ShouldReturnError()
    {
        var tool = CreateTool(injector:
            new FakeProcessInjector { ValidationResult = InjectionError.NotWpfApplication });
        var parameters = new { processId = 12345 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("not a WPF application");
    }

    [Fact]
    public void ValidateDllPath_WithUnsignedDllInTrustedRoot_ShouldNotThrowInDebug()
    {
        var unsignedDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");
        var act = () => DllPathValidator.ValidateDllPath(unsignedDllPath);
        var signatureAction = DllPathValidator.GetCurrentBuildSignatureAction();

        if (signatureAction == SignaturePolicy.Action.Skip)
        {
            act.Should().NotThrow(
                "development builds should auto-skip signature verification for DLLs in trusted roots");
        }
        else
        {
            act.Should().Throw<System.Security.Cryptography.CryptographicException>()
                .WithMessage("*signature*");
        }
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\evil.dll")]
    [InlineData("\\\\network\\share\\evil.dll")]
    public void ValidateDllPath_WithMaliciousPath_ShouldThrow(string maliciousPath)
    {
        var act = () => DllPathValidator.ValidateDllPath(maliciousPath);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateDllPath_WithPathOutsideAppDirectory_ShouldThrow()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "evil.dll");

        var act = () => DllPathValidator.ValidateDllPath(outsidePath);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*application directory*");
    }

    [Fact]
    public void ValidateDllPath_WithTrustedSolutionRelativeDllPath_ShouldRespectBuildSignaturePolicy()
    {
        var solutionRoot = FindSolutionRoot();
        var artifactsDir = Path.Combine(solutionRoot, ".test-artifacts");
        Directory.CreateDirectory(artifactsDir);

        var trustedDllPath = Path.Combine(artifactsDir, "WpfDevTools.Inspector.dll");
        File.WriteAllText(trustedDllPath, string.Empty);

        try
        {
            var act = () => DllPathValidator.ValidateDllPath(trustedDllPath);
            var signatureAction = DllPathValidator.GetCurrentBuildSignatureAction();

            if (signatureAction == SignaturePolicy.Action.Skip)
            {
                act.Should().NotThrow();
            }
            else
            {
                act.Should().Throw<System.Security.Cryptography.CryptographicException>()
                    .WithMessage("*signature*");
            }
        }
        finally
        {
            if (File.Exists(trustedDllPath))
            {
                File.Delete(trustedDllPath);
            }

            if (Directory.Exists(artifactsDir) && !Directory.EnumerateFileSystemEntries(artifactsDir).Any())
            {
                Directory.Delete(artifactsDir);
            }
        }
    }

    [Fact]
    public async Task Execute_WithArchitectureMismatch_ShouldReturnError()
    {
        var tool = CreateTool(injector:
            new FakeProcessInjector { ValidationResult = InjectionError.ArchitectureMismatch });
        var parameters = new { processId = 12345 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Architecture mismatch");
    }

    [Fact]
    public async Task Execute_WithElevatedTargetAccessDenied_ShouldExplainAdministratorRequirement()
    {
        var tool = CreateTool(
            injector: new FakeProcessInjector { ValidationResult = InjectionError.AccessDenied },
            processDetector: new FakeProcessDetector(isElevated: true),
            isCurrentProcessElevated: () => false);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("elevated");
        resultJson.GetProperty("error").GetString().Should().Contain("administrator");
        resultJson.GetProperty("requiresElevationToConnect").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithElevatedTargetAndNonElevatedServer_ShouldReturnPreflightPermissionWarning()
    {
        var injector = new FakeProcessInjector();
        var tool = CreateTool(
            injector: injector,
            processDetector: new FakeProcessDetector(isElevated: true),
            isCurrentProcessElevated: () => false);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("AccessDenied");
        resultJson.GetProperty("targetIsElevated").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("requiresElevationToConnect").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("canConnectFromCurrentServer").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("administrator");
        injector.InjectWithBootstrapCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Execute_WhenMcpTargetPolicyRejectsProcess_ShouldReturnSecurityErrorWithoutInjection()
    {
        EnsureDummyBootstrapperExists();

        var injector = new FakeProcessInjector();
        var tool = CreateTool(
            injector: injector,
            processDetector: new FakeProcessDetector(executablePath: @"C:\Denied\Target.exe"),
            targetPolicy: _ => new McpTargetAuthorization(
                IsAllowed: false,
                Error: "Target blocked by test policy.",
                Hint: $"Set {McpServerConfiguration.AllowedTargetsEnvVar} to allow this target."));

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultText = JsonSerializer.Serialize(result);
        resultText.Should().NotContain("TestApp");
        var resultJson = JsonSerializer.Deserialize<JsonElement>(resultText);
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("SecurityError");
        resultJson.GetProperty("policyEnvVar").GetString().Should().Be(McpServerConfiguration.AllowedTargetsEnvVar);
        injector.InjectWithBootstrapCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Execute_WithElevatedTargetAndElevatedServer_ShouldProceedPastPreflight()
    {
        EnsureDummyBootstrapperExists();

        var injector = new FakeProcessInjector
        {
            ShouldFailInjection = true,
            InjectionErrorMessage = "Expected downstream injection failure",
            FailedError = InjectionError.BootstrapFailed
        };

        var tool = CreateTool(
            injector: injector,
            processDetector: new FakeProcessDetector(isElevated: true),
            isCurrentProcessElevated: () => true);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("BootstrapFailed");
        resultJson.GetProperty("error").GetString().Should().Contain("Bootstrap failed");
        injector.InjectWithBootstrapCallCount.Should().Be(1);
    }
}
