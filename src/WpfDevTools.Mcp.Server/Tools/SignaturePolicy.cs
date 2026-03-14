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
    /// <param name="isTrustedLocalDevelopmentBuild">Whether this is a trusted non-Release workspace build used for local development</param>
    /// <returns>Whether to verify or skip signature verification</returns>
    public static Action Evaluate(
        bool isDebugBuild,
        bool isTrustedLocalDevelopmentBuild = false)
    {
        if (isDebugBuild || isTrustedLocalDevelopmentBuild)
        {
            return Action.Skip;
        }

        return Action.Verify;
    }

    /// <summary>
    /// Determine the certificate revocation check mode based on build configuration.
    /// Debug uses Offline to prevent network blocking during development.
    /// Release uses Online for maximum security.
    /// </summary>
    /// <param name="isDebugBuild">Whether this is a DEBUG build</param>
    /// <param name="isTrustedLocalDevelopmentBuild">Whether this is a trusted non-Release workspace build used for local development</param>
    /// <returns>The revocation mode to use for certificate chain validation</returns>
    public static X509RevocationMode GetRevocationMode(
        bool isDebugBuild,
        bool isTrustedLocalDevelopmentBuild = false)
    {
        return Evaluate(isDebugBuild, isTrustedLocalDevelopmentBuild) == Action.Skip
            ? X509RevocationMode.Offline
            : X509RevocationMode.Online;
    }
}
