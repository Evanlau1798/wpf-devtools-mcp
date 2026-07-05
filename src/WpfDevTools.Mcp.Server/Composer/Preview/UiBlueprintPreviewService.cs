using System.Diagnostics;
using System.Text;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed class UiBlueprintPreviewService(PackRegistry registry)
{
    private const int BuildTimeoutSeconds = 60;

    public PreviewBlueprintResult Preview(PreviewBlueprintRequest request)
    {
        var render = new UiBlueprintRenderer(registry)
            .Render(new RenderBlueprintRequest(request.BlueprintJson));
        var rendererTemplatePath = ResolveRootRendererTemplatePath(request.BlueprintJson);
        if (!render.Valid)
        {
            return PreviewBlueprintResult.Invalid(
                request.RestoreEnabled,
                render.Xaml,
                render.Errors.Select(error => new PreviewDiagnostic(
                    error.Code,
                    error.Message,
                    error.JsonPath,
                    rendererTemplatePath)).ToArray());
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            WritePreviewProject(tempRoot, render.Xaml);
            var output = new StringBuilder();
            var restoreSucceeded = !request.RestoreEnabled || RunDotnet(tempRoot, ["restore", "PreviewHost.csproj", "--ignore-failed-sources", "-v:minimal"], output);
            var buildSucceeded = restoreSucceeded
                && RunDotnet(tempRoot, ["build", "PreviewHost.csproj", "--no-restore", "-v:minimal"], output);
            var buildOutput = output.ToString();
            var diagnostics = CreateDiagnostics(buildSucceeded, buildOutput, rendererTemplatePath, render.Xaml);

            return new PreviewBlueprintResult(
                Success: true,
                Valid: true,
                BuildSucceeded: buildSucceeded,
                RestoreEnabled: request.RestoreEnabled,
                BuildOutput: buildOutput,
                Xaml: render.Xaml,
                Diagnostics: diagnostics,
                PreviewHost: new PreviewHostResult(buildSucceeded ? "compiled" : "not-started", Started: false));
        }
        finally
        {
            if (!request.KeepArtifacts && Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private string ResolveRootRendererTemplatePath(string blueprintJson)
    {
        try
        {
            var blueprint = ComposerJsonLoader.Parse<UiBlueprint>(
                blueprintJson,
                "<inline-blueprint>",
                UiComposerSchemaVersions.UiBlueprint);
            var packId = blueprint.Layout.Kind.Split('.', 2)[0];
            var pack = registry.ListPacks().Packs.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, packId, StringComparison.Ordinal));
            if (pack is null)
            {
                return string.Empty;
            }

            var loaded = ComposerPackLoader.Load(pack.RootPath);
            var block = loaded.Blocks.FirstOrDefault(candidate =>
                string.Equals(candidate.Kind, blueprint.Layout.Kind, StringComparison.Ordinal));
            return block is null || string.IsNullOrWhiteSpace(block.Renderer.XamlTemplate)
                ? string.Empty
                : Path.GetFullPath(Path.Combine(pack.RootPath, block.Renderer.XamlTemplate.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<PreviewDiagnostic> CreateDiagnostics(
        bool buildSucceeded,
        string buildOutput,
        string rendererTemplatePath,
        string xaml)
    {
        if (!buildSucceeded)
        {
            return
            [
                new(
                    "XamlCompileFailed",
                    FirstNonEmptyLine(buildOutput) ?? "Generated preview XAML did not compile.",
                    "$.layout",
                    rendererTemplatePath)
            ];
        }

        var diagnostics = new List<PreviewDiagnostic>
        {
            new("PreviewXamlCompiled", "Generated preview XAML compiled successfully.", "$.layout", rendererTemplatePath)
        };
        if (xaml.Contains("<ui:Button.Icon>", StringComparison.Ordinal))
        {
            diagnostics.Add(new("ButtonIconPropertyElementValid", "Button icon slot compiled as Button.Icon property element.", "$.layout", rendererTemplatePath));
        }

        if (xaml.Contains("<ui:DataGrid.Columns>", StringComparison.Ordinal))
        {
            diagnostics.Add(new("DataGridColumnsPropertyElementValid", "DataGrid columns slot compiled as DataGrid.Columns property element.", "$.layout", rendererTemplatePath));
        }

        return diagnostics;
    }

    private static string? FirstNonEmptyLine(string value)
        => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

    private static void WritePreviewProject(string root, string generatedXaml)
    {
        File.WriteAllText(Path.Combine(root, "PreviewHost.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWPF>true</UseWPF>
                <UseAppHost>false</UseAppHost>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """, Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "App.xaml"), """
            <Application x:Class="PreviewHost.App" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" StartupUri="MainWindow.xaml" />
            """, Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "App.xaml.cs"), BuildAppCode(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "MainWindow.xaml"), BuildWindowXaml(generatedXaml), Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "MainWindow.xaml.cs"), BuildMainWindowCode(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "WpfUiStubs.cs"), WpfUiStubs, Encoding.UTF8);
    }

    private static string BuildAppCode()
        => string.Join(
            Environment.NewLine,
            "using System.Windows;",
            "namespace PreviewHost;",
            "public partial " + "class App : Application { }",
            string.Empty);

    private static string BuildMainWindowCode()
        => string.Join(
            Environment.NewLine,
            "using System.Windows;",
            "namespace PreviewHost;",
            "public partial " + "class MainWindow : Window { public MainWindow() { InitializeComponent(); } }",
            string.Empty);

    private static string BuildWindowXaml(string generatedXaml)
        => string.Join(
            Environment.NewLine,
            """<Window x:Class="PreviewHost.MainWindow" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:ui="clr-namespace:Wpf.Ui.Controls">""",
            "  <Grid>",
            Indent(generatedXaml, "    "),
            "  </Grid>",
            "</Window>",
            string.Empty);

    private static string Indent(string value, string indentation)
        => string.Join(
            Environment.NewLine,
            value.Split(["\r\n", "\n"], StringSplitOptions.None).Select(line => indentation + line));

    private static bool RunDotnet(string workingDirectory, string[] arguments, StringBuilder output)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(BuildTimeoutSeconds * 1000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            output.AppendLine($"dotnet {string.Join(' ', arguments)} timed out after {BuildTimeoutSeconds} seconds.");
            return false;
        }

        output.Append(standardOutput.GetAwaiter().GetResult());
        output.Append(standardError.GetAwaiter().GetResult());
        return process.ExitCode == 0;
    }

    private const string WpfUiStubs =
        """
        using System.Collections.ObjectModel;
        using System.Windows;
        using System.Windows.Controls;
        using System.Windows.Markup;

        [assembly: XmlnsDefinition("http://schemas.lepo.co/wpfui/2022/xaml", "Wpf.Ui.Controls")]

        namespace Wpf.Ui.Controls;

        public class Button : ContentControl
        {
            public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
                nameof(Icon), typeof(object), typeof(Button));
            public static readonly DependencyProperty AppearanceProperty = DependencyProperty.Register(
                nameof(Appearance), typeof(string), typeof(Button));
            public object? Icon
            {
                get => GetValue(IconProperty);
                set => SetValue(IconProperty, value);
            }

            public string? Appearance
            {
                get => (string?)GetValue(AppearanceProperty);
                set => SetValue(AppearanceProperty, value);
            }
        }

        public class SymbolIcon : Control
        {
            public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
                nameof(Symbol), typeof(string), typeof(SymbolIcon));
            public string? Symbol
            {
                get => (string?)GetValue(SymbolProperty);
                set => SetValue(SymbolProperty, value);
            }
        }

        public class TextBlock : System.Windows.Controls.TextBlock
        {
            public static readonly DependencyProperty AppearanceProperty = DependencyProperty.Register(
                nameof(Appearance), typeof(string), typeof(TextBlock));
            public string? Appearance
            {
                get => (string?)GetValue(AppearanceProperty);
                set => SetValue(AppearanceProperty, value);
            }
        }

        public class Card : ItemsControl
        {
            public static readonly DependencyProperty AppearanceProperty = DependencyProperty.Register(
                nameof(Appearance), typeof(string), typeof(Card));
            public string? Appearance
            {
                get => (string?)GetValue(AppearanceProperty);
                set => SetValue(AppearanceProperty, value);
            }
        }

        public class NavigationView : ItemsControl
        {
            public static readonly DependencyProperty PaneDisplayModeProperty = DependencyProperty.Register(
                nameof(PaneDisplayMode), typeof(string), typeof(NavigationView));
            public Collection<object> MenuItems { get; } = new();
            public Collection<object> FooterMenuItems { get; } = new();
            public string? PaneDisplayMode
            {
                get => (string?)GetValue(PaneDisplayModeProperty);
                set => SetValue(PaneDisplayModeProperty, value);
            }
        }

        public class NavigationViewItem : HeaderedContentControl
        {
            public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
                nameof(Icon), typeof(object), typeof(NavigationViewItem));
            public static readonly DependencyProperty TargetPageTagProperty = DependencyProperty.Register(
                nameof(TargetPageTag), typeof(string), typeof(NavigationViewItem));
            public object? Icon
            {
                get => GetValue(IconProperty);
                set => SetValue(IconProperty, value);
            }

            public string? TargetPageTag
            {
                get => (string?)GetValue(TargetPageTagProperty);
                set => SetValue(TargetPageTagProperty, value);
            }
        }

        public class TabView : ItemsControl
        {
            public int SelectedIndex { get; set; }
        }

        public class TabViewItem : HeaderedContentControl
        {
            public bool IsClosable { get; set; }
        }

        public class ContentDialog : ItemsControl
        {
            public string? Title { get; set; }
        }

        public class Snackbar : ItemsControl
        {
            public double Timeout { get; set; }
        }

        public class TitleBar : Control
        {
            public string? Title { get; set; }
        }

        public class FluentWindow : Window
        {
            public static readonly DependencyProperty TitleBarProperty = DependencyProperty.Register(
                nameof(TitleBar), typeof(object), typeof(FluentWindow));
            public object? TitleBar
            {
                get => GetValue(TitleBarProperty);
                set => SetValue(TitleBarProperty, value);
            }
        }

        public class DataGrid : ItemsControl
        {
            public Collection<object> Columns { get; } = new();
        }
        """;
}

internal sealed record PreviewBlueprintRequest(
    string BlueprintJson,
    bool RestoreEnabled = true,
    bool KeepArtifacts = false);

internal sealed record PreviewBlueprintResult(
    bool Success,
    bool Valid,
    bool BuildSucceeded,
    bool RestoreEnabled,
    string BuildOutput,
    string Xaml,
    IReadOnlyList<PreviewDiagnostic> Diagnostics,
    PreviewHostResult PreviewHost)
{
    public static PreviewBlueprintResult Invalid(
        bool restoreEnabled,
        string xaml,
        IReadOnlyList<PreviewDiagnostic> diagnostics)
        => new(false, false, false, restoreEnabled, string.Empty, xaml, diagnostics, new PreviewHostResult("not-started", Started: false));
}

internal sealed record PreviewDiagnostic(
    string Code,
    string Message,
    string JsonPath,
    string RendererTemplatePath);

internal sealed record PreviewHostResult(string Status, bool Started);
