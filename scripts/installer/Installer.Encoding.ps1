if (-not (Get-Command Get-InstallerHardLinkCount -ErrorAction SilentlyContinue)) {
    function Get-InstallerHardLinkCount {
        param([Parameter(Mandatory)] [string]$Path)

        if ($PSVersionTable.PSVersion.Major -lt 5) {
            return 1
        }

        if (-not ('WpfDevToolsInstallerFileIdentity' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class WpfDevToolsInstallerFileIdentity
{
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out BY_HANDLE_FILE_INFORMATION fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    public static uint GetHardLinkCount(string path)
    {
        using (SafeFileHandle handle = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
        {
            if (handle.IsInvalid)
            {
                throw new IOException("Failed to open installer path for identity validation.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }

            BY_HANDLE_FILE_INFORMATION fileInformation;
            if (!GetFileInformationByHandle(handle, out fileInformation))
            {
                throw new IOException("Failed to read installer path identity metadata.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }

            return fileInformation.NumberOfLinks;
        }
    }
}
'@
        }

        return [WpfDevToolsInstallerFileIdentity]::GetHardLinkCount($Path)
    }
}

function Write-InstallerUtf8NoBomFile {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [AllowEmptyString()] [string]$Content
    )

    $resolvedPath = if (Get-Command Assert-InstallerLocalPathTrusted -ErrorAction SilentlyContinue) {
        Assert-InstallerLocalPathTrusted -Path $Path
    }
    else {
        $Path
    }

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        $trustedDirectory = if (Get-Command Assert-InstallerLocalPathTrusted -ErrorAction SilentlyContinue) {
            Assert-InstallerLocalPathTrusted -Path $directory
        }
        else {
            $directory
        }

        New-Item -ItemType Directory -Force -Path $trustedDirectory | Out-Null
        if (Get-Command Assert-InstallerLocalPathTrusted -ErrorAction SilentlyContinue) {
            Assert-InstallerLocalPathTrusted -Path $trustedDirectory | Out-Null
        }
    }

    if (Get-Command Assert-InstallerLocalPathTrusted -ErrorAction SilentlyContinue) {
        $resolvedPath = Assert-InstallerLocalPathTrusted -Path $resolvedPath -RejectHardLinks
    }

    $utf8Encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($resolvedPath, [string]$Content, $utf8Encoding)
}
