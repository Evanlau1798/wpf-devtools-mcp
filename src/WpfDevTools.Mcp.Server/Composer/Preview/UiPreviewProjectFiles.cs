using System.Text;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static class UiPreviewProjectFiles
{
    private static readonly Regex RootXmlNamespaceAttributePattern = new(
        @"\s+xmlns(?::[A-Za-z_][A-Za-z0-9_.-]*)?\s*=\s*(""[^""]*""|'[^']*'|[^\s/>]+)",
        RegexOptions.CultureInvariant);

    internal static void Write(
        string root,
        string generatedXaml,
        bool includeRuntimeDiagnostics,
        string loadedSentinelFileName,
        string sdkOptionsFileName,
        string sdkReadyFileName,
        PreviewContractGenerationResult previewContract)
    {
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
        File.WriteAllText(Path.Combine(root, "MainWindow.xaml"), BuildWindowXaml(generatedXaml, previewContract), Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(root, "MainWindow.xaml.cs"),
            BuildMainWindowCode(
                includeRuntimeDiagnostics,
                loadedSentinelFileName,
                sdkOptionsFileName,
                sdkReadyFileName,
                previewContract.WindowRootType),
            Encoding.UTF8);
        if (!string.IsNullOrWhiteSpace(previewContract.Source))
        {
            File.WriteAllText(Path.Combine(root, "PackPreviewStubs.cs"), previewContract.Source, Encoding.UTF8);
        }
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
        string sdkReadyFileName,
        string? windowRootType)
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
        lines.Add("using System.Windows.Threading;");
        if (includeRuntimeDiagnostics)
        {
            lines.Add("using WpfDevTools.Inspector.Sdk;");
        }

        lines.AddRange(
        [
            "namespace PreviewHost;",
            "public partial " + "class MainWindow : " + (windowRootType ?? "Window"),
            "{",
            "    public MainWindow()",
            "    {",
            "        try",
            "        {",
            "            InitializeComponent();",
            "            ContentRendered += OnContentRendered;"
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
            "    }",
            string.Empty,
            "    private void OnContentRendered(object? sender, EventArgs e)",
            "    {",
            "        ContentRendered -= OnContentRendered;",
            "        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(MarkPreviewReady));",
            "    }",
            string.Empty,
            "    private static void MarkPreviewReady()",
            "    {",
            "        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, \"" + loadedSentinelFileName + "\"), \"loaded\");",
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

        var packagedReferences = ResolvePackagedInspectorSdkReferences();
        if (packagedReferences.Any(reference => reference.Name == "WpfDevTools.Inspector.Sdk"))
        {
            return BuildAssemblyReferenceItemGroup(packagedReferences);
        }

        var sourceProject = Path.Combine(
            ComposerRuntimePaths.ResolveComposerRoot(),
            "src",
            "WpfDevTools.Inspector.Sdk",
            "WpfDevTools.Inspector.Sdk.csproj");
        return File.Exists(sourceProject)
            ? $"""
              <ItemGroup>
                <ProjectReference Include="{EscapeXml(sourceProject)}" />
              </ItemGroup>
            """
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

    private static string BuildWindowXaml(string generatedXaml, PreviewContractGenerationResult previewContract)
    {
        var previewFragment = RemoveRootXmlNamespaceDeclarations(generatedXaml);
        var namespaceAttributes = BuildPreviewNamespaceAttributes(previewContract.XmlNamespaces);
        var windowRootTag = previewContract.WindowRootTag
            ?? (HasNativeWindowRoot(previewFragment) ? "Window" : null);
        return windowRootTag is not null
            ? AddPreviewHostClass(previewFragment, windowRootTag, namespaceAttributes)
            : string.Join(
                Environment.NewLine,
                "<Window x:Class=\"PreviewHost.MainWindow\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"" + namespaceAttributes + ">",
                "  <Grid>",
                Indent(previewFragment, "    "),
                "  </Grid>",
                "</Window>",
                string.Empty);
    }

    private static bool HasNativeWindowRoot(string xaml)
    {
        var rootStart = XamlDocumentRootLocator.FindStart(xaml);
        if (rootStart < 0)
        {
            return false;
        }

        var root = xaml.AsSpan(rootStart + 1);
        const string tag = "Window";
        return root.StartsWith(tag, StringComparison.Ordinal)
            && root.Length > tag.Length
            && (char.IsWhiteSpace(root[tag.Length]) || root[tag.Length] is '>' or '/');
    }

    private static string BuildPreviewNamespaceAttributes(IReadOnlyDictionary<string, string> namespaces)
        => string.Concat(namespaces.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $" xmlns:{item.Key}=\"clr-namespace:{item.Value}\""));

    private static string AddPreviewHostClass(string generatedXaml, string rootTag, string namespaceAttributes)
    {
        var rootStart = XamlDocumentRootLocator.FindStart(generatedXaml);
        var attributeStart = rootStart + 1 + rootTag.Length;
        if (rootStart < 0
            || attributeStart > generatedXaml.Length
            || !generatedXaml.AsSpan(rootStart + 1, rootTag.Length).SequenceEqual(rootTag))
        {
            return generatedXaml;
        }

        var attributes = " x:Class=\"PreviewHost.MainWindow\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\""
            + " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\""
            + namespaceAttributes;
        return generatedXaml.Insert(attributeStart, attributes);
    }

    private static string RemoveRootXmlNamespaceDeclarations(string xaml)
    {
        var rootStart = XamlDocumentRootLocator.FindStart(xaml);
        if (rootStart < 0)
        {
            return xaml;
        }

        var rootEnd = FindTagEnd(xaml, rootStart + 1);
        if (rootEnd < 0)
        {
            return xaml;
        }

        var rootTag = xaml[rootStart..rootEnd];
        return xaml[..rootStart] + RootXmlNamespaceAttributePattern.Replace(rootTag, string.Empty) + xaml[rootEnd..];
    }

    private static int FindTagEnd(string xaml, int start)
    {
        var quote = '\0';
        for (var index = start; index < xaml.Length; index++)
        {
            if (quote != '\0')
            {
                if (xaml[index] == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (xaml[index] is '"' or '\'')
            {
                quote = xaml[index];
            }
            else if (xaml[index] == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private static string Indent(string value, string indentation)
        => string.Join(
            Environment.NewLine,
            value.Split(["\r\n", "\n"], StringSplitOptions.None).Select(line => indentation + line));
}
