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
    }

    [Fact]
    public void ExpectedPipeName_ShouldFollowConvention()
    {
        var pipeName = InjectionRequest.CreatePipeName(5678);

        pipeName.Should().Be("WpfDevTools_5678");
    }

    [Fact]
    public void ToBootstrapParameters_WithSecuritySettings_ShouldIncludeSecureFlagsAndValues()
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

        var parameters = request.ToBootstrapParameters();

        parameters.Should().Contain("inspectorDllPath=C:\\app\\net8.0-windows\\Inspector.dll");
        parameters.Should().Contain("pipeName=WpfDevTools_1234");
        parameters.Should().Contain("auth=enabled");
        parameters.Should().Contain("authSecretBase64=YWJjZA==",
            "base64 padding must survive parameter formatting");
        parameters.Should().Contain("encryption=enabled");
        parameters.Should().Contain("certDirectory=C:\\secure certs");
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
