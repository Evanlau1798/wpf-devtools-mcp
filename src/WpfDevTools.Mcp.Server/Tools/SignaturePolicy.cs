using System.Security.Cryptography.X509Certificates;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Pure logic for DLL signature verification policy decisions.
/// Extracted from ConnectTool to enable comprehensive unit testing.
///
/// Security model: trusted-root-only.
/// Path validation (trusted root check) is enforced by ConnectTool.ValidateDllPath()
/// BEFORE this policy is consulted. This policy only decides whether to perform
/// Authenticode signature verification for DLLs already validated as being
/// within trusted roots.
/// </summary>
public static class SignaturePolicy
{
    /// <summary>
    /// The action to take for signature verification
    /// </summary>
    public enum Action
    {
        /// <summary>Perform full Authenticode signature verification</summary>
        Verify,
        /// <summary>Skip signature verification (trusted context)</summary>
        Skip
    }

    /// <summary>
    /// Evaluate the signature verification policy for a DLL.
    /// Called only after path validation confirms the DLL is under a trusted root.
    /// </summary>
    /// <param name="isDebugBuild">Whether this is a DEBUG build</param>
    /// <returns>Whether to verify or skip signature verification</returns>
    public static Action Evaluate(bool isDebugBuild)
    {
        // RELEASE builds ALWAYS verify - no exceptions
        if (!isDebugBuild) return Action.Verify;

        // DEBUG builds skip verification for trusted root DLLs
        // (path already validated as trusted root by caller)
        return Action.Skip;
    }

    /// <summary>
    /// Determine the certificate revocation check mode based on build configuration.
    /// Debug uses Offline to prevent network blocking during development.
    /// Release uses Online for maximum security.
    /// </summary>
    /// <param name="isDebugBuild">Whether this is a DEBUG build</param>
    /// <returns>The revocation mode to use for certificate chain validation</returns>
    public static X509RevocationMode GetRevocationMode(bool isDebugBuild)
    {
        return isDebugBuild ? X509RevocationMode.Offline : X509RevocationMode.Online;
    }
}
