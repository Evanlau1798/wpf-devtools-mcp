using System.Text;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static class UiPreviewProjectFiles
{
    internal static void Write(
        string root,
        string generatedXaml,
        bool includeRuntimeDiagnostics,
        string loadedSentinelFileName,
        string sdkOptionsFileName,
        string sdkReadyFileName)
    {
        var generatedRootIsWindow = IsFluentWindowRoot(generatedXaml);
        File.WriteAllText(Path.Combine(root, "PreviewHost.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWPF>true</UseWPF>
                <UseAppHost>false</UseAppHost>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            {{BuildInspectorSdkReferenceItemGroup(includeRuntimeDiagnostics)}}
            </Project>
            """, Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "App.xaml"), """
            <Application x:Class="PreviewHost.App" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" StartupUri="MainWindow.xaml" />
            """, Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "App.xaml.cs"), BuildAppCode(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "MainWindow.xaml"), BuildWindowXaml(generatedXaml, generatedRootIsWindow), Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(root, "MainWindow.xaml.cs"),
            BuildMainWindowCode(
                includeRuntimeDiagnostics,
                loadedSentinelFileName,
                sdkOptionsFileName,
                sdkReadyFileName),
            Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "WpfUiStubs.cs"), UiPreviewProjectStubs.WpfUi, Encoding.UTF8);
    }

    private static string BuildAppCode()
        => string.Join(
            Environment.NewLine,
            "using System.Windows;",
            "namespace PreviewHost;",
            "public partial " + "class App : Application { }",
            string.Empty);

    private static string BuildMainWindowCode(
        bool includeRuntimeDiagnostics,
        string loadedSentinelFileName,
        string sdkOptionsFileName,
        string sdkReadyFileName)
    {
        var lines = new List<string>
        {
            "using System;",
            "using System.IO;"
        };
        if (includeRuntimeDiagnostics)
        {
            lines.Add("using System.Threading;");
            lines.Add("using System.Threading.Tasks;");
        }

        lines.Add("using System.Windows;");
        if (includeRuntimeDiagnostics)
        {
            lines.Add("using WpfDevTools.Inspector.Sdk;");
        }

        lines.AddRange(
        [
            "namespace PreviewHost;",
            "public partial " + "class MainWindow : Window",
            "{",
            "    public MainWindow()",
            "    {",
            "        try",
            "        {",
            "            InitializeComponent();",
            "            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, \"" + loadedSentinelFileName + "\"), \"loaded\");"
        ]);

        if (includeRuntimeDiagnostics)
        {
            lines.Add("            _ = Task.Run(InitializeInspectorWhenOptionsArrive);");
        }

        lines.AddRange(
        [
            "        }",
            "        catch (Exception ex)",
            "        {",
            "            Console.Error.WriteLine(\"preview host view failed: \" + ex.GetType().FullName + \": \" + ex.Message);",
            "            throw;",
            "        }",
            "    }"
        ]);

        if (includeRuntimeDiagnostics)
        {
            lines.AddRange(BuildInspectorInitializationCode(sdkOptionsFileName, sdkReadyFileName));
        }

        lines.Add("}");
        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string[] BuildInspectorInitializationCode(string sdkOptionsFileName, string sdkReadyFileName)
        =>
        [
            string.Empty,
            "    private void InitializeInspectorWhenOptionsArrive()",
            "    {",
            "        try",
            "        {",
            "            var optionsPath = Path.Combine(AppContext.BaseDirectory, \"" + sdkOptionsFileName + "\");",
            "            var readyPath = Path.Combine(AppContext.BaseDirectory, \"" + sdkReadyFileName + "\");",
            "            for (var attempt = 0; attempt < 100; attempt++)",
            "            {",
            "                if (File.Exists(optionsPath))",
            "                {",
            "                    string[] lines;",
            "                    try",
            "                    {",
            "                        lines = File.ReadAllLines(optionsPath);",
            "                    }",
            "                    finally",
            "                    {",
            "                        DeleteFileBestEffort(optionsPath);",
            "                    }",
            "                    if (lines.Length >= 2)",
            "                    {",
            "                        Dispatcher.Invoke(() => InspectorSdk.InitializeWithOptions(new InspectorSdkOptions",
            "                        {",
            "                            AuthenticationSecretBase64 = lines[0],",
            "                            CertificateDirectory = lines[1]",
            "                        }));",
            "                        if (InspectorSdk.LastInitializationStatus.IsInitialized)",
            "                        {",
            "                            File.WriteAllText(readyPath, \"ready\");",
            "                        }",
            "                        else",
            "                        {",
            "                            var status = InspectorSdk.LastInitializationStatus;",
            "                            Console.Error.WriteLine(\"preview host inspector sdk failed: \" + status.ErrorCode + \": \" + status.ErrorMessage);",
            "                        }",
            "                    }",
            "                    return;",
            "                }",
            string.Empty,
            "                Thread.Sleep(50);",
            "            }",
            "        }",
            "        catch (Exception ex)",
            "        {",
            "            Console.Error.WriteLine(\"preview host inspector sdk failed: \" + ex.GetType().FullName + \": \" + ex.Message);",
            "        }",
            "    }",
            string.Empty,
            "    private static void DeleteFileBestEffort(string path)",
            "    {",
            "        try",
            "        {",
            "            File.Delete(path);",
            "        }",
            "        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)",
            "        {",
            "        }",
            "    }"
        ];

    private static string BuildInspectorSdkReferenceItemGroup(bool includeRuntimeDiagnostics)
    {
        if (!includeRuntimeDiagnostics)
        {
            return string.Empty;
        }

        var sourceProject = Path.Combine(
            ComposerRuntimePaths.ResolveComposerRoot(),
            "src",
            "WpfDevTools.Inspector.Sdk",
            "WpfDevTools.Inspector.Sdk.csproj");
        if (File.Exists(sourceProject))
        {
            return $"""
              <ItemGroup>
                <ProjectReference Include="{EscapeXml(sourceProject)}" />
              </ItemGroup>
            """;
        }

        var packagedReferences = ResolvePackagedInspectorSdkReferences();
        return packagedReferences.Any(reference => reference.Name == "WpfDevTools.Inspector.Sdk")
            ? BuildAssemblyReferenceItemGroup(packagedReferences)
            : string.Empty;
    }

    private static IReadOnlyList<(string Name, string Path)> ResolvePackagedInspectorSdkReferences()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var references = new List<(string Name, string Path)>
        {
            ("WpfDevTools.Inspector.Sdk", Path.Combine(baseDirectory, "WpfDevTools.Inspector.Sdk.dll")),
            ("WpfDevTools.Inspector", Path.Combine(baseDirectory, "inspectors", "net8.0-windows", "WpfDevTools.Inspector.dll")),
            ("WpfDevTools.Shared", Path.Combine(baseDirectory, "WpfDevTools.Shared.dll"))
        };
        return references.Where(reference => File.Exists(reference.Path)).ToArray();
    }

    private static string BuildAssemblyReferenceItemGroup(IReadOnlyList<(string Name, string Path)> references)
        => "  <ItemGroup>" + Environment.NewLine +
           string.Join(Environment.NewLine, references.Select(reference => $"""
                <Reference Include="{EscapeXml(reference.Name)}">
                  <HintPath>{EscapeXml(reference.Path)}</HintPath>
                </Reference>
            """)) +
           Environment.NewLine + "  </ItemGroup>";

    private static string EscapeXml(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static bool IsFluentWindowRoot(string generatedXaml)
        => generatedXaml.TrimStart().StartsWith("<ui:FluentWindow", StringComparison.Ordinal);

    private static string BuildWindowXaml(string generatedXaml, bool generatedRootIsWindow)
        => generatedRootIsWindow
            ? AdaptFluentWindowRootForPreviewHost(generatedXaml)
            : string.Join(
                Environment.NewLine,
                """<Window x:Class="PreviewHost.MainWindow" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:ui="clr-namespace:Wpf.Ui.Controls">""",
                "  <Grid>",
                Indent(generatedXaml, "    "),
                "  </Grid>",
                "</Window>",
                string.Empty);

    private static string AdaptFluentWindowRootForPreviewHost(string generatedXaml)
    {
        const string rootTag = "<ui:FluentWindow";
        const string hostRootTag = "<Window x:Class=\"PreviewHost.MainWindow\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:ui=\"clr-namespace:Wpf.Ui.Controls\"";
        var index = generatedXaml.IndexOf(rootTag, StringComparison.Ordinal);
        var hosted = index < 0
            ? generatedXaml
            : generatedXaml[..index] + hostRootTag + generatedXaml[(index + rootTag.Length)..];
        hosted = RemoveFluentWindowTitleBar(hosted);
        return hosted.Replace("</ui:FluentWindow>", "</Window>", StringComparison.Ordinal);
    }

    private static string RemoveFluentWindowTitleBar(string xaml)
    {
        const string startTag = "<ui:FluentWindow.TitleBar>";
        const string endTag = "</ui:FluentWindow.TitleBar>";
        var start = xaml.IndexOf(startTag, StringComparison.Ordinal);
        if (start < 0)
        {
            return xaml;
        }

        var end = xaml.IndexOf(endTag, start, StringComparison.Ordinal);
        return end < 0
            ? xaml
            : xaml[..start] + xaml[(end + endTag.Length)..];
    }

    private static string Indent(string value, string indentation)
        => string.Join(
            Environment.NewLine,
            value.Split(["\r\n", "\n"], StringSplitOptions.None).Select(line => indentation + line));
}
