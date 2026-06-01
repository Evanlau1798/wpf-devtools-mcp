param(
    [string]$CertificateBase64 = $env:WPFDEVTOOLS_RELEASE_CERTIFICATE_BASE64,
    [string]$CertificatePath = '',
    [string]$GitHubEnvPath = $env:GITHUB_ENV
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

function Set-ReleaseSigningCertificateFileSecurity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [System.Security.AccessControl.FileSecurity]$FileSecurity
    )

    $fileSetAccessControl = [System.IO.File].GetMethod(
        'SetAccessControl',
        [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static,
        $null,
        [Type[]]@([string], [System.Security.AccessControl.FileSecurity]),
        $null)
    if ($null -ne $fileSetAccessControl) {
        $fileSetAccessControl.Invoke($null, @($Path, $FileSecurity))
        return
    }

    try {
        [System.Reflection.Assembly]::Load('System.IO.FileSystem.AccessControl') | Out-Null
    }
    catch {
    }

    $extensionType = [Type]::GetType(
        'System.IO.FileSystemAclExtensions, System.IO.FileSystem.AccessControl',
        $false)
    if ($null -ne $extensionType) {
        $fileInfo = [System.IO.FileInfo]::new($Path)
        $extensionSetAccessControl = $extensionType.GetMethods(
            [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static) |
            Where-Object {
                $_.Name -eq 'SetAccessControl' -and
                $_.GetParameters().Count -eq 2 -and
                $_.GetParameters()[0].ParameterType.FullName -eq 'System.IO.FileInfo'
            } |
            Select-Object -First 1

        if ($null -ne $extensionSetAccessControl) {
            $extensionSetAccessControl.Invoke($null, @($fileInfo, $FileSecurity))
            return
        }
    }

    throw 'Could not locate a .NET file ACL API for release signing certificate hardening.'
}

function Protect-ReleaseSigningCertificateFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    if ($null -eq $currentUser) {
        throw 'Could not resolve the current Windows user SID for release signing certificate ACL hardening.'
    }

    $systemSid = New-Object System.Security.Principal.SecurityIdentifier `
        ([System.Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
    $administratorsSid = New-Object System.Security.Principal.SecurityIdentifier `
        ([System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)

    $fileSecurity = New-Object System.Security.AccessControl.FileSecurity
    $fileSecurity.SetOwner($currentUser)
    $fileSecurity.SetAccessRuleProtection($true, $false)

    $rights = [System.Security.AccessControl.FileSystemRights]::FullControl
    $allow = [System.Security.AccessControl.AccessControlType]::Allow
    foreach ($identity in @($currentUser, $systemSid, $administratorsSid)) {
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, $rights, $allow)
        $fileSecurity.AddAccessRule($rule)
    }

    Set-ReleaseSigningCertificateFileSecurity -Path $Path -FileSecurity $fileSecurity
}

if ([string]::IsNullOrWhiteSpace($CertificateBase64)) {
    throw 'GitHub Release packaging requires WPFDEVTOOLS_RELEASE_CERTIFICATE_BASE64.'
}

if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    $tempRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
        [System.IO.Path]::GetTempPath()
    }
    else {
        $env:RUNNER_TEMP
    }

    $CertificatePath = Join-Path $tempRoot 'wpf-devtools-release-signing.pfx'
}

$certificateBytes = $null
try {
    $certificateBytes = [System.Convert]::FromBase64String($CertificateBase64)
    $certificateDirectory = [System.IO.Path]::GetDirectoryName($CertificatePath)
    if (-not [string]::IsNullOrWhiteSpace($certificateDirectory) -and
        -not (Test-Path -LiteralPath $certificateDirectory)) {
        New-Item -ItemType Directory -Path $certificateDirectory -Force | Out-Null
    }

    [System.IO.File]::WriteAllBytes($CertificatePath, $certificateBytes)
    Protect-ReleaseSigningCertificateFile -Path $CertificatePath

    if (-not [string]::IsNullOrWhiteSpace($GitHubEnvPath)) {
        "WPFDEVTOOLS_RELEASE_CERTIFICATE_PATH=$CertificatePath" |
            Out-File -FilePath $GitHubEnvPath -Append -Encoding utf8
    }
}
finally {
    if ($null -ne $certificateBytes) {
        [Array]::Clear($certificateBytes, 0, $certificateBytes.Length)
    }
}
