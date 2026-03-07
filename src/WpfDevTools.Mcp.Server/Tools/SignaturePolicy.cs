namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Pure logic for DLL signature verification policy decisions.
/// Extracted from ConnectTool to enable comprehensive unit testing
/// of all policy branches without conditional compilation dependencies.
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
    /// </summary>
    /// <param name="isDebugBuild">Whether this is a DEBUG build</param>
    /// <param name="isTrustedRoot">Whether the DLL is under a trusted root (app dir or solution root)</param>
    /// <param name="hasSkipEnvVar">Whether WPFDEVTOOLS_SKIP_SIGNATURE_CHECK=1 is set</param>
    /// <param name="isCi">Whether running in CI (CI or TF_BUILD env var is set)</param>
    /// <returns>Whether to verify or skip signature verification</returns>
    public static Action Evaluate(bool isDebugBuild, bool isTrustedRoot, bool hasSkipEnvVar, bool isCi)
    {
        // RELEASE builds ALWAYS verify - no exceptions
        if (!isDebugBuild) return Action.Verify;

        // DEBUG + trusted root: auto-skip for frictionless local development
        if (isTrustedRoot) return Action.Skip;

        // DEBUG + untrusted root + env var bypass (not in CI)
        if (hasSkipEnvVar && !isCi) return Action.Skip;

        // All other cases: verify
        return Action.Verify;
    }
}
