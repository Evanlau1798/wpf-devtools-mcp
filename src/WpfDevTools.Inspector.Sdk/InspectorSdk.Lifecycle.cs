using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Inspector.Sdk;

public static partial class InspectorSdk
{
    private static bool DisposeStartedHostIfShutdownRequested(
        ref InspectorHost? host,
        ref AuthenticationManager? authenticationManager,
        ref CertificateManager? certificateManager)
    {
        lock (LifecycleLock)
        {
            if (Volatile.Read(ref _shutdownRequestedDuringInitialization) == 0)
            {
                return false;
            }
        }

        DisposeUnpublishedHost(ref host, ref authenticationManager, ref certificateManager);
        return true;
    }

    private static bool TryPublishInitializedHost(
        int processId,
        ref InspectorHost? host,
        ref AuthenticationManager? authenticationManager,
        ref CertificateManager? certificateManager)
    {
        lock (LifecycleLock)
        {
            if (Volatile.Read(ref _shutdownRequestedDuringInitialization) != 0)
            {
                return false;
            }

            _authenticationManager = authenticationManager;
            _certificateManager = certificateManager;
            _host = host;
            _isInitialized = true;
            LastInitializationStatus = CreateInitializedStatus(processId);
            return true;
        }
    }

    private static bool TryRecordShutdownDuringInitialization()
    {
        lock (LifecycleLock)
        {
            if (!_isInitialized && Volatile.Read(ref _isInitializing) != 0)
            {
                RecordShutdownRequestedDuringInitialization();
                return true;
            }

            return false;
        }
    }

    private static bool IsAlreadyInitialized()
    {
        lock (LifecycleLock)
        {
            return _isInitialized;
        }
    }

    private static void ClearStaleDeferredShutdownRequestIfIdle()
    {
        lock (LifecycleLock)
        {
            if (!_isInitialized && Volatile.Read(ref _isInitializing) == 0)
            {
                Interlocked.Exchange(ref _shutdownRequestedDuringInitialization, 0);
            }
        }
    }

    private static void RecordShutdownRequestedDuringInitialization()
        => Interlocked.Exchange(ref _shutdownRequestedDuringInitialization, 1);

    private static void FinishShutdownOperation()
    {
        lock (LifecycleLock)
        {
            Interlocked.Exchange(ref _shutdownRequestedDuringInitialization, 0);
            Interlocked.Exchange(ref _isInitializing, 0);
        }
    }

    private static bool TryTakePublishedHostForShutdown(
        out InspectorHost? host,
        out AuthenticationManager? authenticationManager)
    {
        lock (LifecycleLock)
        {
            if (!_isInitialized)
            {
                host = null;
                authenticationManager = null;
                return false;
            }

            host = _host;
            authenticationManager = _authenticationManager;

            _host = null;
            _authenticationManager = null;
            _certificateManager = null;
            _isInitialized = false;
            Interlocked.Exchange(ref _shutdownRequestedDuringInitialization, 0);
            LastInitializationStatus = CreateNotStartedStatus();

            return true;
        }
    }

    private static void DisposeUnpublishedHost(
        ref InspectorHost? host,
        ref AuthenticationManager? authenticationManager,
        ref CertificateManager? certificateManager)
    {
        try
        {
            CleanupHostResources(host, authenticationManager);
        }
        finally
        {
            host = null;
            authenticationManager = null;
            certificateManager = null;
            LastInitializationStatus = CreateNotStartedStatus();
        }
    }

    private static void RunDeferredShutdownIfRequested()
    {
        if (Interlocked.Exchange(ref _shutdownRequestedDuringInitialization, 0) != 0 && _isInitialized)
        {
            Shutdown();
        }
    }
}
