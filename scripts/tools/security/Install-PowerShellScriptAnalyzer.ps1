[CmdletBinding()]
param(
    [string]$RequiredVersion = '1.25.0'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$ConfirmPreference = 'None'

function Resolve-ScriptAnalyzerModule {
    param([Parameter(Mandatory = $true)] [version]$Version)

    Get-Module -ListAvailable PSScriptAnalyzer |
        Where-Object { $_.Version -eq $Version } |
        Sort-Object ModuleBase |
        Select-Object -First 1
}

function Assert-ScriptAnalyzerModule {
    param(
        [Parameter(Mandatory = $true)] [object]$Module,
        [Parameter(Mandatory = $true)] [version]$Version
    )

    if ($null -eq $Module -or [string]::IsNullOrWhiteSpace([string]$Module.Path)) {
        throw "PSScriptAnalyzer $Version was not found after installation."
    }

    $manifest = Test-ModuleManifest -Path $Module.Path -ErrorAction Stop
    if ($manifest.Name -ne 'PSScriptAnalyzer') {
        throw "Unexpected module manifest name '$($manifest.Name)' at $($Module.Path)."
    }

    if ($manifest.Version -ne $Version) {
        throw "Unexpected PSScriptAnalyzer version '$($manifest.Version)' at $($Module.Path); expected $Version."
    }

    if (-not $manifest.ExportedCommands.ContainsKey('Invoke-ScriptAnalyzer')) {
        throw "PSScriptAnalyzer manifest at $($Module.Path) does not export Invoke-ScriptAnalyzer."
    }
}

$requiredVersionValue = [version]$RequiredVersion
$module = Resolve-ScriptAnalyzerModule -Version $requiredVersionValue
if ($null -eq $module) {
    Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Scope CurrentUser -Force -Confirm:$false -ErrorAction Stop | Out-Null
    Install-Module PSScriptAnalyzer -Scope CurrentUser -Force -AcceptLicense -Confirm:$false -RequiredVersion $RequiredVersion -Repository PSGallery -ErrorAction Stop
    $module = Resolve-ScriptAnalyzerModule -Version $requiredVersionValue
}

Assert-ScriptAnalyzerModule -Module $module -Version $requiredVersionValue
Write-Host "Verified PSScriptAnalyzer $RequiredVersion module manifest: $($module.Path)"
