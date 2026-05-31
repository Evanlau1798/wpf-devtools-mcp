param(
    [string]$CertificateBase64 = $env:WPFDEVTOOLS_RELEASE_CERTIFICATE_BASE64,
    [string]$CertificatePath = '',
    [string]$GitHubEnvPath = $env:GITHUB_ENV
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

function Set-ReleaseSigningCertificateAcl {
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

    Set-Acl -LiteralPath $Path -AclObject $fileSecurity
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
    Set-ReleaseSigningCertificateAcl -Path $CertificatePath

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
