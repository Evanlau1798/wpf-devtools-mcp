using System.Diagnostics;
using System.Text;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed class UiBlueprintPreviewService(PackRegistry registry)
{
    private const int BuildTimeoutSeconds = 60;
    private const string PreviewLoadedSentinelFileName = "preview-host.loaded";

    public PreviewBlueprintResult Preview(PreviewBlueprintRequest request)
        => PreviewAsync(request).GetAwaiter().GetResult();

    public async Task<PreviewBlueprintResult> PreviewAsync(
        PreviewBlueprintRequest request,
        CancellationToken cancellationToken = default)
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

        var tempRoot = request.TemporaryRoot
            ?? Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var output = new StringBuilder();
        try
        {
            WritePreviewProject(tempRoot, render.Xaml);
            cancellationToken.ThrowIfCancellationRequested();
            var restoreSucceeded = true;
            var cancelled = false;
            if (request.RestoreEnabled)
            {
                var restore = await RunDotnetAsync(
                    tempRoot,
                    ["restore", "PreviewHost.csproj", "--ignore-failed-sources", "-v:minimal"],
                    output,
                    cancellationToken).ConfigureAwait(false);
                restoreSucceeded = restore.Succeeded;
                cancelled = restore.Cancelled;
            }

            var buildSucceeded = false;
            if (!cancelled && restoreSucceeded)
            {
                var build = await RunDotnetAsync(
                    tempRoot,
                    ["build", "PreviewHost.csproj", "--no-restore", "-v:minimal"],
                    output,
                    cancellationToken).ConfigureAwait(false);
                buildSucceeded = build.Succeeded;
                cancelled = build.Cancelled;
            }

            if (cancelled)
            {
                return CreateCancelledResult(request.RestoreEnabled, render.Xaml, output.ToString(), rendererTemplatePath);
            }

            var previewHost = new PreviewHostResult(buildSucceeded ? "compiled" : "not-started", Started: false);
            var diagnostics = CreateDiagnostics(buildSucceeded, output.ToString(), rendererTemplatePath, render.Xaml);
            if (buildSucceeded && request.StartHost)
            {
                previewHost = await StartPreviewHostAsync(tempRoot, output, cancellationToken).ConfigureAwait(false);
                diagnostics = diagnostics.Concat(CreateHostDiagnostics(previewHost, rendererTemplatePath)).ToArray();
            }

            return new PreviewBlueprintResult(
                Success: true,
                Valid: true,
                BuildSucceeded: buildSucceeded,
                RestoreEnabled: request.RestoreEnabled,
                BuildOutput: output.ToString(),
                Xaml: render.Xaml,
                Diagnostics: diagnostics,
                PreviewHost: previewHost);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            output.AppendLine("preview compile cancelled.");
            return CreateCancelledResult(request.RestoreEnabled, render.Xaml, output.ToString(), rendererTemplatePath);
        }
        finally
        {
            if (!request.KeepArtifacts && Directory.Exists(tempRoot))
            {
                DeleteDirectoryBestEffort(tempRoot);
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

    private static PreviewBlueprintResult CreateCancelledResult(
        bool restoreEnabled,
        string xaml,
        string buildOutput,
        string rendererTemplatePath)
        => new(
            Success: true,
            Valid: true,
            BuildSucceeded: false,
            RestoreEnabled: restoreEnabled,
            BuildOutput: buildOutput,
            Xaml: xaml,
            Diagnostics:
            [
                new("PreviewCancelled", "Preview compile was cancelled before completion.", "$.layout", rendererTemplatePath)
            ],
            PreviewHost: new PreviewHostResult("cancelled", Started: false));

    private static IReadOnlyList<PreviewDiagnostic> CreateHostDiagnostics(
        PreviewHostResult previewHost,
        string rendererTemplatePath)
    {
        if (previewHost.ViewLoaded)
        {
            return
            [
                new("PreviewHostStarted", "Temporary preview host process started.", "$.layout", rendererTemplatePath),
                new("PreviewHostViewLoaded", "Temporary preview host loaded the generated view.", "$.layout", rendererTemplatePath)
            ];
        }

        return
        [
            new("PreviewHostNotLoaded", $"Temporary preview host status: {previewHost.Status}.", "$.layout", rendererTemplatePath)
        ];
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
        File.WriteAllText(Path.Combine(root, "WpfUiStubs.cs"), UiPreviewProjectStubs.WpfUi, Encoding.UTF8);
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
            "using System;",
            "using System.IO;",
            "using System.Windows;",
            "namespace PreviewHost;",
            "public partial " + "class MainWindow : Window",
            "{",
            "    public MainWindow()",
            "    {",
            "        try",
            "        {",
            "            InitializeComponent();",
            "            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, \"" + PreviewLoadedSentinelFileName + "\"), \"loaded\");",
            "        }",
            "        catch (Exception ex)",
            "        {",
            "            Console.Error.WriteLine(\"preview host view failed: \" + ex.GetType().FullName + \": \" + ex.Message);",
            "            throw;",
            "        }",
            "    }",
            "}",
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

    private static async Task<DotnetCommandResult> RunDotnetAsync(
        string workingDirectory,
        string[] arguments,
        StringBuilder output,
        CancellationToken cancellationToken)
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(BuildTimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopProcessAsync(process).ConfigureAwait(false);
            output.Append(await standardOutput.ConfigureAwait(false));
            output.Append(await standardError.ConfigureAwait(false));
            output.AppendLine($"dotnet {string.Join(' ', arguments)} cancelled.");
            return new DotnetCommandResult(false, Cancelled: true);
        }
        catch (OperationCanceledException)
        {
            await StopProcessAsync(process).ConfigureAwait(false);
            output.Append(await standardOutput.ConfigureAwait(false));
            output.Append(await standardError.ConfigureAwait(false));

            output.AppendLine($"dotnet {string.Join(' ', arguments)} timed out after {BuildTimeoutSeconds} seconds.");
            return new DotnetCommandResult(false, Cancelled: false);
        }

        output.Append(await standardOutput.ConfigureAwait(false));
        output.Append(await standardError.ConfigureAwait(false));
        return new DotnetCommandResult(process.ExitCode == 0, Cancelled: false);
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task<PreviewHostResult> StartPreviewHostAsync(
        string tempRoot,
        StringBuilder output,
        CancellationToken cancellationToken)
    {
        var hostDll = Path.Combine(tempRoot, "bin", "Debug", "net8.0-windows", "PreviewHost.dll");
        var sentinelPath = Path.Combine(Path.GetDirectoryName(hostDll)!, PreviewLoadedSentinelFileName);
        if (!File.Exists(hostDll))
        {
            output.AppendLine("preview host binary was not found.");
            return new PreviewHostResult("missing-host", Started: false);
        }

        File.Delete(sentinelPath);
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = tempRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add(hostDll);

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            output.AppendLine("preview host failed to start: " + ex.Message);
            return new PreviewHostResult("start-failed", Started: false);
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        var processId = process.Id;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            while (!timeout.IsCancellationRequested && !process.HasExited)
            {
                if (File.Exists(sentinelPath))
                {
                    await StopProcessAsync(process).ConfigureAwait(false);
                    output.Append(await standardOutput.ConfigureAwait(false));
                    output.Append(await standardError.ConfigureAwait(false));
                    return new PreviewHostResult("loaded", Started: true, ViewLoaded: true, processId);
                }

                await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopProcessAsync(process).ConfigureAwait(false);
            output.Append(await standardOutput.ConfigureAwait(false));
            output.Append(await standardError.ConfigureAwait(false));
            return new PreviewHostResult("cancelled", Started: true, ViewLoaded: false, processId);
        }
        catch (OperationCanceledException)
        {
        }

        var status = process.HasExited ? "exited" : "window-timeout";
        await StopProcessAsync(process).ConfigureAwait(false);
        output.Append(await standardOutput.ConfigureAwait(false));
        output.Append(await standardError.ConfigureAwait(false));
        return new PreviewHostResult(status, Started: true, ViewLoaded: false, processId);
    }

    private static void DeleteDirectoryBestEffort(string tempRoot)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 2)
                {
                    return;
                }

                Thread.Sleep(100);
            }
        }
    }

}
