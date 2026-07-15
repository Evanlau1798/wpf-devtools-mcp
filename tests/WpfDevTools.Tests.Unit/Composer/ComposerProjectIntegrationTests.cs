using FluentAssertions;
using System.Xml.Linq;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("ProcessEnvironment")]
public sealed class ComposerProjectIntegrationTests
{
    [Fact]
    public void ApplyBlueprintDryRun_ShouldExposeDeterministicPackNeutralIntegrationOperations()
    {
        var root = CreateFixture();
        try
        {
            var service = new UiBlueprintApplyService(CreateRegistry(root));

            var first = service.Apply(new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));
            var second = service.Apply(new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));

            first.Success.Should().BeTrue(first.Errors.FirstOrDefault()?.Message);
            first.ProjectIntegrationPlan.Ready.Should().BeTrue();
            first.ProjectIntegrationPlan.PlanHash.Should().Be(second.ProjectIntegrationPlan.PlanHash);
            first.ProjectIntegrationPlan.Operations.Should().Contain(operation =>
                operation.Role == "package-reference"
                && operation.TargetPath == Path.Combine(root, "NebulaApp.csproj")
                && operation.Precondition.Exists
                && operation.Precondition.Sha256.Length == 64);
            first.ProjectIntegrationPlan.Operations.Should().Contain(operation =>
                operation.Role == "application-xaml"
                && operation.Purposes.Contains("resources")
                && operation.Purposes.Contains("startup")
                && operation.TargetPath == Path.Combine(root, "App.xaml"));
            first.ProjectIntegrationPlan.Operations.Should().Contain(operation =>
                operation.Role == "code-behind-base-type"
                && operation.TargetPath == Path.Combine(root, "MainWindow.xaml.cs"));
            File.ReadAllText(Path.Combine(root, "NebulaApp.csproj")).Should().NotContain("Nebula.Controls");
            File.ReadAllText(Path.Combine(root, "App.xaml")).Should().NotContain("urn:nebula");
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public void ProjectIntegration_ShouldApplyOnlyReviewedPlanWithBackupEvidence()
    {
        var root = CreateFixture();
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
        using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, root);
        try
        {
            var registry = CreateRegistry(root);
            var dryRun = new UiBlueprintApplyService(registry).Apply(
                new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));

            var result = new UiBlueprintProjectIntegrationService(registry).Apply(
                new ProjectIntegrationRequest(
                    Blueprint(),
                    root,
                    "MainWindow.xaml",
                    dryRun.ProjectIntegrationPlan.PlanHash,
                    ConfirmIntegration: true));

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            result.Applied.Should().BeTrue();
            result.RolledBack.Should().BeFalse();
            result.Changes.Should().HaveCount(3);
            result.Changes.Should().OnlyContain(change =>
                change.BackupPath != null && File.Exists(change.BackupPath));
            File.ReadAllText(Path.Combine(root, "NebulaApp.csproj"))
                .Should().Contain("PackageReference Include=\"Nebula.Controls\" Version=\"1.2.3\"");
            File.ReadAllText(Path.Combine(root, "App.xaml"))
                .Should().Contain("urn:nebula")
                .And.Contain("Theme")
                .And.Contain("StartupUri=\"MainWindow.xaml\"");
            File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"))
                .Should().Contain("partial class MainWindow : Nebula.Controls.Shell");
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public void ProjectIntegration_ShouldProduceStableNoOpPlanAfterApply()
    {
        var root = CreateFixture();
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
        using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, root);
        try
        {
            var registry = CreateRegistry(root);
            var service = new UiBlueprintApplyService(registry);
            var initial = service.Apply(new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));
            var applied = new UiBlueprintProjectIntegrationService(registry).Apply(
                new ProjectIntegrationRequest(
                    Blueprint(),
                    root,
                    "MainWindow.xaml",
                    initial.ProjectIntegrationPlan.PlanHash,
                    ConfirmIntegration: true));

            var firstNoOp = service.Apply(new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));
            var secondNoOp = service.Apply(new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));

            applied.Success.Should().BeTrue(applied.Errors.FirstOrDefault()?.Message);
            firstNoOp.Success.Should().BeTrue(firstNoOp.Errors.FirstOrDefault()?.Message);
            firstNoOp.ProjectIntegrationPlan.Ready.Should().BeTrue();
            firstNoOp.ProjectIntegrationPlan.Operations.Should().HaveCount(3);
            firstNoOp.ProjectIntegrationPlan.Operations.Should().OnlyContain(operation =>
                operation.Action == "none"
                && operation.Precondition.Exists
                && operation.Precondition.Sha256 == operation.ProposedSha256);
            secondNoOp.Success.Should().BeTrue(secondNoOp.Errors.FirstOrDefault()?.Message);
            secondNoOp.ProjectIntegrationPlan.Ready.Should().BeTrue();
            firstNoOp.ProjectIntegrationPlan.PlanHash.Should().Be(secondNoOp.ProjectIntegrationPlan.PlanHash);
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public void ProjectIntegration_ShouldRejectStaleReviewedPlanWithoutWriting()
    {
        var root = CreateFixture();
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
        using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, root);
        try
        {
            var registry = CreateRegistry(root);
            var dryRun = new UiBlueprintApplyService(registry).Apply(
                new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));
            var projectFile = Path.Combine(root, "NebulaApp.csproj");
            File.AppendAllText(projectFile, "<!-- user change -->");

            var result = new UiBlueprintProjectIntegrationService(registry).Apply(
                new ProjectIntegrationRequest(
                    Blueprint(),
                    root,
                    "MainWindow.xaml",
                    dryRun.ProjectIntegrationPlan.PlanHash,
                    ConfirmIntegration: true));

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Code == "IntegrationPlanChanged");
            File.ReadAllText(Path.Combine(root, "App.xaml")).Should().NotContain("urn:nebula");
            File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"))
                .Should().Contain("System.Windows.Window");
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public void ProjectIntegration_ShouldRollBackEarlierWritesWhenLaterOperationFails()
    {
        var root = CreateFixture();
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
        using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, root);
        try
        {
            var registry = CreateRegistry(root);
            var projectFile = Path.Combine(root, "NebulaApp.csproj");
            var originalProject = File.ReadAllText(projectFile);
            var dryRun = new UiBlueprintApplyService(registry).Apply(
                new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));
            var appPath = Path.Combine(root, "App.xaml");
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
            File.ReadAllText(projectFile).Should().Be(originalProject);
            File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"))
                .Should().Contain("System.Windows.Window");
        }
        finally
        {
            var appPath = Path.Combine(root, "App.xaml");
            if (File.Exists(appPath))
            {
                File.SetAttributes(appPath, FileAttributes.Normal);
            }

            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public async Task ProjectIntegrationTool_ShouldApplyTheReviewedDryRunPlan()
    {
        var root = CreateFixture();
        using var destructive = new EnvironmentVariableScope(McpServerConfiguration.AllowDestructiveToolsEnvVar, "true");
        using var writes = new EnvironmentVariableScope(McpServerConfiguration.AllowProjectWritesEnvVar, "true");
        using var roots = new EnvironmentVariableScope(McpServerConfiguration.AllowedProjectRootsEnvVar, root);
        try
        {
            var dryRun = await UiComposerMcpTools.ApplyUiBlueprint(
                Blueprint(),
                root,
                "MainWindow.xaml",
                cancellationToken: CancellationToken.None);
            var plan = dryRun.StructuredContent!.Value.GetProperty("projectIntegrationPlan");
            plan.GetProperty("ready").GetBoolean().Should().BeTrue();

            var result = await UiComposerMcpTools.ApplyUiProjectIntegration(
                Blueprint(),
                root,
                plan.GetProperty("planHash").GetString()!,
                "MainWindow.xaml",
                confirmIntegration: true,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeTrue();
            payload.GetProperty("applied").GetBoolean().Should().BeTrue();
            payload.GetProperty("changes").GetArrayLength().Should().Be(3);
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public void DryRun_ShouldInferStartupFromPackDeclaredWindowPreviewType()
    {
        var root = CreateFixture();
        try
        {
            var blockPath = Path.Combine(root, ".wpfdevtools", "packs", "nebula", "1.0.0", "blocks", "shell.block.json");
            File.WriteAllText(blockPath,
                """
                {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"nebula.shell","displayName":"Shell","description":"Third-party shell.","category":"window","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/shell.xaml.sbn"},"sourceHints":[]}
                """);

            var result = new UiBlueprintApplyService(CreateRegistry(root)).Apply(
                new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));

            result.ProjectIntegrationPlan.Operations.Should().Contain(operation =>
                operation.Role == "application-xaml" && operation.Purposes.Contains("startup"));
            result.ProjectIntegrationPlan.Operations.Should().NotContain(operation =>
                operation.Role == "code-behind-base-type");
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [Fact]
    public void DryRun_ShouldAddPackageReferenceToAnUnconditionalItemGroup()
    {
        var root = CreateFixture();
        try
        {
            var projectPath = Path.Combine(root, "NebulaApp.csproj");
            File.WriteAllText(projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><UseWPF>true</UseWPF><RootNamespace>NebulaApp</RootNamespace></PropertyGroup>
                  <ItemGroup Condition="'$(Configuration)' == 'Debug'"><Compile Include="DebugOnly.cs" /></ItemGroup>
                </Project>
                """);

            var result = new UiBlueprintApplyService(CreateRegistry(root)).Apply(
                new ApplyBlueprintRequest(Blueprint(), root, "MainWindow.xaml"));
            var packagePatch = result.ProjectIntegrationPlan.Operations
                .Single(operation => operation.Role == "package-reference");
            var packageReference = XDocument.Parse(packagePatch.ProposedContent).Descendants()
                .Single(element => element.Name.LocalName == "PackageReference");

            packageReference.Parent!.Attribute("Condition").Should().BeNull();
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
        var root = Path.Combine(Path.GetTempPath(), "wpfdevtools-project-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "NebulaApp.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWPF>true</UseWPF>
                <RootNamespace>NebulaApp</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "App.xaml"),
            """
            <Application x:Class="NebulaApp.App"
                         xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         StartupUri="Legacy.xaml">
              <Application.Resources>
                <ResourceDictionary>
                  <ResourceDictionary.MergedDictionaries />
                </ResourceDictionary>
              </Application.Resources>
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
        return root;
    }

    private static void CreatePack(string root)
    {
        var pack = Path.Combine(root, ".wpfdevtools", "packs", "nebula", "1.0.0");
        Directory.CreateDirectory(Path.Combine(pack, "blocks"));
        Directory.CreateDirectory(Path.Combine(pack, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(pack, "pack.json"),
            """
            {
              "schemaVersion":"wpfdevtools.ui-pack.v1","id":"nebula","kind":"control-pack","displayName":"Nebula","version":"1.0.0",
              "nugetPackages":[{"id":"Nebula.Controls","versionRange":"1.2.3"}],
              "xmlNamespaces":{"nebula":"urn:nebula"},
              "resourceSetup":{"applicationMergedDictionaries":["<nebula:Theme Mode=\"Light\" />"]},
              "preview":{"namespaceUri":"urn:nebula","clrNamespace":"Nebula.Preview","types":{"Shell":{"baseKind":"window"}}},
              "blocks":["nebula.shell"],"recipes":[]
            }
            """);
        File.WriteAllText(Path.Combine(pack, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}""");
        File.WriteAllText(Path.Combine(pack, "install.manifest.json"),
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"nebula","version":"1.0.0","scope":"project-local","path":".","enabled":true}""");
        File.WriteAllText(Path.Combine(pack, "blocks", "shell.block.json"),
            """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"nebula.shell","displayName":"Shell","description":"Third-party shell.","category":"window","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/shell.xaml.sbn","codeBehindBaseType":"Nebula.Controls.Shell"},"sourceHints":[]}
            """);
        File.WriteAllText(Path.Combine(pack, "renderers", "xaml", "shell.xaml.sbn"), "<nebula:Shell />");
    }

    private static string Blueprint()
        => """
            {"schemaVersion":"wpfdevtools.ui-blueprint.v1","name":"MainWindow","packs":[{"id":"nebula","version":"1.0.0","required":true,"role":"primary"}],"primaryPack":"nebula","layout":{"kind":"nebula.shell"}}
            """;

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
