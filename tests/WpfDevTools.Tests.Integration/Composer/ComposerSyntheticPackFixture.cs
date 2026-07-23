using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WpfDevTools.Tests.Integration.TestSupport;

namespace WpfDevTools.Tests.Integration.Composer;

internal static class ComposerSyntheticPackFixture
{
    internal const string Blueprint = """
        {
          "schemaVersion":"wpfdevtools.ui-blueprint.v1",
          "name":"SyntheticConsumer",
          "packs":[
            {"id":"core","version":"0.1.0","required":true,"role":"layout-pack"},
            {"id":"synthetic","version":"1.0.0","required":true,"role":"primary"}
          ],
          "primaryPack":"synthetic",
          "layout":{
            "kind":"synthetic.window",
            "properties":{"title":"Extension acceptance","width":880,"height":560},
            "slots":{"content":[{
              "kind":"core.stack",
              "properties":{"margin":"24","spacing":"0,0,0,12"},
              "slots":{"children":[
                {"kind":"core.text","properties":{"text":"Extension workspace","fontSize":24,"fontWeight":"SemiBold"}},
                {"kind":"core.text","properties":{"text":"{Binding Status}"}},
                {"kind":"synthetic.action","properties":{"execute":"{Binding RunExtensionCommand}","payload":"neutral-payload","caption":"Run extension action"}}
              ]}
            }]}
          }
        }
        """;

    internal static string CreateProject()
    {
        var root = Path.Combine(
            ReleasePackagingTestHarness.CreateTempDirectory(),
            "SyntheticConsumer");
        var source = ReleasePackagingTestHarness.GetRepoFilePath(Path.Combine(
            "tests",
            "WpfDevTools.Tests.Integration",
            "TestData",
            "ComposerPacks",
            "synthetic-extension",
            "1.0.0"));
        CopyDirectory(
            source,
            Path.Combine(
                root,
                ".wpfdevtools",
                "packs",
                "synthetic",
                "1.0.0"));
        Directory.CreateDirectory(root);
        var sdkProject = SecurityElement.Escape(
            ReleasePackagingTestHarness.GetRepoFilePath(
                "src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj"))!;
        File.WriteAllText(
            Path.Combine(root, "SyntheticConsumer.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWPF>true</UseWPF>
                <RootNamespace>SyntheticConsumer</RootNamespace>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{sdkProject}}" />
              </ItemGroup>
            </Project>
            """,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(root, "Directory.Packages.props"),
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(root, "SyntheticTheme.xaml"),
            """
            <ResourceDictionary
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Style x:Key="SyntheticActionStyle" TargetType="Button">
                <Setter Property="Padding" Value="12,6" />
              </Style>
            </ResourceDictionary>
            """,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(root, "ExtensionWindow.cs"),
            """
            using System.Windows;

            namespace SyntheticExtension.Controls;

            public class ExtensionWindow : Window;
            """,
            Encoding.UTF8);
        WriteApplicationShell(root);
        WriteWindowCode(root, "SyntheticConsumer", "MainWindow");
        return root;
    }

    private static void WriteApplicationShell(string projectRoot)
    {
        const string projectName = "SyntheticConsumer";
        var appPath = Path.Combine(projectRoot, "App.xaml");
        File.WriteAllText(
            appPath,
            $$"""
            <Application x:Class="{{projectName}}.App" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Application.Resources>
                <SolidColorBrush x:Key="ExistingBrush" Color="#FF102030" />
              </Application.Resources>
            </Application>
            """,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(projectRoot, "App.xaml.cs"),
            $"using System.Windows; namespace {projectName}; public partial class App : Application {{ }}",
            Encoding.UTF8);
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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("dotnet process did not start");
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

    private static void WriteWindowCode(
        string projectRoot,
        string projectName,
        string windowName)
    {
        File.WriteAllText(
            Path.Combine(projectRoot, windowName + ".xaml.cs"),
            $$"""
            using System.ComponentModel;
            using System.IO;
            using System.Windows.Input;
            using System.Windows;
            using WpfDevTools.Inspector.Sdk;

            namespace {{projectName}};

            public partial class {{windowName}} : Window
            {
                public {{windowName}}()
                {
                    InitializeComponent();
                    DataContext = new ExtensionViewModel();
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
                            File.WriteAllText(
                                Path.Combine(AppContext.BaseDirectory, "inspector-ready.txt"),
                                "ready");
                            return;
                        }
                        Thread.Sleep(100);
                    }
                }
            }

            public sealed class ExtensionViewModel : INotifyPropertyChanged
            {
                private string _status = "Ready";

                public ExtensionViewModel()
                    => RunExtensionCommand = new RelayCommand(
                        parameter => Status = "Extension action completed: " + parameter);

                public ICommand RunExtensionCommand { get; }

                public string Status
                {
                    get => _status;
                    private set
                    {
                        _status = value;
                        PropertyChanged?.Invoke(
                            this,
                            new PropertyChangedEventArgs(nameof(Status)));
                    }
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
        foreach (var directory in Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(
                Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.Copy(
                file,
                Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }
}
