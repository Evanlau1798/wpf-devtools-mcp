using System.Diagnostics;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class ConnectTool
{
    private const string ConnectionSourceActiveSession = "active-session";
    private const string ConnectionSourceSdkHostedInspector = "sdk-hosted-inspector";
    private const string ConnectionSourceRawInjection = "raw-injection";

    private static object CreateConnectResponse(
        object connectResult,
        int processId,
        bool explicitProcessSelection,
        ProcessDiscoverySelectionStrategy selectionStrategy,
        AutoDiscoveryResolution? autoDiscoveryResolution)
    {
        if (connectResult is not ConnectOperationResult result)
        {
            return connectResult;
        }

        return result.SuccessKind switch
        {
            ConnectSuccessKind.AlreadyConnected => CreateAlreadyConnectedResponse(
                processId,
                explicitProcessSelection,
                selectionStrategy,
                autoDiscoveryResolution),
            ConnectSuccessKind.ReusedExistingHost => CreateSuccessfulConnectResponse(
                processId,
                explicitProcessSelection,
                selectionStrategy,
                autoDiscoveryResolution,
                reusedExistingHost: true),
            _ => CreateSuccessfulConnectResponse(
                processId,
                explicitProcessSelection,
                selectionStrategy,
                autoDiscoveryResolution,
                reusedExistingHost: false)
        };
    }

    private static object CreateAlreadyConnectedResponse(
        int processId,
        bool explicitProcessSelection,
        ProcessDiscoverySelectionStrategy selectionStrategy,
        AutoDiscoveryResolution? autoDiscoveryResolution)
    {
        var message = BuildConnectSuccessMessage("Already connected to process.");

        if (!explicitProcessSelection &&
            autoDiscoveryResolution?.SelectedCandidate is ProcessDiscoveryCandidateSummary candidate)
        {
            return new
            {
                success = true,
                message,
                processId,
                connectionSource = ConnectionSourceActiveSession,
                processName = candidate.ProcessName,
                windowTitle = candidate.WindowTitle,
                autoDiscovered = true,
                autoSelected = autoDiscoveryResolution.AutoSelected,
                selectionReason = autoDiscoveryResolution.AutoSelected
                    ? ProcessDiscoverySelectionStrategies.ToContractValue(selectionStrategy)
                    : null,
                candidateCount = autoDiscoveryResolution.CandidateCount,
                redactedCandidateCount = autoDiscoveryResolution.RedactedCandidateCount,
                policyEnvVar = GetCandidateRedactionPolicyEnvVar(autoDiscoveryResolution.RedactedCandidateCount),
                processes = autoDiscoveryResolution.Candidates.Select(ToContractCandidate).ToArray()
            };
        }

        return new
        {
            success = true,
            message,
            processId,
            connectionSource = ConnectionSourceActiveSession
        };
    }

    private static object CreateSuccessfulConnectResponse(
        int processId,
        bool explicitProcessSelection,
        ProcessDiscoverySelectionStrategy selectionStrategy,
        AutoDiscoveryResolution? autoDiscoveryResolution,
        bool reusedExistingHost)
    {
        var message = reusedExistingHost
            ? BuildConnectSuccessMessage("Connected to an existing Inspector host.")
            : BuildConnectSuccessMessage("Connected successfully.");

        if (!explicitProcessSelection &&
            autoDiscoveryResolution?.SelectedCandidate is ProcessDiscoveryCandidateSummary candidate)
        {
            if (reusedExistingHost)
            {
                return new
                {
                    success = true,
                    message,
                    processId,
                    connectionSource = ConnectionSourceSdkHostedInspector,
                    processName = candidate.ProcessName,
                    windowTitle = candidate.WindowTitle,
                    autoDiscovered = true,
                    autoSelected = autoDiscoveryResolution.AutoSelected,
                    selectionReason = autoDiscoveryResolution.AutoSelected
                        ? ProcessDiscoverySelectionStrategies.ToContractValue(selectionStrategy)
                        : null,
                    candidateCount = autoDiscoveryResolution.CandidateCount,
                    redactedCandidateCount = autoDiscoveryResolution.RedactedCandidateCount,
                    policyEnvVar = GetCandidateRedactionPolicyEnvVar(autoDiscoveryResolution.RedactedCandidateCount),
                    processes = autoDiscoveryResolution.Candidates.Select(ToContractCandidate).ToArray(),
                    reusedExistingHost = true
                };
            }

            return new
            {
                success = true,
                message,
                processId,
                connectionSource = ConnectionSourceRawInjection,
                processName = candidate.ProcessName,
                windowTitle = candidate.WindowTitle,
                autoDiscovered = true,
                autoSelected = autoDiscoveryResolution.AutoSelected,
                selectionReason = autoDiscoveryResolution.AutoSelected
                    ? ProcessDiscoverySelectionStrategies.ToContractValue(selectionStrategy)
                    : null,
                candidateCount = autoDiscoveryResolution.CandidateCount,
                redactedCandidateCount = autoDiscoveryResolution.RedactedCandidateCount,
                policyEnvVar = GetCandidateRedactionPolicyEnvVar(autoDiscoveryResolution.RedactedCandidateCount),
                processes = autoDiscoveryResolution.Candidates.Select(ToContractCandidate).ToArray()
            };
        }

        if (reusedExistingHost)
        {
            return new
            {
                success = true,
                message,
                processId,
                connectionSource = ConnectionSourceSdkHostedInspector,
                reusedExistingHost = true
            };
        }

        return new
        {
            success = true,
            message,
            processId,
            connectionSource = ConnectionSourceRawInjection
        };
    }

    private static string BuildConnectSuccessMessage(string prefix)
        => $"{prefix} Start with get_ui_summary, get_element_snapshot, or get_form_summary to build scene-first context before any tree-heavy follow-up.";

    private static string? GetCandidateRedactionPolicyEnvVar(int redactedCandidateCount)
        => redactedCandidateCount > 0 ? McpServerConfiguration.AllowedTargetsEnvVar : null;

    private static object CreatePreInjectionConnectFailure(
        int processId,
        NamedPipeConnectFailure failure)
    {
        var pipeConnectFailure = DescribePipeConnectFailure(failure, processId);
        return new
        {
            success = false,
            error = pipeConnectFailure.Error,
            errorCode = pipeConnectFailure.ErrorCode,
            hint = pipeConnectFailure.Hint
        };
    }

    private object CreateSessionConnectionFailure(int processId, InvalidOperationException exception)
    {
        if (_sessionManager.TryActivateConnectedSession(processId))
        {
            return ConnectOperationResult.AlreadyConnected;
        }

        if (exception.Message.Contains("Maximum session limit", StringComparison.Ordinal))
        {
            Trace.WriteLine($"ConnectTool session limit prevented attach for process {processId}: {SensitiveLogRedactor.Redact(exception.ToString())}");
            return new
            {
                success = false,
                error = "The MCP server has reached its maximum number of active sessions.",
                errorCode = "SessionLimitExceeded",
                hint = "Disconnect an existing session before connecting another target process."
            };
        }

        if (exception.Message.Contains("already exists", StringComparison.Ordinal))
        {
            return new
            {
                success = false,
                error = $"A session for process {processId} was created concurrently before connect could finish attaching.",
                errorCode = "SessionConflict",
                hint = "Retry connect, or reuse the existing session if it is already connected."
            };
        }

        Trace.WriteLine($"ConnectTool session connection failure for process {processId}: {SensitiveLogRedactor.Redact(exception.ToString())}");

        return new
        {
            success = false,
            error = "Connect could not attach the new session because the server encountered an internal error.",
            errorCode = "InternalError",
            hint = "Retry connect. If the problem persists, restart the MCP server."
        };
    }

    private static (string ErrorCode, string Error) DescribeInjectionFailure(
        InjectionResult injectionResult,
        int processId,
        WpfProcessInfo processInfo)
    {
        var errorCode = injectionResult.Error switch
        {
            InjectionError.Timeout or InjectionError.PipeReadyTimeout => "Timeout",
            _ => injectionResult.Error.ToString()
        };

        var error = injectionResult.Error switch
        {
            InjectionError.ProcessNotFound or
            InjectionError.NotWpfApplication or
            InjectionError.ArchitectureMismatch or
            InjectionError.SingleFileApplication => GetErrorMessage(injectionResult.Error, processId, processInfo),
            InjectionError.AccessDenied => "Access denied while starting the injected inspector.",
            InjectionError.FileNotFound => "Required injection file was not found. Verify the build output and inspector paths.",
            InjectionError.AllocationFailed => "Failed to allocate remote memory during injection.",
            InjectionError.WriteFailed => "Failed to write injector payload into the target process.",
            InjectionError.CreateThreadFailed => "Failed to start the remote injection thread.",
            InjectionError.Timeout when injectionResult.TimeoutReason == InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart
                => InjectionBudgetExhaustedMessage,
            InjectionError.Timeout => "Injection timed out before the target process became ready.",
            InjectionError.PipeReadyTimeout when injectionResult.TimeoutReason == InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart
                => PipeReadyBudgetExhaustedMessage,
            InjectionError.PipeReadyTimeout => "Bootstrap completed, but the Inspector Named Pipe did not become ready before the timeout expired.",
            InjectionError.BootstrapFailed => DescribeBootstrapFailureMessage(injectionResult),
            InjectionError.Unknown => "Injection failed due to an unexpected internal error. Check server logs for details.",
            _ => "Injection failed"
        };

        Trace.WriteLine(
            $"ConnectTool injection failure for process {processId}: error={injectionResult.Error}; " +
            $"stage={injectionResult.FailedAtStage}; exitCode={injectionResult.BootstrapExitCode}; " +
            $"detail={SensitiveLogRedactor.Redact(injectionResult.ErrorMessage)}");

        return (errorCode, error);
    }

    private static string DescribeBootstrapFailureMessage(InjectionResult injectionResult)
    {
        if (injectionResult.BootstrapExitCode is int exitCode &&
            exitCode < 0)
        {
            return "Bootstrap failed while starting the injected inspector.";
        }

        return injectionResult.FailedAtStage switch
        {
            BootstrapStage.ClrDetection => "Bootstrap failed during CLR detection.",
            BootstrapStage.ClrHosting => "Bootstrap failed during CLR hosting initialization.",
            BootstrapStage.ManagedEntrypoint => "Bootstrap failed while invoking the managed bootstrap entrypoint.",
            BootstrapStage.LoadLibrary => "Bootstrap failed while loading the inspector DLL.",
            BootstrapStage.AuthSecretLoad => "Bootstrap failed while loading the authentication secret file.",
            BootstrapStage.PipeReady => "Bootstrap completed, but the Inspector Named Pipe did not become ready before the timeout expired.",
            _ => "Bootstrap failed while starting the injected inspector."
        };
    }

    private static object CreateSessionManagerDisposedFailure()
    {
        return new
        {
            success = false,
            error = "Connect cannot continue because the session manager is shutting down.",
            errorCode = "ServerShuttingDown",
            hint = "Wait for the MCP server to finish shutting down or restart it before retrying connect."
        };
    }

    internal static (string ErrorCode, string Error, string Hint) DescribePipeConnectFailure(
        NamedPipeConnectFailure failure,
        int processId)
    {
        return failure switch
        {
            NamedPipeConnectFailure.AuthenticationFailed => (
                "SecurityError",
                $"Authentication failed connecting to Inspector Named Pipe for process {processId}.",
                "The shared secret used by the MCP server does not match the injected Inspector. Restart the target or reconnect after refreshing the secure session state."),
            NamedPipeConnectFailure.SecureTransportFailed => (
                "SecurityError",
                $"Secure transport handshake failed connecting to Inspector Named Pipe for process {processId}.",
                "Verify certificate or thumbprint configuration, then retry connect. A stale or mismatched certificate directory can cause this failure."),
            NamedPipeConnectFailure.ServerProcessMismatch => (
                "SecurityError",
                $"Connected Named Pipe server for process {processId} is not hosted by the requested target process.",
                "A different local process is advertising the expected pipe name. Restart the target process, then retry connect so the MCP server can attach to the real Inspector host."),
            NamedPipeConnectFailure.IncompatibleHost => (
                "CompatibilityError",
                $"Existing Inspector host for process {processId} is incompatible with the current MCP server build.",
                "Restart the target process so connect() can inject or reuse an Inspector host that matches the current protocol and build."),
            NamedPipeConnectFailure.AccessDenied => (
                "AccessDenied",
                $"Access denied connecting to Inspector Named Pipe for process {processId}.",
                "Ensure the MCP server and target process run under compatible privileges and user sessions before retrying connect."),
            _ => (
                "Timeout",
                "Timeout connecting to Inspector Named Pipe",
                "The Inspector DLL may not have started its Named Pipe server. Check if the target process is frozen or if antivirus is blocking the pipe.")
        };
    }
}
