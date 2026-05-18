using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Inspector.Sdk;

public static partial class InspectorSdk
{
    private static bool CompleteInitializationIfShutdownRequested(
        ref InspectorHost? host,
        ref AuthenticationManager? authenticationManager,
        ref CertificateManager? certificateManager)
    {
        if (Volatile.Read(ref _shutdownRequestedDuringInitialization) == 0)
        {
            return false;
        }

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

        return true;
    }

    private static void RunDeferredShutdownIfRequested()
    {
        if (Interlocked.Exchange(ref _shutdownRequestedDuringInitialization, 0) != 0 && _isInitialized)
        {
            Shutdown();
        }
    }
}
