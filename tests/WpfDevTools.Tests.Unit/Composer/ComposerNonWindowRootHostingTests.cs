using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
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
    public void DryRun_NonWindowRootWithReviewedTargetWindowSize_ShouldAlignHostAndReportPlan()
    {
        var root = CreateFixture();
        try
        {
            var result = new UiBlueprintApplyService(CreateRegistry(root)).Apply(
                new ApplyBlueprintRequest(
                    Blueprint(),
                    root,
                    "MainWindow.xaml",
                    TargetWindowWidth: 1600,
                    TargetWindowHeight: 900));

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            var window = XDocument.Parse(RemoveComposerHeader(result.Xaml)).Root!;
            window.Attribute("Width")!.Value.Should().Be("1600");
            window.Attribute("Height")!.Value.Should().Be("900");
            result.TargetWindowPlan.Status.Should().Be("configured-existing-window");
            result.TargetWindowPlan.TargetWindowWidth.Should().Be(1600);
            result.TargetWindowPlan.TargetWindowHeight.Should().Be(900);
            result.TargetWindowPlan.Guidance.Should().Contain("preview_ui_blueprint");
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public void DryRun_NonWindowRootWithoutTargetWindowSize_ShouldPreserveExistingDimensions()
    {
        var root = CreateFixture();
        try
        {
            var targetPath = Path.Combine(root, "MainWindow.xaml");
            File.WriteAllText(targetPath, File.ReadAllText(targetPath).Replace(
                "Title=\"Host\"",
                "Title=\"Host\" Width=\"800\" Height=\"450\"",
                StringComparison.Ordinal));

            var result = new UiBlueprintApplyService(CreateRegistry(root)).Apply(
                new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            var window = XDocument.Parse(RemoveComposerHeader(result.Xaml)).Root!;
            window.Attribute("Width")!.Value.Should().Be("800");
            window.Attribute("Height")!.Value.Should().Be("450");
            result.TargetWindowPlan.Status.Should().Be("preserved-existing-window");
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
                new ApplyBlueprintRequest(
                    Blueprint(),
                    root,
                    "MainWindow.xaml",
                    TargetWindowWidth: 1200,
                    TargetWindowHeight: 700));

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            var window = XDocument.Parse(RemoveComposerHeader(result.Xaml)).Root!;
            window.Name.LocalName.Should().Be("PanelHost");
            window.Attribute("Width")!.Value.Should().Be("1200");
            window.Attribute("Height")!.Value.Should().Be("700");
            result.TargetWindowPlan.Status.Should().Be("configured-rendered-window");
            result.ProjectIntegrationPlan.Operations.Should().Contain(operation =>
                operation.Role == "application-xaml" && operation.Purposes.Contains("startup"));
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8193)]
    public void DryRun_InvalidTargetWindowDimension_ShouldFail(int width)
    {
        var root = CreateFixture();
        try
        {
            var result = new UiBlueprintApplyService(CreateRegistry(root)).Apply(
                new ApplyBlueprintRequest(
                    Blueprint(),
                    root,
                    "MainWindow.xaml",
                    TargetWindowWidth: width));

            result.Success.Should().BeFalse();
            result.Errors.Should().ContainSingle(error =>
                error.JsonPath == "$.targetWindowWidth"
                && error.Code == "InvalidTargetWindowDimension");
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [StaFact]
    public void Apply_NonWindowRootRepeatedly_ShouldKeepSingleComposerEnvelope()
    {
        var root = CreateFixture();
        try
        {
            File.WriteAllText(
                Path.Combine(root, ".wpfdevtools", "packs", "neutral", "1.0.0", "renderers", "xaml", "panel-host.xaml.sbn"),
                "<Button Content=\"Generated\" />");
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
            var existing = XDocument.Load(targetPath, LoadOptions.PreserveWhitespace);
            existing.Root!.SetAttributeValue(
                XNamespace.Xmlns + "sys",
                "clr-namespace:System;assembly=System.Runtime");
            File.WriteAllText(targetPath, existing.ToString(SaveOptions.DisableFormatting).Replace(
                "<!-- WPFDEVTOOLS_SAFE_SLOT_END: manual-content -->",
                "<TextBlock Text=\"Manual note\" Tag=\"{x:Static sys:String.Empty}\" />\n<!-- WPFDEVTOOLS_SAFE_SLOT_END: manual-content -->",
                StringComparison.Ordinal));

            service.Apply(request).Success.Should().BeTrue();

            var written = File.ReadAllText(targetPath);
            CountOccurrences(written, "WPFDEVTOOLS_BLUEPRINT_SOURCE").Should().Be(1);
            CountOccurrences(written, "WPFDEVTOOLS_SAFE_SLOT_BEGIN: manual-content").Should().Be(1);
            CountOccurrences(written, "WPFDEVTOOLS_SAFE_SLOT_END: manual-content").Should().Be(1);
            written.Should().Contain("Manual note");
            var document = XDocument.Parse(written);
            document.Descendants().Should().Contain(element =>
                element.Name.LocalName == "TextBlock"
                && element.Name.NamespaceName == "http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                && element.Attribute("Text") != null
                && element.Attribute("Text")!.Value == "Manual note");
            document.Descendants().Single(element => element.Attribute("Text")?.Value == "Manual note")
                .GetNamespaceOfPrefix("sys")!.NamespaceName.Should().Be("clr-namespace:System;assembly=System.Runtime");
            document.Root!.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))!.Remove();
            var window = XamlReader.Parse(document.Root.ToString()).Should().BeOfType<Window>().Subject;
            try
            {
                window.Content.Should().BeOfType<Grid>();
            }
            finally
            {
                window.Close();
            }

            File.WriteAllText(targetPath, written.Replace(
                "<!-- WPFDEVTOOLS_SAFE_SLOT_END: manual-content -->",
                string.Empty,
                StringComparison.Ordinal));
            var malformedContent = File.ReadAllText(targetPath);
            var malformed = service.Apply(request);
            malformed.Success.Should().BeFalse();
            malformed.Errors.Should().Contain(error => error.Code == "MalformedComposerSafeSlot");
            File.ReadAllText(targetPath).Should().Be(malformedContent);
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public void Apply_DuplicateSafeSlots_ShouldFailBeforeReplacingTarget()
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
            var duplicated = File.ReadAllText(targetPath).Replace(
                "<!-- WPFDEVTOOLS_SAFE_SLOT_END: manual-content -->",
                "<!-- WPFDEVTOOLS_SAFE_SLOT_END: manual-content -->\n<!-- WPFDEVTOOLS_SAFE_SLOT_BEGIN: manual-content -->\n<TextBlock Text=\"Second slot\" />\n<!-- WPFDEVTOOLS_SAFE_SLOT_END: manual-content -->",
                StringComparison.Ordinal);
            File.WriteAllText(targetPath, duplicated);

            var result = service.Apply(request);

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == "MalformedComposerSafeSlot");
            File.ReadAllText(targetPath).Should().Be(duplicated);
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
