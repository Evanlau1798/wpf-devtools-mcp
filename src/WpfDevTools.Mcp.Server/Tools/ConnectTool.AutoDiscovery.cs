using System.Diagnostics;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class ConnectTool
{
    private static string GetErrorMessage(InjectionError error, int processId, WpfProcessInfo? processInfo)
    {
        return error switch
        {
            InjectionError.ProcessNotFound => $"Process {processId} not found or has exited",
            InjectionError.NotWpfApplication => $"Process {processId} is not a WPF application",
            InjectionError.AccessDenied when processInfo?.IsElevated == true =>
                $"Access denied to process {processId} because the target is elevated. Restart the MCP server as administrator to connect or control this WPF process.",
            InjectionError.AccessDenied => $"Access denied to process {processId}. Try running as administrator.",
            InjectionError.ArchitectureMismatch => $"Architecture mismatch for process {processId}. Ensure the MCP server architecture matches the target process (both x64 or both x86).",
            InjectionError.SingleFileApplication =>
                $"Process {processId} appears to use packaging that does not support injection. Start the target-side SDK host with matching WPFDEVTOOLS_AUTH_SECRET and local absolute WPFDEVTOOLS_CERT_DIR values, then retry connect(). Network paths are not allowed.",
            _ => $"Validation failed: {error}"
        };
    }

    internal static TimeSpan GetRemainingPipeConnectTimeout(TimeSpan elapsed, TimeSpan totalTimeout)
    {
        var remaining = totalTimeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    internal static void ValidateDllPath(string dllPath) => DllPathValidator.ValidateDllPath(dllPath);

    internal static bool IsLikelySdkOnlyPackaging(WpfProcessInfo processInfo)
    {
        if (processInfo.Runtime != TargetRuntime.NetCore || string.IsNullOrWhiteSpace(processInfo.ExecutablePath))
        {
            return false;
        }

        try
        {
            var executablePath = processInfo.ExecutablePath;
            if (!Path.IsPathRooted(executablePath) || !File.Exists(executablePath))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(executablePath);
            var baseName = Path.GetFileNameWithoutExtension(executablePath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(baseName))
            {
                return false;
            }

            if (string.Equals(baseName, "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var runtimeConfigPath = Path.Combine(directory, baseName + ".runtimeconfig.json");
            var depsPath = Path.Combine(directory, baseName + ".deps.json");

            return !File.Exists(runtimeConfigPath) && !File.Exists(depsPath);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                $"ConnectTool SDK-only packaging heuristic failed: {SensitiveLogRedactor.Redact(ex.Message)}");
            return false;
        }
    }

    private AutoDiscoveryResolution TryResolveAutoDiscoveredProcess(
        ProcessDiscoverySelectionStrategy selectionStrategy,
        ProcessWindowFilter windowFilter)
    {
        var currentProcessIsElevated = _isCurrentProcessElevated();
        var allProcesses = _processDetector.GetAllWpfProcesses(windowFilter);
        var redactedCandidateCount = 0;
        var allowedProcesses = new List<WpfProcessInfo>();
        McpTargetAuthorization? discoveryAbort = null;
        foreach (var process in allProcesses)
        {
            var authorization = _targetPolicy(process);
            if (authorization.IsAllowed)
            {
                allowedProcesses.Add(process);
                continue;
            }

            redactedCandidateCount++;
            if (authorization.ShouldAbortDiscovery && discoveryAbort is null)
            {
                discoveryAbort = authorization;
            }
        }

        if (discoveryAbort is { } policyFailure)
        {
            return new AutoDiscoveryResolution(
                null,
                new
                {
                    success = false,
                    error = policyFailure.Error,
                    errorCode = policyFailure.ErrorCode,
                    hint = policyFailure.Hint,
                    redactedCandidateCount,
                    policyEnvVar = McpServerConfiguration.AllowedTargetsEnvVar
                },
                0,
                [],
                null,
                false,
                redactedCandidateCount);
        }

        var candidates = allowedProcesses
            .Select(process => ToAllowedCandidateSummary(process, currentProcessIsElevated))
            .OrderByDescending(candidate => candidate.WorkingSetBytes)
            .ThenBy(candidate => candidate.ProcessId)
            .ToArray();

        if (candidates.Length == 0)
        {
            if (redactedCandidateCount > 0)
            {
                return new AutoDiscoveryResolution(
                    null,
                    new
                    {
                        success = false,
                        error = "No allowlisted WPF targets were found for auto-discovery.",
                        errorCode = "SecurityError",
                        redactedCandidateCount,
                        policyEnvVar = McpServerConfiguration.AllowedTargetsEnvVar,
                        hint = $"Configure {McpServerConfiguration.AllowedTargetsEnvVar} with the exact local absolute executable path before connect() can auto-discover that target."
                    },
                    0,
                    candidates,
                    null,
                    false,
                    redactedCandidateCount);
            }

            return new AutoDiscoveryResolution(
                null,
                new
                {
                    success = false,
                    error = "No running WPF processes were found for the requested window filter. Start the target app or call get_processes() to confirm availability.",
                    errorCode = "NoWpfProcessesFound",
                    hint = "Launch the WPF app first, retry connect(windowFilter='all') to include background targets, or call get_processes() for manual discovery."
                },
                0,
                candidates,
                null,
                false,
                redactedCandidateCount);
        }

        if (candidates.Length == 1)
        {
            return new AutoDiscoveryResolution(
                candidates[0].ProcessId,
                null,
                1,
                candidates,
                candidates[0],
                false,
                redactedCandidateCount);
        }

        if (selectionStrategy != ProcessDiscoverySelectionStrategy.LargestWorkingSet)
        {
            return new AutoDiscoveryResolution(
                null,
                new
                {
                    success = false,
                    error = "Multiple WPF processes found; specify processId or use selectionStrategy='largest_working_set'.",
                    errorCode = "MultipleWpfProcessesFound",
                    candidateCount = candidates.Length,
                    redactedCandidateCount,
                    policyEnvVar = GetCandidateRedactionPolicyEnvVar(redactedCandidateCount),
                    processes = candidates.Select(ToContractCandidate).ToArray(),
                    hint = "Call connect(processId) for a specific target, or retry connect(selectionStrategy='largest_working_set') if the largest process is acceptable."
                },
                candidates.Length,
                candidates,
                null,
                false,
                redactedCandidateCount);
        }

        return new AutoDiscoveryResolution(
            candidates[0].ProcessId,
            null,
            candidates.Length,
            candidates,
            candidates[0],
            true,
            redactedCandidateCount);
    }

    private ProcessDiscoveryCandidateSummary ToAllowedCandidateSummary(
        WpfProcessInfo process,
        bool currentProcessIsElevated)
    {
        var access = ProcessConnectionAccessEvaluator.Evaluate(
            process.ProcessId,
            process.IsElevated,
            currentProcessIsElevated);
        return new ProcessDiscoveryCandidateSummary(
            process.ProcessId,
            process.ProcessName,
            process.WindowTitle,
            _workingSetResolver(process.ProcessId),
            process.IsElevated,
            access.RequiresElevationToConnect,
            access.CanConnectFromCurrentServer,
            access.ConnectionWarning);
    }

    private static object ToContractCandidate(ProcessDiscoveryCandidateSummary candidate)
    {
        return new
        {
            processId = candidate.ProcessId,
            processName = candidate.ProcessName,
            windowTitle = candidate.WindowTitle,
            workingSetBytes = candidate.WorkingSetBytes,
            isElevated = candidate.IsElevated,
            requiresElevationToConnect = candidate.RequiresElevationToConnect,
            canConnectFromCurrentServer = candidate.CanConnectFromCurrentServer,
            connectionWarning = candidate.ConnectionWarning
        };
    }

    private static long ResolveWorkingSetBytes(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.WorkingSet64;
        }
        catch (ArgumentException)
        {
            return 0;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return 0;
        }
    }

    private sealed record AutoDiscoveryResolution(
        int? ProcessId,
        object? ErrorResult,
        int CandidateCount,
        IReadOnlyList<ProcessDiscoveryCandidateSummary> Candidates,
        ProcessDiscoveryCandidateSummary? SelectedCandidate,
        bool AutoSelected,
        int RedactedCandidateCount);
}
