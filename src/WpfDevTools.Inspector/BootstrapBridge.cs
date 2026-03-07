using System.Runtime.InteropServices;
using System.Text;

namespace WpfDevTools.Inspector;

/// <summary>
/// Managed entrypoint called by the native bootstrapper.
/// Adapts CLR hosting API signatures to Bootstrap.Initialize().
///
/// .NET Framework: ExecuteInDefaultAppDomain calls Run(string) -> int
/// .NET 8: hostfxr calls RunNative(IntPtr, int) via [UnmanagedCallersOnly]
/// </summary>
public static class BootstrapBridge
{
    /// <summary>
    /// .NET Framework entrypoint.
    /// Signature: public static int Method(string) — required by ExecuteInDefaultAppDomain.
    /// </summary>
    /// <param name="parameters">Semicolon-delimited parameters from bootstrapper</param>
    /// <returns>0 on success, -1 on exception</returns>
    public static int Run(string parameters)
    {
        try
        {
            Bootstrap.Initialize(parameters);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// .NET 8+ entrypoint.
    /// Called via hostfxr load_assembly_and_get_function_pointer with UNMANAGEDCALLERSONLY_METHOD.
    /// </summary>
    /// <param name="args">Pointer to UTF-8 encoded parameters buffer</param>
    /// <param name="sizeBytes">Byte count excluding null terminator</param>
    /// <returns>0 on success, -1 on exception</returns>
    [UnmanagedCallersOnly]
    public static int RunNative(IntPtr args, int sizeBytes)
    {
        try
        {
            string parameters;
            if (args == IntPtr.Zero || sizeBytes <= 0)
            {
                parameters = string.Empty;
            }
            else
            {
                unsafe
                {
                    parameters = Encoding.UTF8.GetString(
                        (byte*)args, sizeBytes);
                }
            }

            Bootstrap.Initialize(parameters);
            return 0;
        }
        catch
        {
            return -1;
        }
    }
#endif
}
