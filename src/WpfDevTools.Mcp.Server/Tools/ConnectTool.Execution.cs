using System.Diagnostics;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class ConnectTool
{
    private object? ValidateAndAuthorizeTarget(
        int processId,
        CancellationToken cancellationToken,
        out ConnectTargetContext? context)
    {
        context = null;

        var existingSessionResult = TryReuseExistingSession(processId);
        if (existingSessionResult != null)
        {
            return existingSessionResult;
        }

        var rateLimitFailure = CheckConnectRateLimit(processId);
        if (rateLimitFailure != null)
        {
            return rateLimitFailure;
        }

        var processInfo = _processDetector.GetProcessInfo(processId);
        if (processInfo == null)
        {
            return new
            {
                success = false,
                error = $"Could not detect process info for {processId}",
                errorCode = "ProcessNotFound"
            };
        }

        var authorizationFailure = AuthorizeTarget(processId, processInfo);
        if (authorizationFailure != null)
        {
            return authorizationFailure;
        }

        var access = ProcessConnectionAccessEvaluator.Evaluate(
            processId,
            processInfo.IsElevated,
            _isCurrentProcessElevated());
        if (access.RequiresElevationToConnect)
        {
            return CreateElevationRequiredFailure(processInfo, access);
        }

        var secureTransportFailure = EnsureSecureTransport(processId, processInfo, access, cancellationToken);
        if (secureTransportFailure != null)
        {
            return secureTransportFailure;
        }

        context = new ConnectTargetContext(
            processInfo,
            access,
            IsLikelySdkOnlyPackaging(processInfo),
            _isRawInjectionTargetAllowed(processInfo));
        return null;
    }

    private object? TryReuseExistingSession(int processId)
    {
        if (!_sessionManager.HasSession(processId))
        {
            return null;
        }

        if (_sessionManager.TryActivateConnectedSession(processId))
        {
            return ConnectOperationResult.AlreadyConnected;
        }

        _sessionManager.RemoveSession(processId);
        return null;
    }

    private object? CheckConnectRateLimit(int processId)
    {
        var rateLimitStatus = _sessionManager.CheckRateLimitStatus(processId);
        return rateLimitStatus.Allowed
            ? null
            : RateLimitResponseFactory.Create(
                rateLimitStatus,
                "Rate limit exceeded for connect operations. Please slow down your requests.");
    }

    private object? AuthorizeTarget(int processId, WpfProcessInfo processInfo)
    {
        var targetAuthorization = _targetPolicy(processInfo);
        if (targetAuthorization.IsAllowed)
        {
            return null;
        }

        Trace.WriteLine($"ConnectTool target allowlist denied process {processId}: executable={processInfo.ExecutablePath}");
        return new
        {
            success = false,
            error = targetAuthorization.Error,
            errorCode = targetAuthorization.ErrorCode,
            hint = targetAuthorization.Hint,
            requiresExplicitTargetOptIn = true,
            policyEnvVar = McpServerConfiguration.AllowedTargetsEnvVar
        };
    }

    private static object CreateElevationRequiredFailure(
        WpfProcessInfo processInfo,
        ProcessConnectionAccess access)
    {
        return new
        {
            success = false,
            error = access.ConnectionWarning,
            errorCode = InjectionError.AccessDenied.ToString(),
            targetIsElevated = processInfo.IsElevated,
            requiresElevationToConnect = access.RequiresElevationToConnect,
            canConnectFromCurrentServer = access.CanConnectFromCurrentServer,
            suggestedAction = "Restart the MCP server as administrator and retry connect."
        };
    }

    private object? EnsureSecureTransport(
        int processId,
        WpfProcessInfo processInfo,
        ProcessConnectionAccess access,
        CancellationToken cancellationToken)
    {
        try
        {
            _sessionManager.EnsureSecureTransportArtifactsCreated();
            return null;
        }
        catch (ObjectDisposedException ex) when (!cancellationToken.IsCancellationRequested && IsSessionManagerDisposed(ex))
        {
            return CreateSessionManagerDisposedFailure();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not ObjectDisposedException)
        {
            Trace.WriteLine($"ConnectTool secure transport initialization failed for process {processId}: {ex}");
            return new
            {
                success = false,
                error = "Failed to prepare secure transport artifacts. Check server logs for details.",
                errorCode = "SecureTransportInitializationFailed",
                targetIsElevated = processInfo.IsElevated,
                requiresElevationToConnect = access.RequiresElevationToConnect,
                canConnectFromCurrentServer = access.CanConnectFromCurrentServer
            };
        }
    }

    private async Task<object?> ProbeExistingInspectorHostAsync(
        int processId,
        ConnectTargetContext context,
        TimeSpan elapsedBeforeProbe,
        CancellationToken cancellationToken)
    {
        var existingHostProbeBudget = context.LikelySdkOnlyPackaging
            ? (context.IsRawInjectionTargetAllowed
                ? _connectTimeout
                : McpServerConfiguration.ExternalSdkHostReuseGracePeriod)
            : TimeSpan.FromMilliseconds(250);

        return await TryConnectToExistingInspectorHostAsync(
            processId,
            context.LikelySdkOnlyPackaging,
            elapsedBeforeProbe,
            existingHostProbeBudget,
            cancellationToken).ConfigureAwait(false);
    }

    private object CreateRawInjectionDeniedFailure(int processId, WpfProcessInfo processInfo)
    {
        var authorization = RawInjectionTargetPolicy.Authorize(processInfo);
        Trace.WriteLine($"ConnectTool raw injection denied process {processId}: executable={processInfo.ExecutablePath}");
        return new
        {
            success = false,
            error = authorization.Error,
            errorCode = authorization.ErrorCode,
            hint = authorization.Hint,
            requiresExplicitTargetOptIn = true,
            allowlistEnvVar = McpServerConfiguration.RawInjectionAllowedTargetsEnvVar
        };
    }

    private object? TryCreateInjectionRequest(
        int processId,
        ConnectTargetContext context,
        TimeSpan elapsedBeforeInjection,
        out InjectionRequest? injectionRequest)
    {
        injectionRequest = null;

        var validationError = context.LikelySdkOnlyPackaging
            ? InjectionError.SingleFileApplication
            : _injector.ValidateTarget(processId);
        if (validationError != InjectionError.None)
        {
            return CreateValidationFailure(processId, context, validationError);
        }

        var planFailure = TryBuildInjectionPlan(processId, context.ProcessInfo, out var request);
        if (planFailure != null)
        {
            return planFailure;
        }

        var timeoutFailure = ApplyRemainingInjectionBudget(
            elapsedBeforeInjection,
            request!,
            out injectionRequest);
        return timeoutFailure;
    }

    private object? TryBuildInjectionPlan(
        int processId,
        WpfProcessInfo processInfo,
        out InjectionRequest? injectionRequest)
    {
        var inspectorCandidates = _inspectorCandidateResolver(AppContext.BaseDirectory)
            .Where(File.Exists)
            .ToArray();
        var bootstrapperCandidates = _bootstrapperCandidateResolver(AppContext.BaseDirectory)
            .Where(File.Exists)
            .ToArray();

        injectionRequest = InjectionPlanFactory.CreateRequest(
            processInfo,
            inspectorCandidates,
            bootstrapperCandidates,
            authenticationSecretBase64: null,
            _sessionManager.GetCertificateDirectory());
        if (injectionRequest == null)
        {
            return new
            {
                success = false,
                error = "No matching Inspector or Bootstrapper DLL found for target process. " +
                    $"Runtime: {processInfo.Runtime}, Architecture: {processInfo.Architecture}",
                errorCode = "FileNotFound",
                hint = "Verify that the server was built for the correct architecture and that Inspector/Bootstrapper DLLs exist in the output directory."
            };
        }

        _dllPathValidator(injectionRequest.InspectorDllPath);
        _dllPathValidator(injectionRequest.BootstrapperDllPath);
        injectionRequest = injectionRequest.WithAuthenticationSecretBase64(
            _sessionManager.GetAuthenticationSecretBase64(processId, injectionRequest.ExpectedPipeName));
        return null;
    }

    private object? ApplyRemainingInjectionBudget(
        TimeSpan elapsedBeforeInjection,
        InjectionRequest request,
        out InjectionRequest? injectionRequest)
    {
        injectionRequest = null;

        var remainingConnectTimeoutBeforeInjection = GetRemainingPipeConnectTimeout(
            elapsedBeforeInjection,
            _connectTimeout);
        if (remainingConnectTimeoutBeforeInjection <= TimeSpan.Zero)
        {
            return new
            {
                success = false,
                error = "Connect timed out before bootstrap injection could start.",
                errorCode = "Timeout",
                hint = "The pre-injection discovery and validation phases consumed the full connect budget. Retry connect or target the process explicitly."
            };
        }

        injectionRequest = request.WithTotalTimeout(remainingConnectTimeoutBeforeInjection);
        return null;
    }

    private static object CreateValidationFailure(
        int processId,
        ConnectTargetContext context,
        InjectionError validationError)
    {
        return new
        {
            success = false,
            error = GetErrorMessage(validationError, processId, context.ProcessInfo),
            errorCode = validationError.ToString(),
            targetIsElevated = context.ProcessInfo.IsElevated,
            requiresElevationToConnect = context.Access.RequiresElevationToConnect,
            canConnectFromCurrentServer = context.Access.CanConnectFromCurrentServer
        };
    }

    private object? ExecuteBootstrapInjection(
        int processId,
        ConnectTargetContext context,
        InjectionRequest injectionRequest,
        CancellationToken cancellationToken,
        out string? pipeName)
    {
        pipeName = null;
        InjectionResult injectionResult;
        try
        {
            injectionResult = _sessionManager.ExecuteWithShutdownGuard(
                () => _injector.InjectWithBootstrap(injectionRequest, cancellationToken));
        }
        catch (ObjectDisposedException ex) when (!cancellationToken.IsCancellationRequested && IsSessionManagerDisposed(ex))
        {
            return CreateSessionManagerDisposedFailure();
        }

        if (!injectionResult.Success)
        {
            return CreateInjectionFailure(processId, context, injectionResult);
        }

        pipeName = string.IsNullOrWhiteSpace(injectionResult.PipeName)
            ? injectionRequest.ExpectedPipeName
            : injectionResult.PipeName;
        return null;
    }

    private static object CreateInjectionFailure(
        int processId,
        ConnectTargetContext context,
        InjectionResult injectionResult)
    {
        if (injectionResult.Error == InjectionError.AccessDenied && context.ProcessInfo.IsElevated)
        {
            return new
            {
                success = false,
                error = GetErrorMessage(InjectionError.AccessDenied, processId, context.ProcessInfo),
                errorCode = InjectionError.AccessDenied.ToString(),
                targetIsElevated = context.ProcessInfo.IsElevated,
                requiresElevationToConnect = context.Access.RequiresElevationToConnect,
                canConnectFromCurrentServer = context.Access.CanConnectFromCurrentServer,
                stage = injectionResult.FailedAtStage?.ToString()
            };
        }

        var injectionFailure = DescribeInjectionFailure(
            injectionResult,
            processId,
            context.ProcessInfo);
        return new
        {
            success = false,
            error = injectionFailure.Error,
            errorCode = injectionFailure.ErrorCode,
            stage = injectionResult.FailedAtStage?.ToString()
        };
    }

    private async Task<object> ConnectPipeHandshakeAsync(
        int processId,
        string? pipeName,
        TimeSpan elapsedBeforeHandshake,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ConnectPipeHandshakeCoreAsync(
                processId,
                pipeName,
                elapsedBeforeHandshake,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "ConnectTool cleanup triggered for process {0} after pipe handshake failure: {1}: {2}",
                processId,
                ex.GetType().Name,
                ex.Message);
            throw;
        }
    }

    private async Task<object> ConnectPipeHandshakeCoreAsync(
        int processId,
        string? pipeName,
        TimeSpan elapsedBeforeHandshake,
        CancellationToken cancellationToken)
    {
        var remainingPipeConnectTimeout = GetRemainingPipeConnectTimeout(
            elapsedBeforeHandshake,
            _connectTimeout);
        if (remainingPipeConnectTimeout <= TimeSpan.Zero)
        {
            return new
            {
                success = false,
                error = "Connect timed out before the final Inspector Named Pipe handshake could start",
                errorCode = "Timeout",
                hint = "The injection phase consumed the full timeout budget. Target process may be slow to load the Inspector DLL."
            };
        }

        NamedPipeConnectFailure pipeConnectFailure;
        try
        {
            pipeConnectFailure = await _connectInjectedSessionAsync(
                processId,
                pipeName,
                remainingPipeConnectTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex) when (!cancellationToken.IsCancellationRequested && IsSessionManagerDisposed(ex))
        {
            return CreateSessionManagerDisposedFailure();
        }
        catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return CreateSessionConnectionFailure(processId, ex);
        }

        if (pipeConnectFailure != NamedPipeConnectFailure.None)
        {
            var describedFailure = DescribePipeConnectFailure(pipeConnectFailure, processId);
            return new
            {
                success = false,
                error = describedFailure.Error,
                errorCode = describedFailure.ErrorCode,
                hint = describedFailure.Hint
            };
        }

        return ConnectOperationResult.FreshConnect;
    }

    private sealed record ConnectTargetContext(
        WpfProcessInfo ProcessInfo,
        ProcessConnectionAccess Access,
        bool LikelySdkOnlyPackaging,
        bool IsRawInjectionTargetAllowed);
}
