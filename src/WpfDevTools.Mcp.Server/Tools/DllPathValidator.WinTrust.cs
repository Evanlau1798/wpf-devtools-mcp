using System.Runtime.InteropServices;

namespace WpfDevTools.Mcp.Server.Tools;

internal static partial class DllPathValidator
{
    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    private static void VerifyFileAuthenticodeTrust(string filePath)
    {
        var fileInfo = new WinTrustFileInfo(filePath);
        var fileInfoPointer = IntPtr.Zero;

        try
        {
            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);

            var trustData = new WinTrustData(fileInfoPointer);
            var result = WinVerifyTrustOverrideForTesting?.Invoke(filePath)
                ?? WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref trustData);
            if (result != 0)
            {
                throw new System.Security.Cryptography.CryptographicException(
                    $"Authenticode verification failed for '{filePath}' (WinVerifyTrust HRESULT: 0x{result:X8}).");
            }
        }
        finally
        {
            if (fileInfoPointer != IntPtr.Zero)
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
                Marshal.FreeCoTaskMem(fileInfoPointer);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private readonly struct WinTrustFileInfo
    {
        private readonly uint _cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        [MarshalAs(UnmanagedType.LPWStr)]
        private readonly string _filePath = string.Empty;
        private readonly IntPtr _fileHandle = IntPtr.Zero;
        private readonly IntPtr _knownSubject = IntPtr.Zero;

        public WinTrustFileInfo(string filePath)
        {
            _filePath = filePath;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private readonly struct WinTrustData
    {
        private const uint UiChoiceNone = 2;
        private const uint RevocationChecksNone = 0;
        private const uint ChoiceFile = 1;
        private const uint StateActionIgnore = 0;
        private const uint ProviderFlagsSafer = 0x00000100;
        private const uint UiContextExecute = 0;

        private readonly uint _cbStruct = (uint)Marshal.SizeOf<WinTrustData>();
        private readonly IntPtr _policyCallbackData = IntPtr.Zero;
        private readonly IntPtr _sipClientData = IntPtr.Zero;
        private readonly uint _uiChoice = UiChoiceNone;
        private readonly uint _revocationChecks = RevocationChecksNone;
        private readonly uint _unionChoice = ChoiceFile;
        private readonly IntPtr _fileInfoPointer = IntPtr.Zero;
        private readonly uint _stateAction = StateActionIgnore;
        private readonly IntPtr _stateData = IntPtr.Zero;
        private readonly IntPtr _urlReference = IntPtr.Zero;
        private readonly uint _providerFlags = ProviderFlagsSafer;
        private readonly uint _uiContext = UiContextExecute;

        public WinTrustData(IntPtr fileInfoPointer)
        {
            _fileInfoPointer = fileInfoPointer;
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        ref WinTrustData trustData);
}
