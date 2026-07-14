using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed partial class UiBlueprintPreviewService
{
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

    private async Task<PreviewHostResult> StartPreviewHostAsync(
        string tempRoot,
        StringBuilder output,
        PreviewBlueprintRequest request,
        CancellationToken cancellationToken)
    {
        var hostDll = Path.Combine(tempRoot, "bin", "Debug", "net8.0-windows", "PreviewHost.dll");
        var hostDirectory = Path.GetDirectoryName(hostDll)!;
        var sentinelPath = Path.Combine(hostDirectory, PreviewLoadedSentinelFileName);
        var sdkOptionsPath = Path.Combine(hostDirectory, PreviewSdkOptionsFileName);
        var sdkReadyPath = Path.Combine(hostDirectory, PreviewSdkReadyFileName);
        if (!File.Exists(hostDll))
        {
            output.AppendLine("preview host binary was not found.");
            return new PreviewHostResult("missing-host", Started: false);
        }

        DeleteFileBestEffort(sentinelPath);
        DeleteFileBestEffort(sdkOptionsPath);
        DeleteFileBestEffort(sdkReadyPath);
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = tempRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        process.StartInfo.Environment["WINDIR"] = windowsDirectory;
        process.StartInfo.Environment["SystemRoot"] = windowsDirectory;
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
                    var runtimeDiagnostics = await CaptureRuntimeDiagnosticsAfterSdkOptionsAsync(
                        request,
                        process,
                        hostDirectory,
                        sdkReadyPath,
                        cancellationToken).ConfigureAwait(false);
                    return new PreviewHostResult(
                        "loaded",
                        Started: true,
                        ViewLoaded: true,
                        processId,
                        runtimeDiagnostics);
                }

                await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new PreviewHostResult("cancelled", Started: true, ViewLoaded: false, processId);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await StopProcessAsync(process).ConfigureAwait(false);
            DeleteFileBestEffort(sdkOptionsPath);
            DeleteFileBestEffort(sdkReadyPath);
            output.Append(await standardOutput.ConfigureAwait(false));
            output.Append(await standardError.ConfigureAwait(false));
        }

        var status = timeout.IsCancellationRequested
            ? "window-timeout"
            : process.HasExited ? "exited" : "window-timeout";
        return new PreviewHostResult(status, Started: true, ViewLoaded: false, processId);
    }

    private async Task<IReadOnlyList<PreviewRuntimeDiagnostic>?> CaptureRuntimeDiagnosticsAfterSdkOptionsAsync(
        PreviewBlueprintRequest request,
        Process process,
        string hostDirectory,
        string sdkReadyPath,
        CancellationToken cancellationToken)
    {
        if (!RequiresRuntimeDiagnostics(request))
        {
            return null;
        }

        try
        {
            if (!WritePreviewSdkOptions(process, hostDirectory))
            {
                return
                [
                    CreateRuntimeDiagnosticFailure(
                        "PreviewDiagnosticsUnavailable",
                        "Preview runtime diagnostics require secure SessionManager transport artifacts.")
                ];
            }

            if (!await WaitForFileAsync(sdkReadyPath, process, cancellationToken).ConfigureAwait(false))
            {
                return
                [
                    CreateRuntimeDiagnosticFailure(
                        "PreviewSdkInitializationTimeout",
                        "Preview runtime diagnostics timed out waiting for the preview host Inspector SDK.")
                ];
            }

            return await CaptureRuntimeDiagnosticsAsync(request, process, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return
            [
                CreateRuntimeDiagnosticFailure(
                    "PreviewDiagnosticsFailed",
                    "Preview runtime diagnostics failed before completion.",
                    ex)
            ];
        }
    }

    private bool WritePreviewSdkOptions(Process process, string hostDirectory)
    {
        if (sessionManager is null)
        {
            return false;
        }

        var authSecret = sessionManager.GetAuthenticationSecretBase64(process.Id);
        var certificateDirectory = sessionManager.GetCertificateDirectory();
        if (string.IsNullOrWhiteSpace(authSecret) || string.IsNullOrWhiteSpace(certificateDirectory))
        {
            return false;
        }

        sessionManager.EnsureSecureTransportArtifactsCreated();
        var optionsPath = Path.Combine(hostDirectory, PreviewSdkOptionsFileName);
        var temporaryPath = optionsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllLines(temporaryPath, [authSecret, certificateDirectory], Encoding.UTF8);
            File.Move(temporaryPath, optionsPath, overwrite: true);
            return true;
        }
        finally
        {
            DeleteFileBestEffort(temporaryPath);
        }
    }

    private async Task<IReadOnlyList<PreviewRuntimeDiagnostic>?> CaptureRuntimeDiagnosticsAsync(
        PreviewBlueprintRequest request,
        Process process,
        CancellationToken cancellationToken)
    {
        if (!RequiresRuntimeDiagnostics(request))
        {
            return null;
        }

        if (sessionManager is null)
        {
            return
            [
                CreateRuntimeDiagnosticFailure(
                    "PreviewDiagnosticsUnavailable",
                    "Preview runtime diagnostics require a SessionManager.")
            ];
        }

        return await UiBlueprintPreviewDiagnosticsBridge.CaptureAsync(
            sessionManager,
            process,
            request.IncludeScreenshotDiagnostics,
            request.ScreenshotOutputMode,
            request.ScreenshotMaxWidth,
            request.ScreenshotMaxHeight,
            request.RuntimeElementCorrelations,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> WaitForFileAsync(string path, Process process, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline && !process.HasExited)
        {
            if (File.Exists(path))
            {
                return true;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        return File.Exists(path);
    }

    private static PreviewRuntimeDiagnostic CreateRuntimeDiagnosticFailure(
        string errorCode,
        string error,
        Exception? exception = null)
        => new("preview_diagnostics", Success: false, JsonSerializer.SerializeToElement(new
        {
            success = false,
            error,
            errorCode,
            exceptionType = exception?.GetType().FullName
        }));

    private static void DeleteFileBestEffort(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
