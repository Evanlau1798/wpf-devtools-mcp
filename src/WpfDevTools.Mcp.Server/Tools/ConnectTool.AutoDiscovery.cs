using System.Diagnostics;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Shared.Enums;

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
            _ => $"Validation failed: {error}"
        };
    }

    internal static TimeSpan GetRemainingPipeConnectTimeout(TimeSpan elapsed, TimeSpan totalTimeout)
    {
        var remaining = totalTimeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    internal static void ValidateDllPath(string dllPath) => DllPathValidator.ValidateDllPath(dllPath);

    private AutoDiscoveryResolution TryResolveAutoDiscoveredProcess(
        ProcessDiscoverySelectionStrategy selectionStrategy,
        ProcessWindowFilter windowFilter)
    {
        var currentProcessIsElevated = _isCurrentProcessElevated();
        var candidates = _processDetector
            .GetAllWpfProcesses(windowFilter)
            .Select(process =>
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
            })
            .OrderByDescending(candidate => candidate.WorkingSetBytes)
            .ThenBy(candidate => candidate.ProcessId)
            .ToArray();

        if (candidates.Length == 0)
        {
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
                false);
        }

        if (candidates.Length == 1)
        {
            return new AutoDiscoveryResolution(
                candidates[0].ProcessId,
                null,
                1,
                candidates,
                candidates[0],
                false);
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
                    processes = candidates.Select(ToContractCandidate).ToArray(),
                    hint = "Call connect(processId) for a specific target, or retry connect(selectionStrategy='largest_working_set') if the largest process is acceptable."
                },
                candidates.Length,
                candidates,
                null,
                false);
        }

        return new AutoDiscoveryResolution(
            candidates[0].ProcessId,
            null,
            candidates.Length,
            candidates,
            candidates[0],
            true);
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
        bool AutoSelected);
}
