using System.Security.Cryptography;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Shared.Security;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("ProcessEnvironment")]
public sealed class ComposerThirdPartyPackAcceptanceTests
{
    [Fact]
    public async Task NonWpfUiPack_ShouldCompleteResolutionPreviewApplyAndMcpInspection()
    {
        var projectRoot = ThirdPartyPackFixture.Create();
        try
        {
            var registry = CreateRegistry(projectRoot);
            var blueprint = ThirdPartyPackFixture.Blueprint;
            var validation = new BlueprintValidationService(registry).Validate(blueprint);

            validation.Success.Should().BeTrue(string.Join(Environment.NewLine,
                validation.Errors.Select(error => error.Message)));
            validation.Resolution.Packs.Should().Contain(pack =>
                pack.Id == "nebula" && pack.Kind == "style-pack" && pack.Status == "resolved");
            validation.Resolution.Packs.Should().Contain(pack =>
                pack.Id == "core" && pack.Kind == "layout-pack" && pack.Status == "resolved");

            var render = new UiBlueprintRenderer(registry).Render(
                new RenderBlueprintRequest(blueprint, ProjectRoot: projectRoot));
            render.Success.Should().BeTrue(string.Join(Environment.NewLine,
                render.Errors.Select(error => error.Message)));
            render.Xaml.Should().Contain("<nebula:NebulaWindow").And.Contain("<Button");
            render.PackageIntegrationGuidance.Mode.Should().Be("project");

            var apply = new UiBlueprintApplyService(registry).Apply(
                new ApplyBlueprintRequest(blueprint, projectRoot, "MainWindow.xaml"));
            apply.Success.Should().BeTrue(string.Join(Environment.NewLine,
                apply.Errors.Select(error => error.Message)));
            apply.FilePlan.Should().Contain(item =>
                item.Role == "code-behind-integration"
                && item.Action.Contains("Nebula.Controls.NebulaWindow", StringComparison.Ordinal));
            var interaction = apply.BehaviorIntegrationContract.Interactions.Should().ContainSingle().Subject;
            interaction.CommandPath.Should().Be("RunCommand");
            interaction.CommandParameter.Should().Be("acceptance-42");
            interaction.Label.Should().Be("Run acceptance");

            using var sensitiveReads = new EnvironmentVariableScope(
                McpServerConfiguration.AllowSensitiveReadsEnvVar,
                "true");
            using var session = SecurePreviewSession.Create();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            var preview = await new UiBlueprintPreviewService(registry, session.SessionManager).PreviewAsync(
                new PreviewBlueprintRequest(
                    blueprint,
                    RestoreEnabled: true,
                    StartHost: true,
                    IncludeRuntimeDiagnostics: true),
                timeout.Token);

            preview.BuildSucceeded.Should().BeTrue(preview.BuildOutput + Environment.NewLine
                + string.Join(Environment.NewLine, preview.Diagnostics.Select(diagnostic => diagnostic.Message)));
            preview.PreviewHost.Status.Should().Be("loaded", preview.BuildOutput);
            preview.ElementCorrelations.Should().HaveCount(4);
            preview.PreviewHost.RuntimeDiagnostics.Should().Contain(diagnostic =>
                diagnostic.Tool == "connect" && diagnostic.Success);
            preview.PreviewHost.RuntimeDiagnostics.Should().Contain(diagnostic =>
                diagnostic.Tool == "get_ui_summary" && diagnostic.Success);
            preview.PreviewHost.RuntimeDiagnostics.Should().Contain(diagnostic =>
                diagnostic.Tool == "find_elements" && diagnostic.Success);
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    private static PackRegistry CreateRegistry(string projectRoot)
        => new(
            ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
            ComposerPackPaths.ProjectLocalRoot(projectRoot));

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

    private sealed class SecurePreviewSession : IDisposable
    {
        private readonly AuthenticationManager _authenticationManager;
        private readonly string _certificateDirectory;

        private SecurePreviewSession(
            AuthenticationManager authenticationManager,
            CertificateManager certificateManager,
            string certificateDirectory)
        {
            _authenticationManager = authenticationManager;
            _certificateDirectory = certificateDirectory;
            SessionManager = new SessionManager(authManager: authenticationManager, certManager: certificateManager);
        }

        public SessionManager SessionManager { get; }

        public static SecurePreviewSession Create()
        {
            var secretBytes = RandomNumberGenerator.GetBytes(32);
            var secret = Convert.ToBase64String(secretBytes);
            CryptographicOperations.ZeroMemory(secretBytes);
            var authManager = new AuthenticationManager(() => secret);
            var certificateDirectory = Path.Combine(
                TestRepositoryPaths.GetRepoFilePath("tmp"),
                "third-party-preview-certs-" + Guid.NewGuid().ToString("N"));
            return new SecurePreviewSession(
                authManager,
                new CertificateManager(certificateDirectory),
                certificateDirectory);
        }

        public void Dispose()
        {
            SessionManager.Dispose();
            _authenticationManager.Dispose();
            TestDirectory.Delete(_certificateDirectory);
        }
    }
}

internal static class ThirdPartyPackFixture
{
    public const string Blueprint = """
        {
          "schemaVersion":"wpfdevtools.ui-blueprint.v1",
          "name":"NebulaAcceptance",
          "packs":[
            {"id":"core","version":"0.1.0","required":true,"role":"layout-pack"},
            {"id":"nebula","version":"1.0.0","required":true,"role":"primary"}
          ],
          "primaryPack":"nebula",
          "layout":{
            "kind":"nebula.window",
            "properties":{"title":"Third-party acceptance"},
            "slots":{"content":[{
              "kind":"core.stack",
              "slots":{"children":[
                {"kind":"core.text","properties":{"text":"Pack-neutral runtime"}},
                {"kind":"nebula.action","properties":{"execute":"{Binding RunCommand}","payload":"acceptance-42","caption":"Run acceptance"}}
              ]}
            }]}
          }
        }
        """;

    public static string Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "wpfdevtools-third-party-" + Guid.NewGuid().ToString("N"));
        var pack = Path.Combine(root, ".wpfdevtools", "packs", "nebula", "1.0.0");
        Directory.CreateDirectory(Path.Combine(pack, "blocks"));
        Directory.CreateDirectory(Path.Combine(pack, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(root, "ThirdPartyAcceptance.csproj"),
            """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><RootNamespace>ThirdPartyAcceptance</RootNamespace></PropertyGroup></Project>""");
        File.WriteAllText(Path.Combine(pack, "pack.json"), PackJson);
        File.WriteAllText(Path.Combine(pack, "install.manifest.json"),
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"nebula","version":"1.0.0","scope":"project-local","path":".","enabled":true}""");
        File.WriteAllText(Path.Combine(pack, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[{"name":"Nebula Controls","url":"https://example.invalid/nebula","version":"1.0.0","paths":["src"]}],"transformPolicy":{}}""");
        File.WriteAllText(Path.Combine(pack, "blocks", "window.block.json"), WindowBlockJson);
        File.WriteAllText(Path.Combine(pack, "blocks", "action.block.json"), ActionBlockJson);
        File.WriteAllText(Path.Combine(pack, "renderers", "xaml", "window.xaml.sbn"),
            "<nebula:NebulaWindow Title=\"{{title}}\" Width=\"720\" Height=\"480\">{{slot.content}}</nebula:NebulaWindow>");
        File.WriteAllText(Path.Combine(pack, "renderers", "xaml", "action.xaml.sbn"),
            "<Button Content=\"{{caption}}\" Command=\"{{execute}}\" CommandParameter=\"{{payload}}\" />");
        return root;
    }

    private const string PackJson = """
        {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"nebula","kind":"style-pack","displayName":"Nebula Controls","version":"1.0.0","blocks":["nebula.window","nebula.action"],"recipes":[],"xmlNamespaces":{"nebula":"clr-namespace:Nebula.Controls"},"preview":{"namespaceUri":"clr-namespace:Nebula.Controls","clrNamespace":"Nebula.Controls","types":{"NebulaWindow":{"baseKind":"window","contentProperty":"Content","properties":{"Content":"object"}}}}}
        """;

    private const string WindowBlockJson = """
        {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"nebula.window","displayName":"Nebula Window","category":"window","properties":{"title":{"type":"string","required":true,"default":"Nebula"}},"slots":{"content":{"allowedKinds":["*"]}},"renderer":{"xamlTemplate":"renderers/xaml/window.xaml.sbn","codeBehindBaseType":"Nebula.Controls.NebulaWindow"},"sourceHints":[]}
        """;

    private const string ActionBlockJson = """
        {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"nebula.action","displayName":"Nebula Action","category":"interaction","properties":{"execute":{"type":"binding","required":true},"payload":{"type":"string"},"caption":{"type":"string","required":true}},"slots":{},"interaction":{"kind":"action","commandProperty":"execute","commandParameterProperty":"payload","labelProperty":"caption"},"renderer":{"xamlTemplate":"renderers/xaml/action.xaml.sbn"},"sourceHints":[]}
        """;
}
