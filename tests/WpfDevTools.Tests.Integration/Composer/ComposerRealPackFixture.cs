using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Tests.Integration.TestSupport;

namespace WpfDevTools.Tests.Integration.Composer;

internal static class ComposerRealPackFixture
{
    internal const string MaterialDesignBlueprint = """
        {
          "schemaVersion":"wpfdevtools.ui-blueprint.v1",
          "name":"MaterialDesignConsumer",
          "packs":[
            {"id":"core","version":"0.1.0","required":true,"role":"layout-pack"},
            {"id":"materialdesign","version":"5.3.2","required":true,"role":"primary"}
          ],
          "primaryPack":"materialdesign",
          "layout":{
            "kind":"materialdesign.window",
            "properties":{"title":"Material acceptance","width":880,"height":560},
            "slots":{"content":[{
              "kind":"core.stack",
              "properties":{"margin":"24","spacing":"0,0,0,12"},
              "slots":{"children":[{
                "kind":"materialdesign.card",
                "slots":{"content":[{
                  "kind":"core.stack",
                  "slots":{"children":[
                    {"kind":"core.text","properties":{"text":"Material workspace","fontSize":24,"fontWeight":"SemiBold"}},
                    {"kind":"core.text","properties":{"text":"{Binding Status}"}},
                    {"kind":"materialdesign.action","properties":{"execute":"{Binding OpenWorkspaceCommand}","payload":"material-532","caption":"Open workspace"}}
                  ]}
                }]}
              }]}
            }]}
          }
        }
        """;

    internal const string MahAppsBlueprint = """
        {
          "schemaVersion":"wpfdevtools.ui-blueprint.v1",
          "name":"MahAppsConsumer",
          "packs":[{"id":"mahapps","version":"2.4.11","required":true,"role":"primary"}],
          "primaryPack":"mahapps",
          "layout":{"kind":"mahapps.window","properties":{"title":"Operations","width":880,"height":560}}
        }
        """;

    internal static string CreateProject(string packId, string version, string projectName, string packageReference)
    {
        var root = Path.Combine(ReleasePackagingTestHarness.CreateTempDirectory(), projectName);
        var source = ReleasePackagingTestHarness.GetRepoFilePath(
            Path.Combine("tests", "WpfDevTools.Tests.Integration", "TestData", "ComposerPacks", packId, version));
        CopyDirectory(source, Path.Combine(root, ".wpfdevtools", "packs", packId, version));
        Directory.CreateDirectory(root);
        var sdkProject = SecurityElement.Escape(ReleasePackagingTestHarness.GetRepoFilePath(
            "src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj"))!;
        var sdkReference = packId == "materialdesign"
            ? $"<ProjectReference Include=\"{sdkProject}\" />"
            : string.Empty;
        File.WriteAllText(
            Path.Combine(root, projectName + ".csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWPF>true</UseWPF>
                <RootNamespace>{{projectName}}</RootNamespace>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                {{packageReference}}
                {{sdkReference}}
              </ItemGroup>
            </Project>
            """,
            Encoding.UTF8);
        return root;
    }

    internal static void WriteApplication(
        string projectRoot,
        string projectName,
        string windowName,
        IReadOnlyList<string> resources,
        bool inspectorEnabled)
    {
        var namespaceAttribute = inspectorEnabled
            ? " xmlns:materialDesign=\"http://materialdesigninxaml.net/winfx/xaml/themes\""
            : " xmlns:mah=\"http://metro.mahapps.com/winfx/xaml/controls\"";
        File.WriteAllText(
            Path.Combine(projectRoot, "App.xaml"),
            $$"""
            <Application x:Class="{{projectName}}.App" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"{{namespaceAttribute}} StartupUri="{{windowName}}.xaml">
              <Application.Resources>
                <ResourceDictionary>
                  <ResourceDictionary.MergedDictionaries>
            {{string.Join(Environment.NewLine, resources.Select(resource => "        " + resource))}}
                  </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
              </Application.Resources>
            </Application>
            """,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(projectRoot, "App.xaml.cs"),
            $"using System.Windows; namespace {projectName}; public partial class App : Application {{ }}",
            Encoding.UTF8);

        if (inspectorEnabled)
        {
            WriteMaterialWindowCode(projectRoot, projectName, windowName);
        }
        else
        {
            File.WriteAllText(
                Path.Combine(projectRoot, windowName + ".xaml.cs"),
                $"using MahApps.Metro.Controls; namespace {projectName}; public partial class {windowName} : MetroWindow {{ public {windowName}() => InitializeComponent(); }}",
                Encoding.UTF8);
        }
    }

    internal static async Task<(int ExitCode, string Output)> RunDotNetAsync(
        string projectRoot,
        string verb,
        bool noRestore,
        TimeSpan timeout)
    {
        var project = Directory.EnumerateFiles(projectRoot, "*.csproj").Single();
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = projectRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(verb);
        startInfo.ArgumentList.Add(project);
        if (noRestore)
        {
            startInfo.ArgumentList.Add("--no-restore");
        }
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--verbosity");
        startInfo.ArgumentList.Add("minimal");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("dotnet process did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet {verb} timed out for {project}");
        }

        return (process.ExitCode, await stdout + Environment.NewLine + await stderr);
    }

    private static void WriteMaterialWindowCode(string projectRoot, string projectName, string windowName)
    {
        File.WriteAllText(
            Path.Combine(projectRoot, windowName + ".xaml.cs"),
            $$"""
            using System.ComponentModel;
            using System.IO;
            using System.Windows;
            using System.Windows.Input;
            using WpfDevTools.Inspector.Sdk;

            namespace {{projectName}};

            public partial class {{windowName}} : Window
            {
                public {{windowName}}()
                {
                    InitializeComponent();
                    DataContext = new WorkspaceViewModel();
                    _ = Task.Run(InitializeInspectorWhenReady);
                }

                private void InitializeInspectorWhenReady()
                {
                    var optionsPath = Path.Combine(AppContext.BaseDirectory, "inspector-options.txt");
                    for (var attempt = 0; attempt < 300; attempt++)
                    {
                        if (File.Exists(optionsPath))
                        {
                            var lines = File.ReadAllLines(optionsPath);
                            File.Delete(optionsPath);
                            Dispatcher.Invoke(() => InspectorSdk.InitializeWithOptions(new InspectorSdkOptions
                            {
                                AuthenticationSecretBase64 = lines[0],
                                CertificateDirectory = lines[1]
                            }));
                            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "inspector-ready.txt"), "ready");
                            return;
                        }
                        Thread.Sleep(100);
                    }
                }
            }

            public sealed class WorkspaceViewModel : INotifyPropertyChanged
            {
                private string _status = "Ready";
                public WorkspaceViewModel() => OpenWorkspaceCommand = new RelayCommand(parameter => Status = "Workspace opened: " + parameter);
                public ICommand OpenWorkspaceCommand { get; }
                public string Status
                {
                    get => _status;
                    private set { _status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); }
                }
                public event PropertyChangedEventHandler? PropertyChanged;
            }

            public sealed class RelayCommand(Action<object?> execute) : ICommand
            {
                public bool CanExecute(object? parameter) => true;
                public void Execute(object? parameter) => execute(parameter);
                public event EventHandler? CanExecuteChanged { add { } remove { } }
            }
            """,
            Encoding.UTF8);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }
}
