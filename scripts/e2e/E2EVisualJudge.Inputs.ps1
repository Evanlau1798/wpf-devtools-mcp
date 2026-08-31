Set-StrictMode -Version Latest

function Get-InputSha256 {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $stream = [System.IO.File]::OpenRead($Path)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToHexString($algorithm.ComputeHash($stream)).ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function Copy-FrozenInput {
    param(
        [string] $SourcePath,
        [string] $DestinationPath,
        [string] $Role
    )

    if (Test-Path -LiteralPath $DestinationPath) {
        throw "Frozen $Role input already exists. Use a fresh attempt evidence root."
    }
    [System.IO.File]::Copy($SourcePath, $DestinationPath, $false)
    $sourceHash = Get-InputSha256 $SourcePath
    $frozenHash = Get-InputSha256 $DestinationPath
    if ($sourceHash -cne $frozenHash) {
        throw "Frozen $Role input does not match its source SHA-256."
    }

    return [ordered]@{
        role = $Role
        frozenPath = "inputs/$Role.png"
        sha256 = $frozenHash
        byteLength = [System.IO.FileInfo]::new($DestinationPath).Length
    }
}

function Freeze-VisualJudgeInputs {
    param(
        [string] $EvidenceRoot,
        [string] $CandidatePath,
        [AllowNull()] [string] $ReferencePath
    )

    $inputsRoot = Join-Path $EvidenceRoot 'inputs'
    $mappingPath = Join-Path $EvidenceRoot 'visual-judge-inputs.json'
    if ((Test-Path -LiteralPath $inputsRoot) -or (Test-Path -LiteralPath $mappingPath)) {
        throw 'Visual judge frozen inputs already exist. Use a fresh attempt evidence root.'
    }
    [System.IO.Directory]::CreateDirectory($inputsRoot) | Out-Null

    $images = [System.Collections.Generic.List[object]]::new()
    $frozenReference = $null
    if (-not [string]::IsNullOrWhiteSpace($ReferencePath)) {
        $frozenReference = Join-Path $inputsRoot 'reference.png'
        $images.Add((Copy-FrozenInput $ReferencePath $frozenReference 'reference'))
    }
    $frozenCandidate = Join-Path $inputsRoot 'candidate.png'
    $images.Add((Copy-FrozenInput $CandidatePath $frozenCandidate 'candidate'))

    $mapping = [ordered]@{
        schemaVersion = 'wpfdevtools.e2e-visual-judge-inputs.v1'
        mode = if ($null -eq $frozenReference) { 'standalone' } else { 'reference' }
        images = @($images)
    }
    $options = [System.Text.Json.JsonSerializerOptions]::new()
    $options.WriteIndented = $true
    $json = [System.Text.Json.JsonSerializer]::Serialize($mapping, $mapping.GetType(), $options)
    [System.IO.File]::WriteAllText($mappingPath, $json, [System.Text.UTF8Encoding]::new($false))

    return [pscustomobject]@{
        ReferencePath = $frozenReference
        CandidatePath = $frozenCandidate
        MappingPath = $mappingPath
    }
}
