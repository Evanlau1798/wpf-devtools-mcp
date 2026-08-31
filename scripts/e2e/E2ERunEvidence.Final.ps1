Set-StrictMode -Version Latest

function Get-RunnerCompleted {
    param([System.Text.Json.JsonElement] $Root)

    $runner = Get-JsonProperty $Root 'runner'
    return (Get-JsonBoolean $runner 'completed') -and (Get-JsonInteger $runner 'exitCode') -eq 0
}

function Assert-RunnerEvents {
    param([System.Text.Json.JsonElement] $Root, [hashtable] $Artifacts)

    $runner = Get-JsonProperty $Root 'runner'
    $eventsPath = Get-ArtifactPath $Artifacts (Get-JsonString $runner 'eventsArtifactId')
    $bytes = [System.IO.File]::ReadAllBytes($eventsPath)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw 'Runner JSONL must use UTF-8 without a BOM.'
    }
    try {
        $text = [System.Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    }
    catch [System.Text.DecoderFallbackException] {
        throw "Runner JSONL must contain strict UTF-8: $($_.Exception.Message)"
    }

    $lineNumber = 0
    $terminalLine = 0
    $terminalEvent = $null
    $reader = [System.IO.StringReader]::new($text)
    while ($null -ne ($line = $reader.ReadLine())) {
        $lineNumber++
        if ([string]::IsNullOrWhiteSpace($line)) {
            throw "Runner JSONL line $lineNumber is blank."
        }
        try {
            $document = [System.Text.Json.JsonDocument]::Parse($line)
            try {
                if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
                    throw 'event must be a JSON object'
                }
                $type = Get-JsonString $document.RootElement 'type'
                if ($type -ceq 'run.completed') {
                    if ($null -ne $terminalEvent) {
                        throw 'runner stream contains more than one terminal completion event'
                    }
                    $terminalEvent = $document.RootElement.Clone()
                    $terminalLine = $lineNumber
                }
            }
            finally {
                $document.Dispose()
            }
        }
        catch {
            throw "Runner JSONL line $lineNumber is invalid: $($_.Exception.Message)"
        }
    }
    if ($lineNumber -eq 0) {
        throw 'Runner JSONL must contain at least one event.'
    }
    if ($null -eq $terminalEvent -or $terminalLine -ne $lineNumber) {
        throw 'Runner JSONL must end with exactly one terminal run.completed event.'
    }
    $completed = Get-JsonBoolean $terminalEvent 'completed'
    $exitCode = Get-JsonInteger $terminalEvent 'exitCode'
    if ($completed -ne (Get-JsonBoolean $runner 'completed') -or
        $exitCode -ne (Get-JsonInteger $runner 'exitCode')) {
        throw 'Runner JSONL terminal status does not match the manifest runner status.'
    }
}

function Get-ArtifactRelativePath {
    param([System.Text.Json.JsonElement] $Root, [string] $Id)

    foreach ($artifact in (Get-JsonArray $Root 'artifacts').EnumerateArray()) {
        if ((Get-JsonString $artifact 'id') -ceq $Id) {
            return Get-JsonString $artifact 'path'
        }
    }
    throw "Referenced artifact '$Id' is not declared."
}

function Assert-ReportAndCleanup {
    param([System.Text.Json.JsonElement] $Root, [hashtable] $Artifacts)

    $cleanup = Get-JsonProperty $Root 'cleanup'
    Assert-TrueField $cleanup 'passed' 'Final cleanup gate'
    Assert-ArtifactReference $cleanup 'artifactId' $Artifacts

    $report = Get-JsonProperty $Root 'report'
    $reportPath = Get-ArtifactPath $Artifacts (Get-JsonString $report 'artifactId')
    $reportedImages = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($imageId in (Get-JsonArray $report 'imageArtifactIds').EnumerateArray()) {
        $id = $imageId.GetString()
        if ([string]::IsNullOrWhiteSpace($id) -or -not $reportedImages.Add($id)) {
            throw 'Final report imageArtifactIds must contain unique non-empty ids.'
        }
        Get-ArtifactPath $Artifacts $id | Out-Null
    }

    $requiredImages = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($attempt in (Get-JsonArray $Root 'attempts').EnumerateArray()) {
        $requiredImages.Add((Get-JsonString $attempt 'referenceArtifactId')) | Out-Null
        $requiredImages.Add((Get-JsonString $attempt 'candidateArtifactId')) | Out-Null
    }
    $reportText = [System.IO.File]::ReadAllText($reportPath)
    foreach ($id in $requiredImages) {
        if (-not $reportedImages.Contains($id)) {
            throw "Final report image list is missing '$id'."
        }
        $relativePath = Get-ArtifactRelativePath $Root $id
        if ($reportText.IndexOf($relativePath, [StringComparison]::Ordinal) -lt 0) {
            throw "Final report does not reference required image '$relativePath'."
        }
    }
}

function Assert-InputMappings {
    param([System.Text.Json.JsonElement] $Root, [hashtable] $Artifacts)

    foreach ($attempt in (Get-JsonArray $Root 'attempts').EnumerateArray()) {
        $number = Get-JsonInteger $attempt 'number'
        $mappingId = Get-JsonString $attempt 'imageMappingArtifactId'
        $mappingRelativePath = Get-ArtifactRelativePath $Root $mappingId
        if (-not $mappingRelativePath.Replace('\', '/').StartsWith(
                "attempts/$number/",
                [StringComparison]::Ordinal)) {
            throw "Attempt $number image mapping must be attempt-local."
        }

        $document = [System.Text.Json.JsonDocument]::Parse(
            [System.IO.File]::ReadAllText((Get-ArtifactPath $Artifacts $mappingId)))
        try {
            $mapping = $document.RootElement
            if ((Get-JsonString $mapping 'schemaVersion') -cne 'wpfdevtools.e2e-visual-judge-inputs.v1' -or
                (Get-JsonString $mapping 'mode') -cne 'reference') {
                throw "Attempt $number image mapping has an invalid schema or mode."
            }
            $images = @((Get-JsonArray $mapping 'images').EnumerateArray())
            if ($images.Count -ne 2) {
                throw "Attempt $number image mapping must contain reference and candidate images."
            }

            $byRole = @{}
            foreach ($image in $images) {
                $role = Get-JsonString $image 'role'
                if ($role -notin @('reference', 'candidate') -or $byRole.ContainsKey($role)) {
                    throw "Attempt $number image mapping contains invalid or duplicate roles."
                }
                $byRole[$role] = $image
            }
            foreach ($role in @('reference', 'candidate')) {
                if (-not $byRole.ContainsKey($role)) {
                    throw "Attempt $number image mapping is missing '$role'."
                }
                $artifactField = if ($role -ceq 'reference') { 'referenceArtifactId' } else { 'candidateArtifactId' }
                $artifactId = Get-JsonString $attempt $artifactField
                $artifactPath = Get-ArtifactPath $Artifacts $artifactId
                $relativePath = Get-ArtifactRelativePath $Root $artifactId
                $image = [System.Text.Json.JsonElement] $byRole[$role]
                $sourceId = Get-JsonString $image 'sourceArtifactId'
                $sourcePath = Get-ArtifactPath $Artifacts $sourceId
                $viewport = Get-JsonProperty $Root 'viewport'
                $expectedSourceId = if ($role -ceq 'reference') {
                    Get-JsonString $viewport 'referenceArtifactId'
                }
                elseif ($number -eq 1) {
                    Get-JsonString $viewport 'candidateArtifactId'
                }
                else { $sourceId }
                $lengthValue = Get-JsonProperty $image 'byteLength'
                $byteLength = 0L
                if ($lengthValue.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
                    -not $lengthValue.TryGetInt64([ref] $byteLength) -or
                    $byteLength -ne [System.IO.FileInfo]::new($artifactPath).Length -or
                    (Get-JsonString $image 'frozenPath') -cne "inputs/$role.png" -or
                    $sourceId -cne $expectedSourceId -or
                    -not (Get-Sha256 $sourcePath).Equals((Get-Sha256 $artifactPath), [StringComparison]::OrdinalIgnoreCase) -or
                    -not (Get-JsonString $image 'sha256').Equals(
                        (Get-Sha256 $artifactPath),
                        [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Attempt $number '$role' image mapping does not match its source or frozen artifact."
                }
                if (-not $relativePath.Replace('\', '/').StartsWith(
                        "attempts/$number/inputs/",
                        [StringComparison]::Ordinal)) {
                    throw "Attempt $number '$role' image must be an attempt-local frozen copy."
                }
            }
        }
        finally {
            $document.Dispose()
        }
    }
}

function Get-AxisMinimum {
    param([System.Text.Json.JsonElement] $Axes, [string[]] $Names)

    $minimum = 10.0
    foreach ($name in $Names) {
        $value = Get-JsonProperty $Axes $name
        $score = 0.0
        if ($value.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
            -not $value.TryGetDouble([ref] $score) -or $score -lt 0 -or $score -gt 10) {
            throw "Visual judge axis '$name' must be between 0 and 10."
        }
        $minimum = [Math]::Min($minimum, $score)
    }
    return $minimum
}

function Test-JudgeAttemptQualification {
    param(
        [System.Text.Json.JsonElement] $Attempt,
        [hashtable] $Artifacts,
        [System.Collections.Generic.List[string]] $Reasons,
        [switch] $RecordReasons
    )

    $number = Get-JsonInteger $Attempt 'number'
    $resultPath = Get-ArtifactPath $Artifacts (Get-JsonString $Attempt 'judgeResultArtifactId')
    $document = [System.Text.Json.JsonDocument]::Parse([System.IO.File]::ReadAllText($resultPath))
    try {
        $result = $document.RootElement
        if ((Get-JsonString $result 'mode') -cne 'reference') {
            throw "Visual judge attempt $number must use reference mode."
        }
        Get-JsonString $result 'summary' | Out-Null
        $quality = Get-AxisMinimum (Get-JsonProperty $result 'qualityAxes') @(
            'layoutBalance', 'visualHierarchy', 'readabilityContrast', 'controlStateCoherence', 'visualPolish')
        $fidelity = Get-AxisMinimum (Get-JsonProperty $result 'referenceAxes') @(
            'regionGeometry', 'densityRhythm', 'navigationBrowseRhythm', 'mediaCardComposition')
        $severityCap = 10.0
        foreach ($defect in (Get-JsonArray $result 'defects').EnumerateArray()) {
            $severity = Get-JsonString $defect 'severity'
            $severityCap = switch ($severity) {
                'blocking' { [Math]::Min($severityCap, 9.0); break }
                'material' { [Math]::Min($severityCap, 9.5); break }
                'minor' { $severityCap; break }
                default { throw "Unsupported visual defect severity '$severity'." }
            }
        }
        $quality = [Math]::Min($quality, $severityCap)
        $fidelity = [Math]::Min($fidelity, $severityCap)
        if ($RecordReasons) {
            if ($quality -le 9.5) {
                $Reasons.Add("visualQuality=$quality is not strictly greater than 9.5")
            }
            if ($fidelity -le 9.5) {
                $Reasons.Add("referenceFidelity=$fidelity is not strictly greater than 9.5")
            }
        }
        return $quality -gt 9.5 -and $fidelity -gt 9.5
    }
    finally {
        $document.Dispose()
    }
}

function Test-VisualQualification {
    param(
        [System.Text.Json.JsonElement] $Root,
        [hashtable] $Artifacts,
        [System.Collections.Generic.List[string]] $Reasons
    )

    $attempts = @((Get-JsonArray $Root 'attempts').EnumerateArray())
    if ($attempts.Count -eq 1) {
        return Test-JudgeAttemptQualification $attempts[0] $Artifacts $Reasons -RecordReasons
    }

    $firstQualified = Test-JudgeAttemptQualification $attempts[0] $Artifacts $Reasons
    if ($firstQualified) {
        throw 'Visual judge attempt 2 is invalid because attempt 1 already qualified.'
    }
    return Test-JudgeAttemptQualification $attempts[1] $Artifacts $Reasons -RecordReasons
}

function Write-FinalDecision {
    param(
        [string] $Path,
        [bool] $RunnerCompleted,
        [bool] $OperationalGatesPassed,
        [bool] $VisualQualified,
        [System.Collections.Generic.List[string]] $Reasons,
        [bool] $RepairBudgetExhausted
    )

    $decision = [ordered]@{
        runnerCompleted = $RunnerCompleted
        operationalGatesPassed = $OperationalGatesPassed
        visualQualified = $VisualQualified
        overallResult = if ($RunnerCompleted -and $OperationalGatesPassed -and $VisualQualified) { 'PASS' } else { 'FAIL' }
        reasons = @($Reasons)
        repairBudgetExhausted = $RepairBudgetExhausted
    }
    $options = [System.Text.Json.JsonSerializerOptions]::new()
    $options.WriteIndented = $true
    $json = [System.Text.Json.JsonSerializer]::Serialize($decision, $decision.GetType(), $options)
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
    [Console]::Out.WriteLine($json)
}
