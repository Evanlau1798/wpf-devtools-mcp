using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Utilities;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Security;

public sealed class TransportConstructorSafetyContractTests
{
    [Fact]
    public void PlaintextCompatibilityConstructors_ShouldBeHiddenFromIntelliSense()
    {
        AssertEditorBrowsableNever(typeof(NamedPipeClient).GetConstructor([typeof(int)]));
        AssertEditorBrowsableNever(typeof(InspectorHost).GetConstructor([typeof(int)]));
        AssertEditorBrowsableNever(typeof(InspectorHost).GetConstructor([typeof(int), typeof(FileLogLevel)]));
    }

    [Fact]
    public void PlaintextCompatibilityConstructors_ShouldBeDocumentedAsUnsafeCompatibilityOnly()
    {
        var namedPipeClient = Read("src/WpfDevTools.Mcp.Server/NamedPipeClient.cs");
        var inspectorHost = Read("src/WpfDevTools.Inspector/Host/InspectorHost.cs");

        namedPipeClient.Should().Contain("Compatibility-only plaintext constructor");
        namedPipeClient.Should().Contain("Production SessionManager paths must use authentication and TLS");

        inspectorHost.Should().Contain("Compatibility-only plaintext constructor");
        inspectorHost.Should().Contain("Starting this host requires explicit unsafe plaintext opt-in");
    }

    [Fact]
    public void ProductionSessionManagerPaths_ShouldCreateAuthAndTlsCapablePipeClients()
    {
        var sessionManagerConnection = Read("src/WpfDevTools.Mcp.Server/SessionManager.Connection.cs");

        sessionManagerConnection.Should().Contain("CreateProcessScopedPipeClient");
        sessionManagerConnection.Should().Contain("processAuthManager");
        sessionManagerConnection.Should().Contain("_certManager");
        sessionManagerConnection.Should().Contain("ownsAuthManager: processAuthManager != null");
        sessionManagerConnection.Should().Contain("enforceHostCompatibilityValidation: true");
        sessionManagerConnection.Should().NotContain("new NamedPipeClient(processId)");
    }

    [Fact]
    public void InjectionAndSdkHostedProductionPaths_ShouldPassAuthAndCertManagersToInspectorHost()
    {
        var bootstrap = Read("src/WpfDevTools.Inspector/Bootstrap.cs");
        var inspectorSdk = Read("src/WpfDevTools.Inspector.Sdk/InspectorSdk.cs");

        bootstrap.Should().Contain("HostFactory(processId, pipeName, authManager, certManager)");
        inspectorSdk.Should().Contain("new InspectorHost(pid, authenticationManager, certificateManager)");
    }

    private static void AssertEditorBrowsableNever(ConstructorInfo? constructor)
    {
        constructor.Should().NotBeNull();
        var attribute = constructor!.GetCustomAttribute<EditorBrowsableAttribute>();
        attribute.Should().NotBeNull();
        attribute!.State.Should().Be(EditorBrowsableState.Never);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));
}
