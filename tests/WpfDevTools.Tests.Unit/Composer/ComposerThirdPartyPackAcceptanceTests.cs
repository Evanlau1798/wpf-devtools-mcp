using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using FluentAssertions;
using MaterialDesignThemes.Wpf;
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
    [StaFact]
    public async Task MaterialDesignPack_ShouldCompleteRealRenderPreviewApplyLaunchAndMcpInspection()
    {
        var projectRoot = RealExtensionPackFixture.CreateMaterialDesignProject();
        try
        {
            var registry = CreateRegistry(projectRoot);
            var blueprint = RealExtensionPackFixture.MaterialDesignBlueprint;
            var validation = new BlueprintValidationService(registry).Validate(blueprint);

            validation.Success.Should().BeTrue(string.Join(Environment.NewLine,
                validation.Errors.Select(error => error.Message)));
            validation.Resolution.Packs.Should().Contain(pack =>
                pack.Id == "materialdesign" && pack.Kind == "style-pack" && pack.Status == "resolved");
            validation.Resolution.Packs.Should().Contain(pack =>
                pack.Id == "core" && pack.Kind == "layout-pack" && pack.Status == "resolved");

            var render = new UiBlueprintRenderer(registry).Render(
                new RenderBlueprintRequest(blueprint, ProjectRoot: projectRoot));
            render.Success.Should().BeTrue(string.Join(Environment.NewLine,
                render.Errors.Select(error => error.Message)));
            render.Xaml.Should().Contain("<materialDesign:Card").And.Contain("MaterialDesignFilledButton");
            render.PackageIntegrationGuidance.Mode.Should().Be("project");
            render.RequiredNuGetPackages.Should().ContainSingle(package =>
                package.Id == "MaterialDesignThemes" && package.VersionRange == "[5.3.2]");
            AssertRealMaterialDesignSurface(render.Xaml, render.RequiredResources);

            var apply = new UiBlueprintApplyService(registry).Apply(
                new ApplyBlueprintRequest(blueprint, projectRoot, "MainWindow.xaml"));
            apply.Success.Should().BeTrue(string.Join(Environment.NewLine,
                apply.Errors.Select(error => error.Message)));
            apply.FilePlan.Should().Contain(item =>
                item.Role == "view" && item.TargetPath.EndsWith("MainWindow.xaml", StringComparison.Ordinal));
            var interaction = apply.BehaviorIntegrationContract.Interactions.Should().ContainSingle().Subject;
            interaction.CommandPath.Should().Be("OpenWorkspaceCommand");
            interaction.CommandParameter.Should().Be("material-532");
            interaction.Label.Should().Be("Open workspace");

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
            preview.ElementCorrelations.Should().HaveCount(6);
            preview.ElementCorrelations.Select(item => item.ElementName).Should().OnlyHaveUniqueItems();
            preview.PreviewHost.RuntimeDiagnostics.Should().Contain(diagnostic =>
                diagnostic.Tool == "connect" && diagnostic.Success);
            preview.PreviewHost.RuntimeDiagnostics.Should().Contain(diagnostic =>
                diagnostic.Tool == "get_ui_summary" && diagnostic.Success);
            preview.PreviewHost.RuntimeDiagnostics.Should().Contain(diagnostic =>
                diagnostic.Tool == "find_elements" && diagnostic.Success);
            var summary = preview.PreviewHost.RuntimeDiagnostics.Should().ContainSingle(diagnostic =>
                diagnostic.Tool == "get_ui_summary" && diagnostic.Success).Subject;
            summary.Payload.GetRawText().Should().Contain("Material workspace").And.Contain("Open workspace");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    private static void AssertRealMaterialDesignSurface(string xaml, IReadOnlyList<string> resources)
    {
        resources.Should().Contain(resource => resource.Contains("MaterialDesign3.Defaults.xaml", StringComparison.Ordinal));
        var window = XamlReader.Parse(xaml).Should().BeOfType<Window>().Subject;
        window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(
            """<materialDesign:BundledTheme xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes" BaseTheme="Dark" PrimaryColor="DeepPurple" SecondaryColor="Lime" />"""));
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml",
                UriKind.Absolute)
        });

        try
        {
            window.Show();
            window.UpdateLayout();
            var descendants = EnumerateDescendants(window).ToArray();
            descendants.OfType<Card>().Should().ContainSingle(card => card.IsVisible && card.ActualWidth > 100);
            descendants.OfType<Button>().Should().ContainSingle(button =>
                button.IsVisible && button.IsEnabled && Equals(button.Content, "Open workspace"));
            descendants.OfType<TextBlock>().Should().Contain(text =>
                text.IsVisible && text.Text == "Material workspace");
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            if (current is not Visual)
            {
                continue;
            }

            for (var index = VisualTreeHelper.GetChildrenCount(current) - 1; index >= 0; index--)
            {
                pending.Push(VisualTreeHelper.GetChild(current, index));
            }
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
