using FluentAssertions;
using WpfDevTools.Injector.Injection;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public class InjectionRequestTests
{
    [Fact]
    public void Create_ShouldSetRequiredFields()
    {
        var request = new InjectionRequest
        {
            ProcessId = 1234,
            BootstrapperDllPath = @"C:\app\Bootstrapper.x64.dll",
            InspectorDllPath = @"C:\app\net8.0-windows\Inspector.dll",
            ExpectedPipeName = "WpfDevTools_1234"
        };

        request.ProcessId.Should().Be(1234);
        request.BootstrapperDllPath.Should().Be(@"C:\app\Bootstrapper.x64.dll");
        request.InspectorDllPath.Should().Be(@"C:\app\net8.0-windows\Inspector.dll");
        request.ExpectedPipeName.Should().Be("WpfDevTools_1234");
    }

    [Fact]
    public void Create_ShouldHaveDefaultTimeouts()
    {
        var request = new InjectionRequest
        {
            ProcessId = 1234,
            BootstrapperDllPath = "a.dll",
            InspectorDllPath = "b.dll",
            ExpectedPipeName = "WpfDevTools_1234"
        };

        request.InjectionTimeout.Should().BeGreaterThan(TimeSpan.Zero,
            "injection timeout must have a sensible default");
        request.PipeReadyTimeout.Should().BeGreaterThan(TimeSpan.Zero,
            "pipe ready timeout must have a sensible default");
        request.TotalTimeout.Should().BeNull(
            "callers that do not share a wider operation budget should keep the legacy per-phase defaults");
    }

    [Fact]
    public void ResolvePhaseTimeout_WhenTotalTimeoutIsConfigured_ShouldClampToRemainingBudget()
    {
        var request = new InjectionRequest
        {
            ProcessId = 1234,
            BootstrapperDllPath = "a.dll",
            InspectorDllPath = "b.dll",
            ExpectedPipeName = "WpfDevTools_1234",
            TotalTimeout = TimeSpan.FromSeconds(5)
        };

        var phaseTimeout = request.ResolvePhaseTimeout(
            elapsed: TimeSpan.FromSeconds(3),
            configuredTimeout: TimeSpan.FromSeconds(15));

        phaseTimeout.Should().Be(TimeSpan.FromSeconds(2),
            "later injection phases must consume the remaining shared timeout budget instead of restarting a fresh per-phase timeout");
    }

    [Fact]
    public void ResolvePhaseTimeout_WhenElapsedConsumesTotalTimeout_ShouldReturnZero()
    {
        var request = new InjectionRequest
        {
            ProcessId = 1234,
            BootstrapperDllPath = "a.dll",
            InspectorDllPath = "b.dll",
            ExpectedPipeName = "WpfDevTools_1234",
            TotalTimeout = TimeSpan.FromSeconds(5)
        };

        var phaseTimeout = request.ResolvePhaseTimeout(
            elapsed: TimeSpan.FromSeconds(5),
            configuredTimeout: TimeSpan.FromSeconds(15));

        phaseTimeout.Should().Be(TimeSpan.Zero,
            "once the shared connect budget is exhausted, later injector phases must fail fast instead of starting another wait window");
    }

    [Fact]
    public void ResolvePhaseTimeout_WhenConfiguredTimeoutIsZeroWithoutTotalBudget_ShouldReturnZeroWithoutAssumingSharedBudget()
    {
        var request = new InjectionRequest
        {
            ProcessId = 1234,
            BootstrapperDllPath = "a.dll",
            InspectorDllPath = "b.dll",
            ExpectedPipeName = "WpfDevTools_1234"
        };

        var phaseTimeout = request.ResolvePhaseTimeout(
            elapsed: TimeSpan.Zero,
            configuredTimeout: TimeSpan.Zero);

        phaseTimeout.Should().Be(TimeSpan.Zero,
            "zero per-phase timeouts can come from direct callers even when no shared total budget is configured");
        request.TotalTimeout.Should().BeNull();
    }

    [Fact]
    public void ExpectedPipeName_ShouldFollowConvention()
    {
        var pipeName = InjectionRequest.CreatePipeName(5678);

        pipeName.Should().Be("WpfDevTools_5678");
    }

    [Fact]
    public void CreateBootstrapParameterPayload_WithSecuritySettings_ShouldUseSecretFile()
    {
        var request = new InjectionRequest
        {
            ProcessId = 1234,
            BootstrapperDllPath = @"C:\app\Bootstrapper.x64.dll",
            InspectorDllPath = @"C:\app\net8.0-windows\Inspector.dll",
            ExpectedPipeName = "WpfDevTools_1234",
            AuthenticationSecretBase64 = "YWJjZA==",
            CertificateDirectory = @"C:\secure certs"
        };

        using var payload = request.CreateBootstrapParameterPayload();
        var parameters = payload.Parameters;

        parameters.Should().Contain("inspectorDllPath=C:\\app\\net8.0-windows\\Inspector.dll");
        parameters.Should().Contain("pipeName=WpfDevTools_1234");
        parameters.Should().Contain("auth=enabled");
        parameters.Should().Contain("authSecretFile=");
        parameters.Should().NotContain("authSecretBase64=");
        payload.AuthenticationSecretFilePath.Should().NotBeNull();
        File.Exists(payload.AuthenticationSecretFilePath!).Should().BeTrue();
        File.ReadAllText(payload.AuthenticationSecretFilePath!).Should().Be("YWJjZA==");
        parameters.Should().Contain("encryption=enabled");
        parameters.Should().Contain("certDirectory=C:\\secure certs");
    }

    [Fact]
    public void CreateBootstrapParameterPayload_Dispose_ShouldDeleteSecretFile()
    {
        var request = new InjectionRequest
        {
            ProcessId = 1234,
            BootstrapperDllPath = @"C:\app\Bootstrapper.x64.dll",
            InspectorDllPath = @"C:\app\net8.0-windows\Inspector.dll",
            ExpectedPipeName = "WpfDevTools_1234",
            AuthenticationSecretBase64 = "YWJjZA=="
        };

        var payload = request.CreateBootstrapParameterPayload();
        var secretFilePath = payload.AuthenticationSecretFilePath;

        payload.Dispose();

        File.Exists(secretFilePath!).Should().BeFalse();
    }

    [Fact]
    public void ToBootstrapParameters_WithSemicolonInValue_ShouldThrow()
    {
        var request = new InjectionRequest
        {
            ProcessId = 1234,
            BootstrapperDllPath = @"C:\app\Bootstrapper.x64.dll",
            InspectorDllPath = @"C:\app;bad\Inspector.dll",
            ExpectedPipeName = "WpfDevTools_1234"
        };

        var act = () => request.ToBootstrapParameters();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*semicolon*");
    }
}
