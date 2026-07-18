using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed partial class UiBlueprintPreviewService(PackRegistry registry, SessionManager? sessionManager = null)
{
    private const int BuildTimeoutSeconds = 60;
    private const string PreviewLoadedSentinelFileName = "preview-host.loaded";
    private const string PreviewSdkOptionsFileName = "preview-host-sdk.txt";
    private const string PreviewSdkReadyFileName = "preview-host-sdk.ready";
    private const int GeneratedXamlLineOffset = 2;
    private const int GeneratedXamlColumnOffset = 4;

    private static readonly Regex MainWindowPositionPattern = new(
        @"MainWindow\.xaml\((?<line>\d+),(?<column>\d+)\)",
        RegexOptions.CultureInvariant);
    private static readonly Regex CompilerErrorLinePattern = new(
        @"\berror\s+[A-Za-z]+\d+\s*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public PreviewBlueprintResult Preview(PreviewBlueprintRequest request)
        => PreviewAsync(request).GetAwaiter().GetResult();

    public async Task<PreviewBlueprintResult> PreviewAsync(
        PreviewBlueprintRequest request,
        CancellationToken cancellationToken = default)
    {
        var render = new UiBlueprintRenderer(registry)
            .Render(new RenderBlueprintRequest(
                request.BlueprintJson,
                IncludeTransientElementCorrelation: true));
        request = request with { RuntimeElementCorrelations = render.ElementCorrelations };
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
                    rendererTemplatePath)
                {
                    RelatedJsonPaths = error.RelatedJsonPaths
                }).ToArray());
        }

        var propertyWarnings = CollectPropertyWarnings(request.BlueprintJson);

        var previewContract = new UiPackPreviewContractGenerator(registry).Generate(
            request.BlueprintJson,
            render.Xaml,
            request.RuntimePackApprovalTokens,
            render.PackFingerprints);
        if (!previewContract.Success)
        {
            return PreviewBlueprintResult.Invalid(request.RestoreEnabled, render.Xaml, previewContract.Diagnostics)
                with
                {
                    PropertyWarnings = propertyWarnings,
                    ElementCorrelations = render.ElementCorrelations
                };
        }

        var unsafeResources = UiPreviewRuntimeDependencyPolicy.ValidateResources(previewContract.RuntimeResources);
        if (unsafeResources.Count > 0)
        {
            return PreviewBlueprintResult.Invalid(request.RestoreEnabled, render.Xaml, unsafeResources)
                with
                {
                    PropertyWarnings = propertyWarnings,
                    ElementCorrelations = render.ElementCorrelations
                };
        }

        var tempRoot = request.TemporaryRoot
            ?? Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var output = new StringBuilder();
        try
        {
            var runtimeDiagnosticsRequested = RequiresRuntimeDiagnostics(request);
            UiPreviewProjectFiles.Write(
                tempRoot,
                render.Xaml,
                request.StartHost && runtimeDiagnosticsRequested,
                PreviewLoadedSentinelFileName,
                PreviewSdkOptionsFileName,
                PreviewSdkReadyFileName,
                previewContract,
                previewContract.RuntimeNuGetPackages,
                previewContract.RuntimeResources,
                request.ViewportWidth,
                request.ViewportHeight);
            cancellationToken.ThrowIfCancellationRequested();
            var restoreSucceeded = true;
            var cancelled = false;
            IReadOnlyList<PreviewDiagnostic> packageDiagnostics = [];
            if (request.RestoreEnabled)
            {
                var restore = await RunDotnetAsync(
                    tempRoot,
                    ["restore", "PreviewHost.csproj", "--ignore-failed-sources", "-v:minimal"],
                    output,
                    cancellationToken).ConfigureAwait(false);
                restoreSucceeded = restore.Succeeded;
                cancelled = restore.Cancelled;
                if (restoreSucceeded && !cancelled)
                {
                    packageDiagnostics = UiPreviewRuntimeDependencyPolicy.ValidateRestoredPackages(
                        tempRoot,
                        previewContract.RuntimeNuGetPackages);
                    restoreSucceeded = packageDiagnostics.Count == 0;
                }
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
                return CreateCancelledResult(request.RestoreEnabled, render.Xaml, output.ToString(), rendererTemplatePath)
                    with
                    {
                        PropertyWarnings = propertyWarnings,
                        ElementCorrelations = render.ElementCorrelations
                    };
            }

            var previewHost = new PreviewHostResult(buildSucceeded ? "compiled" : "not-started", Started: false);
            var diagnostics = CreateDiagnostics(
                    buildSucceeded,
                    output.ToString(),
                    rendererTemplatePath,
                    render.SourceMap)
                .Concat(previewContract.Advisories)
                .Concat(packageDiagnostics)
                .Concat(CreateRequestDiagnostics(request, rendererTemplatePath))
                .ToArray();
            if (buildSucceeded && request.StartHost)
            {
                previewHost = await StartPreviewHostAsync(tempRoot, output, request, cancellationToken).ConfigureAwait(false);
                diagnostics = diagnostics.Concat(CreateHostDiagnostics(previewHost, rendererTemplatePath)).ToArray();
            }

            var layoutRiskSummary = PreviewLayoutRiskAnalyzer.Analyze(
                previewHost.RuntimeDiagnostics ?? [],
                render.ElementCorrelations,
                request.CorrelationLookupLimit);

            return new PreviewBlueprintResult(
                Success: true,
                Valid: true,
                BuildSucceeded: buildSucceeded,
                RestoreEnabled: request.RestoreEnabled,
                BuildOutput: output.ToString(),
                Xaml: render.Xaml,
                Diagnostics: diagnostics,
                PreviewHost: previewHost)
            {
                PropertyWarnings = propertyWarnings,
                ElementCorrelations = render.ElementCorrelations,
                LayoutRiskSummary = layoutRiskSummary,
                UsesStructuralStubs = previewContract.UsesStructuralStubs,
                UsesRuntimeDependencies = previewContract.UsesRuntimeDependencies,
                RuntimePackApprovalReviews = previewContract.RuntimePackApprovalReviews
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            output.AppendLine("preview compile cancelled.");
            return CreateCancelledResult(request.RestoreEnabled, render.Xaml, output.ToString(), rendererTemplatePath)
                with
                {
                    PropertyWarnings = propertyWarnings,
                    ElementCorrelations = render.ElementCorrelations
                };
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
            var packId = ComposerPackKindResolver.ResolveDeclaredPackId(blueprint.Layout.Kind, blueprint.Packs.Select(pack => pack.Id))
                ?? ComposerPackKindResolver.GetFallbackPackId(blueprint.Layout.Kind);
            var packRef = blueprint.Packs.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, packId, StringComparison.Ordinal));
            if (packRef is null)
            {
                return string.Empty;
            }

            var packs = registry.ListPacks().Packs;
            var pack = packs.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, packRef.Id, StringComparison.Ordinal)
                && string.Equals(candidate.Version, packRef.Version, StringComparison.Ordinal));
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
        IReadOnlyList<RenderSourceMapEntry> sourceMap)
    {
        if (!buildSucceeded)
        {
            var diagnosticSource = IsRestoreArtifactsMissing(buildOutput)
                ? null
                : ResolveCompileDiagnosticSource(buildOutput, sourceMap);
            return
            [
                new(
                    "XamlCompileFailed",
                    FirstCompilerErrorLine(buildOutput)
                        ?? FirstNonEmptyLine(buildOutput)
                        ?? "Generated preview XAML did not compile.",
                    diagnosticSource?.JsonPath ?? "$.layout",
                    diagnosticSource?.RendererTemplatePath ?? rendererTemplatePath)
            ];
        }

        return
        [
            new("PreviewXamlCompiled", "Generated preview XAML compiled successfully.", "$.layout", rendererTemplatePath)
        ];
    }

    private static IReadOnlyList<PreviewDiagnostic> CreateRequestDiagnostics(
        PreviewBlueprintRequest request,
        string rendererTemplatePath)
        => RequiresRuntimeDiagnostics(request) && !request.StartHost
            ?
            [
                new(
                    "PreviewRuntimeDiagnosticsRequireStartHost",
                    "Runtime and screenshot diagnostics require startHost=true because they attach to the temporary preview host.",
                    "$",
                    rendererTemplatePath)
            ]
            : [];

    internal static bool RequiresRuntimeDiagnostics(PreviewBlueprintRequest request)
        => request.IncludeRuntimeDiagnostics || request.IncludeScreenshotDiagnostics;

    private static RenderSourceMapEntry? ResolveCompileDiagnosticSource(
        string buildOutput,
        IReadOnlyList<RenderSourceMapEntry> sourceMap)
    {
        if (!TryGetGeneratedXamlPosition(buildOutput, out var line, out var column))
        {
            return null;
        }

        return sourceMap
            .Where(entry => ContainsPosition(entry, line, column))
            .OrderByDescending(entry => entry.JsonPath.Length)
            .FirstOrDefault();
    }

    private static bool IsRestoreArtifactsMissing(string buildOutput)
        => buildOutput.Contains("project.assets.json", StringComparison.OrdinalIgnoreCase)
            || buildOutput.Contains("NETSDK1004", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetGeneratedXamlPosition(string buildOutput, out int line, out int column)
    {
        foreach (Match match in MainWindowPositionPattern.Matches(buildOutput))
        {
            if (!int.TryParse(match.Groups["line"].Value, out var windowLine)
                || !int.TryParse(match.Groups["column"].Value, out var windowColumn))
            {
                continue;
            }

            line = windowLine - GeneratedXamlLineOffset;
            column = Math.Max(1, windowColumn - GeneratedXamlColumnOffset);
            if (line > 0)
            {
                return true;
            }
        }

        line = 0;
        column = 0;
        return false;
    }

    private static bool ContainsPosition(RenderSourceMapEntry entry, int line, int column)
    {
        if (entry.StartLine == 0)
        {
            return false;
        }

        var afterStart = line > entry.StartLine
            || line == entry.StartLine && column >= entry.StartColumn;
        var beforeEnd = line < entry.EndLine
            || line == entry.EndLine && column <= entry.EndColumn;
        return afterStart && beforeEnd;
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

    private static string? FirstCompilerErrorLine(string value)
        => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => CompilerErrorLinePattern.IsMatch(line));

    private static async Task<DotnetCommandResult> RunDotnetAsync(
        string workingDirectory,
        string[] arguments,
        StringBuilder output,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = CreateDotnetStartInfo(workingDirectory);
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        process.StartInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
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

    internal static ProcessStartInfo CreateDotnetStartInfo(string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        startInfo.Environment["WINDIR"] = windowsDirectory;
        startInfo.Environment["SystemRoot"] = windowsDirectory;
        return startInfo;
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
