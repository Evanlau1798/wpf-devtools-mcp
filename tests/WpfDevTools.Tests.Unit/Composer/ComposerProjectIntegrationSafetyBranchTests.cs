using System.Xml.Linq;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("ProcessEnvironment")]
public sealed class ComposerProjectIntegrationSafetyBranchTests
{
    [Fact]
    public void DryRun_WithCentralPackageManagement_ShouldPlanVersionlessReferenceAndCentralVersion()
    {
        var root = CreateFixture(centralPackageManagement: true);
        try
        {
            var result = new UiBlueprintApplyService(CreateRegistry(root)).Apply(
                new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));

            result.ProjectIntegrationPlan.Ready.Should().BeTrue();
            var projectOperation = result.ProjectIntegrationPlan.Operations
                .Single(operation => operation.Role == "package-reference");
            projectOperation.ProposedContent.Should()
                .Contain("PackageReference Include=\"Nebula.Controls\"")
                .And.NotContain("PackageReference Include=\"Nebula.Controls\" Version=");

            var centralOperation = result.ProjectIntegrationPlan.Operations
                .Single(operation => operation.Role == "central-package-version");
            centralOperation.Action.Should().Be("create");
            centralOperation.Precondition.Exists.Should().BeFalse();
            centralOperation.TargetPath.Should().Be(Path.Combine(root, "Directory.Packages.props"));
            centralOperation.ProposedContent.Should()
                .Contain("PackageVersion Include=\"Nebula.Controls\" Version=\"1.2.3\"");
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public void DryRun_WithInheritedCentralPackageOutsideRoot_ShouldSuggestProjectLocalOptOut()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "wpfdevtools-inherited-central-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(parent, "scratch-project");
        Directory.CreateDirectory(parent);
        CreateFixture(projectRoot, centralPackageManagement: false);
        File.WriteAllText(Path.Combine(parent, "Directory.Packages.props"),
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            var result = new UiBlueprintApplyService(CreateRegistry(projectRoot)).Apply(
                new ApplyBlueprintRequest(Blueprint(), projectRoot, "MainWindow.xaml"));

            result.ProjectIntegrationPlan.Ready.Should().BeFalse();
            var issue = result.ProjectIntegrationPlan.Errors
                .Should().ContainSingle(error => error.Code == "IntegrationPathOutsideRoot")
                .Which;
            issue.Message.Should().Contain("central package file");
            var repair = issue.RepairSuggestion!;
            repair.Should()
                .Contain("Directory.Packages.props")
                .And.Contain("<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>")
                .And.Contain("rerun");
            var xmlStart = repair.IndexOf("<Project>", StringComparison.Ordinal);
            var xmlEnd = repair.IndexOf("</Project>", StringComparison.Ordinal) + "</Project>".Length;
            var document = XDocument.Parse(repair[xmlStart..xmlEnd]);
            document.Root!.Name.LocalName.Should().Be("Project");
        }
        finally
        {
            TestDirectory.Delete(parent);
        }
    }

    [Fact]
    public void Apply_WhenLaterOperationFails_ShouldDeleteNewCentralPackageFileDuringRollback()
    {
        var root = CreateFixture(centralPackageManagement: true);
        var appPath = Path.Combine(root, "App.xaml");
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
        using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, root);
        try
        {
            var registry = CreateRegistry(root);
            var projectPath = Path.Combine(root, "NebulaApp.csproj");
            var originalProject = File.ReadAllText(projectPath);
            var dryRun = new UiBlueprintApplyService(registry).Apply(
                new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));
            File.SetAttributes(appPath, FileAttributes.ReadOnly);

            var result = new UiBlueprintProjectIntegrationService(registry).Apply(
                new ProjectIntegrationRequest(
                    Blueprint(),
                    root,
                    "MainWindow.xaml",
                    dryRun.ProjectIntegrationPlan.PlanHash,
                    ConfirmIntegration: true));

            result.Success.Should().BeFalse();
            result.RolledBack.Should().BeTrue(string.Join(" | ", result.Errors.Select(error => error.Message)));
            File.Exists(Path.Combine(root, "Directory.Packages.props")).Should().BeFalse();
            File.ReadAllText(projectPath).Should().Be(originalProject);
        }
        finally
        {
            if (File.Exists(appPath))
            {
                File.SetAttributes(appPath, FileAttributes.Normal);
            }

            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public void Apply_WithReparsePointProjectRoot_ShouldRejectEveryIntegrationOperation()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "wpfdevtools-project-integration-reparse-" + Guid.NewGuid().ToString("N"));
        var actualRoot = Path.Combine(tempRoot, "actual-project");
        var linkedRoot = Path.Combine(tempRoot, "linked-project");
        Directory.CreateDirectory(tempRoot);
        CreateFixture(actualRoot, centralPackageManagement: false);
        CreateDirectoryJunction(linkedRoot, actualRoot);
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
        using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, linkedRoot);
        try
        {
            var registry = CreateRegistry(linkedRoot);
            var dryRun = new UiBlueprintApplyService(registry).Apply(
                new ApplyBlueprintRequest(Blueprint(), linkedRoot, "MainWindow.xaml"));

            var result = new UiBlueprintProjectIntegrationService(registry).Apply(
                new ProjectIntegrationRequest(
                    Blueprint(),
                    linkedRoot,
                    "MainWindow.xaml",
                    dryRun.ProjectIntegrationPlan.PlanHash,
                    ConfirmIntegration: true));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == "ProjectPathUsesReparsePoint");
            File.ReadAllText(Path.Combine(actualRoot, "NebulaApp.csproj"))
                .Should().NotContain("PackageReference");
        }
        finally
        {
            if (Directory.Exists(linkedRoot))
            {
                Directory.Delete(linkedRoot);
            }

            TestDirectory.Delete(tempRoot);
        }
    }

    private static PackRegistry CreateRegistry(string root)
        => new(
            ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
            ComposerPackPaths.ProjectLocalRoot(root));

    private static string CreateFixture(bool centralPackageManagement)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "wpfdevtools-project-integration-branches-" + Guid.NewGuid().ToString("N"));
        CreateFixture(root, centralPackageManagement);
        return root;
    }

    private static void CreateFixture(string root, bool centralPackageManagement)
    {
        Directory.CreateDirectory(root);
        var centralProperty = centralPackageManagement
            ? "<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>"
            : string.Empty;
        File.WriteAllText(Path.Combine(root, "NebulaApp.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWPF>true</UseWPF>
                <RootNamespace>NebulaApp</RootNamespace>
                {{centralProperty}}
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "App.xaml"),
            """
            <Application x:Class="NebulaApp.App"
                         xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Application.Resources><ResourceDictionary /></Application.Resources>
            </Application>
            """);
        File.WriteAllText(Path.Combine(root, "MainWindow.xaml.cs"),
            """
            namespace NebulaApp;

            public partial class MainWindow : System.Windows.Window
            {
                public MainWindow() => InitializeComponent();
            }
            """);
        CreatePack(root);
    }

    private static void CreatePack(string root)
    {
        var pack = Path.Combine(root, ".wpfdevtools", "packs", "nebula", "1.0.0");
        Directory.CreateDirectory(Path.Combine(pack, "blocks"));
        Directory.CreateDirectory(Path.Combine(pack, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(pack, "pack.json"),
            """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"nebula","kind":"control-pack","displayName":"Nebula","version":"1.0.0","nugetPackages":[{"id":"Nebula.Controls","versionRange":"1.2.3"}],"xmlNamespaces":{"nebula":"urn:nebula"},"resourceSetup":{"applicationMergedDictionaries":["<nebula:Theme Mode=\"Light\" />"]},"preview":{"namespaceUri":"urn:nebula","clrNamespace":"Nebula.Preview","types":{"Shell":{"baseKind":"window"}}},"blocks":["nebula.shell"],"recipes":[]}
            """);
        File.WriteAllText(Path.Combine(pack, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}""");
        File.WriteAllText(Path.Combine(pack, "install.manifest.json"),
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"nebula","version":"1.0.0","scope":"project-local","path":".","enabled":true}""");
        File.WriteAllText(Path.Combine(pack, "blocks", "shell.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"nebula.shell","displayName":"Shell","description":"Third-party shell.","category":"window","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/shell.xaml.sbn","codeBehindBaseType":"Nebula.Controls.Shell"},"sourceHints":[]}""");
        File.WriteAllText(
            Path.Combine(pack, "renderers", "xaml", "shell.xaml.sbn"),
            "<nebula:Shell />");
    }

    private static string Blueprint()
        => """
            {"schemaVersion":"wpfdevtools.ui-blueprint.v1","name":"MainWindow","packs":[{"id":"nebula","version":"1.0.0","required":true,"role":"primary"}],"primaryPack":"nebula","layout":{"kind":"nebula.shell"}}
            """;

    private static void CreateDirectoryJunction(string linkPath, string targetPath)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        process.WaitForExit();
        process.ExitCode.Should().Be(0, process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd());
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _original;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _original);
    }
}
