using FluentAssertions;
using System.Xml.Linq;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("ProcessEnvironment")]
public sealed class ComposerNonWindowRootHostingTests
{
    [Fact]
    public void ApplyToolDescription_ShouldExplainNonWindowRootHosting()
    {
        UiComposerMcpToolDescriptions.ApplyUiBlueprint.Should()
            .Contain("Existing Window XAML hosts a non-Window root");
    }

    [Fact]
    public void DryRun_NonWindowRootTargetingExistingWindow_ShouldPreserveWindowShell()
    {
        var root = CreateFixture();
        try
        {
            var result = new UiBlueprintApplyService(CreateRegistry(root)).Apply(
                new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            var document = XDocument.Parse(RemoveComposerHeader(result.Xaml));
            document.Root!.Name.LocalName.Should().Be("Window");
            document.Root.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))!
                .Value.Should().Be("NeutralApp.MainWindow");
            document.Root.Descendants().Should().Contain(element =>
                element.Name.LocalName == "PanelHost" && element.Name.NamespaceName == "urn:neutral");
            result.ProjectIntegrationPlan.Operations.Should().Contain(operation =>
                operation.Role == "application-xaml"
                && operation.Purposes.Contains("resources")
                && operation.Purposes.Contains("startup"));
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public void DryRun_NonWindowRootTargetingNewView_ShouldNotClaimStartupIntegration()
    {
        var root = CreateFixture();
        try
        {
            var result = new UiBlueprintApplyService(CreateRegistry(root)).Apply(
                new ApplyBlueprintRequest(Blueprint(), root, Path.Combine("Views", "PanelHost.xaml")));

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            var operation = result.ProjectIntegrationPlan.Operations.Single(item => item.Role == "application-xaml");
            operation.Purposes.Should().Equal("resources");
            operation.Description.Should().Be("Merge pack-declared application resources.");
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public void DryRun_PackDeclaredWindowRoot_ShouldNotBeNestedInExistingWindow()
    {
        var root = CreateFixture();
        try
        {
            var packRoot = Path.Combine(root, ".wpfdevtools", "packs", "neutral", "1.0.0");
            var packPath = Path.Combine(packRoot, "pack.json");
            File.WriteAllText(packPath, File.ReadAllText(packPath).Replace(
                "\"baseKind\":\"contentControl\"",
                "\"baseKind\":\"window\"",
                StringComparison.Ordinal));
            var blockPath = Path.Combine(packRoot, "blocks", "panel-host.block.json");
            File.WriteAllText(blockPath, File.ReadAllText(blockPath).Replace(
                "\"xamlTemplate\":\"renderers/xaml/panel-host.xaml.sbn\"",
                "\"xamlTemplate\":\"renderers/xaml/panel-host.xaml.sbn\",\"codeBehindBaseType\":\"Neutral.Controls.PanelHost\"",
                StringComparison.Ordinal));

            var result = new UiBlueprintApplyService(CreateRegistry(root)).Apply(
                new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            XDocument.Parse(RemoveComposerHeader(result.Xaml)).Root!.Name.LocalName.Should().Be("PanelHost");
            result.ProjectIntegrationPlan.Operations.Should().Contain(operation =>
                operation.Role == "application-xaml" && operation.Purposes.Contains("startup"));
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public void Apply_NonWindowRootRepeatedly_ShouldKeepSingleComposerEnvelope()
    {
        var root = CreateFixture();
        try
        {
            using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
            using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, root);
            var service = new UiBlueprintApplyService(CreateRegistry(root));
            var request = new ApplyBlueprintRequest(
                Blueprint(),
                root,
                "MainWindow.xaml",
                DryRun: false,
                ConfirmApply: true);

            service.Apply(request).Success.Should().BeTrue();
            var targetPath = Path.Combine(root, "MainWindow.xaml");
            File.WriteAllText(targetPath, File.ReadAllText(targetPath).Replace(
                "<!-- WPFDEVTOOLS_SAFE_SLOT_END: manual-content -->",
                "<!-- Manual note -->\n<!-- WPFDEVTOOLS_SAFE_SLOT_END: manual-content -->",
                StringComparison.Ordinal));

            service.Apply(request).Success.Should().BeTrue();

            var written = File.ReadAllText(targetPath);
            CountOccurrences(written, "WPFDEVTOOLS_BLUEPRINT_SOURCE").Should().Be(1);
            CountOccurrences(written, "WPFDEVTOOLS_SAFE_SLOT_BEGIN: manual-content").Should().Be(1);
            CountOccurrences(written, "WPFDEVTOOLS_SAFE_SLOT_END: manual-content").Should().Be(1);
            written.Should().Contain("Manual note");
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    private static PackRegistry CreateRegistry(string root)
        => new(
            ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
            ComposerPackPaths.ProjectLocalRoot(root));

    private static string CreateFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "wpfdevtools-non-window-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "NeutralApp.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><UseWPF>true</UseWPF><RootNamespace>NeutralApp</RootNamespace></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "App.xaml"),
            "<Application x:Class=\"NeutralApp.App\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" StartupUri=\"MainWindow.xaml\"><Application.Resources /></Application>");
        File.WriteAllText(Path.Combine(root, "MainWindow.xaml"),
            "<Window x:Class=\"NeutralApp.MainWindow\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Title=\"Host\"><Grid /></Window>");
        File.WriteAllText(Path.Combine(root, "MainWindow.xaml.cs"),
            "namespace NeutralApp; public partial class MainWindow : System.Windows.Window { public MainWindow() => InitializeComponent(); }");
        CreatePack(root);
        return root;
    }

    private static void CreatePack(string root)
    {
        var pack = Path.Combine(root, ".wpfdevtools", "packs", "neutral", "1.0.0");
        Directory.CreateDirectory(Path.Combine(pack, "blocks"));
        Directory.CreateDirectory(Path.Combine(pack, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(pack, "pack.json"),
            """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"neutral","kind":"control-pack","displayName":"Neutral","version":"1.0.0","xmlNamespaces":{"neutral":"urn:neutral"},"resourceSetup":{"applicationMergedDictionaries":["<neutral:Theme />"]},"preview":{"namespaceUri":"urn:neutral","clrNamespace":"Neutral.Controls","types":{"PanelHost":{"baseKind":"contentControl"}}},"blocks":["neutral.panelHost"],"recipes":[]}
            """);
        File.WriteAllText(Path.Combine(pack, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}""");
        File.WriteAllText(Path.Combine(pack, "install.manifest.json"),
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"neutral","version":"1.0.0","scope":"project-local","path":".","enabled":true}""");
        File.WriteAllText(Path.Combine(pack, "blocks", "panel-host.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"neutral.panelHost","displayName":"Panel host","description":"Third-party content host.","category":"layout","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/panel-host.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(pack, "renderers", "xaml", "panel-host.xaml.sbn"),
            "<neutral:PanelHost><TextBlock Text=\"Generated\" /></neutral:PanelHost>");
    }

    private static string Blueprint()
        => """
           {"schemaVersion":"wpfdevtools.ui-blueprint.v1","name":"GeneratedPanel","packs":[{"id":"neutral","version":"1.0.0","required":true,"role":"primary"}],"primaryPack":"neutral","layout":{"kind":"neutral.panelHost"}}
           """;

    private static string RemoveComposerHeader(string xaml)
        => xaml[(xaml.IndexOf(" -->", StringComparison.Ordinal) + 4)..].TrimStart();

    private static int CountOccurrences(string value, string marker)
        => value.Split(marker, StringSplitOptions.None).Length - 1;

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
}
