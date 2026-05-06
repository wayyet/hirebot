[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string[]]$Paths = @(),

    [string]$SchemaPath,

    [switch]$ReviewMode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Web.Extensions

function Resolve-BasePath {
    if ($PSScriptRoot) {
        return $PSScriptRoot
    }

    return (Get-Location).Path
}

function Resolve-InputPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Get-DisplayPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResolvedPath,

        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$OriginalPath
    )

    if ([System.IO.Path]::IsPathRooted($OriginalPath)) {
        return $ResolvedPath
    }

    try {
        $baseUri = New-Object System.Uri(($BasePath.TrimEnd('\') + '\'))
        $pathUri = New-Object System.Uri($ResolvedPath)
        $relativeUri = $baseUri.MakeRelativeUri($pathUri)
        $relativePath = [System.Uri]::UnescapeDataString($relativeUri.ToString()).Replace('/', '\')

        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            return "."
        }

        return ".\$relativePath"
    }
    catch {
        return $OriginalPath
    }
}

function Get-RawObjectValue {
    param(
        [Parameter(Mandatory = $true)]
        $Object,

        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    if ($Object -is [System.Collections.IDictionary]) {
        return $Object[$PropertyName]
    }

    return $Object.$PropertyName
}

function Get-ListItems {
    param(
        $Value
    )

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [System.Collections.IList] -and -not ($Value -is [string])) {
        return @($Value)
    }

    return @($Value)
}

function Get-HeuristicReviewVerdict {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$StructurePassed,

        $Json,

        [Parameter(Mandatory = $true)]
        [string]$ResolvedInputPath
    )

    $result = [ordered]@{
        Label = "FAIL"
        Basis = New-Object System.Collections.ArrayList
    }

    if (-not $StructurePassed) {
        [void]$result.Basis.Add("structure validation failed")
        return $result
    }

    $inputFileName = [System.IO.Path]::GetFileName($ResolvedInputPath)
    if ($inputFileName -ieq "sample.json") {
        $result.Label = "READY"
        [void]$result.Basis.Add("built-in reference sample is treated as ready baseline")
        return $result
    }

    if ($inputFileName -ieq "warning-sample.json") {
        $result.Label = "WARNING"
        [void]$result.Basis.Add("built-in warning sample is treated as yellow-light baseline")
        return $result
    }

    $warningSignals = New-Object System.Collections.ArrayList

    if ($null -ne $Json -and (Test-ObjectProperty -Object $Json -PropertyName 'sources')) {
        $highTrustCount = 0
        $lowTrustCount = 0
        foreach ($source in (Get-ListItems -Value (Get-RawObjectValue -Object $Json -PropertyName 'sources'))) {
            if ($null -eq $source) {
                continue
            }

            if (Test-ObjectProperty -Object $source -PropertyName 'trust_level') {
                $trustLevel = [string](Get-RawObjectValue -Object $source -PropertyName 'trust_level')
                if ($trustLevel -eq 'high') {
                    $highTrustCount++
                }

                if ($trustLevel -eq 'low') {
                    $lowTrustCount++
                }
            }
        }

        if ($highTrustCount -eq 0) {
            [void]$warningSignals.Add("no high-trust source found")
        }

        if ($lowTrustCount -gt 0) {
            [void]$warningSignals.Add("contains low-trust sources")
        }
    }

    if ($null -ne $Json -and (Test-ObjectProperty -Object $Json -PropertyName 'conflicts')) {
        foreach ($conflict in (Get-ListItems -Value (Get-RawObjectValue -Object $Json -PropertyName 'conflicts'))) {
            if ($null -eq $conflict -or -not (Test-ObjectProperty -Object $conflict -PropertyName 'status')) {
                continue
            }

            $status = [string](Get-RawObjectValue -Object $conflict -PropertyName 'status')
            if ($status -eq 'open' -or $status -eq 'deferred') {
                [void]$warningSignals.Add("contains unresolved conflicts")
                break
            }
        }
    }

    if ($null -ne $Json -and (Test-ObjectProperty -Object $Json -PropertyName 'ambiguities')) {
        foreach ($ambiguity in (Get-ListItems -Value (Get-RawObjectValue -Object $Json -PropertyName 'ambiguities'))) {
            if ($null -eq $ambiguity -or -not (Test-ObjectProperty -Object $ambiguity -PropertyName 'status')) {
                continue
            }

            $status = [string](Get-RawObjectValue -Object $ambiguity -PropertyName 'status')
            if ($status -eq 'open' -or $status -eq 'deferred') {
                [void]$warningSignals.Add("contains unresolved ambiguities")
                break
            }
        }
    }

    if ($null -ne $Json -and (Test-ObjectProperty -Object $Json -PropertyName 'uncertainties')) {
        $uncertainties = Get-ListItems -Value (Get-RawObjectValue -Object $Json -PropertyName 'uncertainties')
        if ($uncertainties.Count -gt 0) {
            [void]$warningSignals.Add("contains explicit uncertainties")
        }
    }

    if ($warningSignals.Count -eq 0) {
        $result.Label = "READY"
        [void]$result.Basis.Add("no warning signals detected by heuristic checks")
        return $result
    }

    $result.Label = "WARNING"
    foreach ($signal in $warningSignals) {
        [void]$result.Basis.Add($signal)
    }

    return $result
}

function Write-ReviewSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DisplayPath,

        [Parameter(Mandatory = $true)]
        [bool]$StructurePassed,

        [Parameter(Mandatory = $true)]
        [string]$DisplayBasePath,

        [Parameter(Mandatory = $true)]
        [string]$SkillRootPath,

        $Json,

        [Parameter(Mandatory = $true)]
        [string]$ResolvedInputPath
    )

    $reviewChecklistPath = Join-Path $SkillRootPath "references\REVIEW_CHECKLIST.md"
    $reviewChecklistDisplay = Get-DisplayPath -ResolvedPath $reviewChecklistPath -BasePath $DisplayBasePath -OriginalPath $reviewChecklistPath

    $heuristicVerdict = Get-HeuristicReviewVerdict -StructurePassed $StructurePassed -Json $Json -ResolvedInputPath $ResolvedInputPath

    if (-not $StructurePassed) {
        $invalidGuidePath = Join-Path $SkillRootPath "examples\invalid\invalid-sample.md"
        $invalidGuideDisplay = Get-DisplayPath -ResolvedPath $invalidGuidePath -BasePath $DisplayBasePath -OriginalPath $invalidGuidePath

        Write-Host ("[REVIEW] {0}" -f $DisplayPath) -ForegroundColor Yellow
        Write-Host "  Structure: FAIL"
        Write-Host ("  Heuristic verdict: {0}" -f $heuristicVerdict.Label)
        Write-Host ("  Basis: {0}" -f ($heuristicVerdict.Basis -join '; '))
        Write-Host "  Next: fix schema errors first, then rerun validation."
        Write-Host ("  Review entry: {0}" -f $invalidGuideDisplay)
        return
    }

    $sampleGuidePath = Join-Path $SkillRootPath "examples\ready\sample.md"
    $warningGuidePath = Join-Path $SkillRootPath "examples\warning\warning-sample.md"
    $sampleGuideDisplay = Get-DisplayPath -ResolvedPath $sampleGuidePath -BasePath $DisplayBasePath -OriginalPath $sampleGuidePath
    $warningGuideDisplay = Get-DisplayPath -ResolvedPath $warningGuidePath -BasePath $DisplayBasePath -OriginalPath $warningGuidePath
    $inputFileName = [System.IO.Path]::GetFileName($ResolvedInputPath)

    Write-Host ("[REVIEW] {0}" -f $DisplayPath) -ForegroundColor Yellow
    Write-Host "  Structure: PASS"
    Write-Host ("  Heuristic verdict: {0}" -f $heuristicVerdict.Label)
    Write-Host ("  Basis: {0}" -f ($heuristicVerdict.Basis -join '; '))
    Write-Host ("  Review entry: {0}" -f $reviewChecklistDisplay)

    if ($inputFileName -ieq "warning-sample.json") {
        Write-Host ("  Suggested guide: {0}" -f $warningGuideDisplay)
    }
    elseif ($inputFileName -ieq "sample.json") {
        Write-Host ("  Suggested guide: {0}" -f $sampleGuideDisplay)
    }
    else {
        Write-Host ("  Suggested guide: {0}" -f $sampleGuideDisplay)
        Write-Host ("  Yellow-light reference: {0}" -f $warningGuideDisplay)
    }

    Write-Host "  Focus: review source quality, concept boundaries, and relation precision before deciding readiness."
}

function Read-JsonDocument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $serializer.MaxJsonLength = 67108864
    $content = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    return $serializer.DeserializeObject($content)
}

function Get-JsonKind {
    param(
        [Parameter(ValueFromPipeline = $true)]
        $Value
    )

    if ($null -eq $Value) {
        return "null"
    }

    if ($Value -is [string]) {
        return "string"
    }

    if ($Value -is [bool]) {
        return "boolean"
    }

    if ($Value -is [int] -or $Value -is [long]) {
        return "integer"
    }

    if ($Value -is [double] -or $Value -is [float] -or $Value -is [decimal]) {
        return "number"
    }

    if ($Value -is [System.Collections.IList]) {
        return "array"
    }

    if ($Value -is [pscustomobject] -or $Value -is [System.Collections.IDictionary]) {
        return "object"
    }

    return $Value.GetType().Name
}

function Get-ObjectProperties {
    param(
        [Parameter(Mandatory = $true)]
        $Object
    )

    if ($Object -is [System.Collections.IDictionary]) {
        return $Object.Keys
    }

    return $Object.PSObject.Properties.Name
}

function Get-ObjectValue {
    param(
        [Parameter(Mandatory = $true)]
        $Object,

        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    if ($Object -is [System.Collections.IDictionary]) {
        $value = $Object[$PropertyName]
    }
    else {
        $value = $Object.$PropertyName
    }

    if ($value -is [System.Collections.IList] -and -not ($value -is [string])) {
        return ,$value
    }

    return $value
}

function Test-ObjectProperty {
    param(
        [Parameter(Mandatory = $true)]
        $Object,

        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    if ($null -eq $Object) {
        return $false
    }

    if ($Object -is [System.Collections.IDictionary]) {
        return $Object.Keys -contains $PropertyName
    }

    return $Object.PSObject.Properties.Name -contains $PropertyName
}

function Resolve-SchemaNode {
    param(
        [Parameter(Mandatory = $true)]
        $SchemaRoot,

        [Parameter(Mandatory = $true)]
        $SchemaNode
    )

    if ($null -eq $SchemaNode) {
        return $null
    }

    if (Test-ObjectProperty -Object $SchemaNode -PropertyName '$ref') {
        $ref = Get-ObjectValue -Object $SchemaNode -PropertyName '$ref'
        if (-not $ref.StartsWith('#/')) {
            throw "Only local schema refs are supported: $ref"
        }

        $current = $SchemaRoot
        foreach ($segment in $ref.Substring(2).Split('/')) {
            $decoded = $segment.Replace('~1', '/').Replace('~0', '~')
            $current = Get-ObjectValue -Object $current -PropertyName $decoded
        }

        return Resolve-SchemaNode -SchemaRoot $SchemaRoot -SchemaNode $current
    }

    return $SchemaNode
}

function Add-ValidationError {
    param(
        [System.Collections.ArrayList]$ValidationIssues,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        [void]$ValidationIssues.Add($Message)
        return
    }

    [void]$ValidationIssues.Add("${Path}: $Message")
}

function Test-DateTimeString {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $result = [System.DateTimeOffset]::MinValue
    return [System.DateTimeOffset]::TryParse($Value, [ref]$result)
}

function Test-UniqueItems {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IList]$Items
    )

    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($item in $Items) {
        $signature = ConvertTo-Json $item -Depth 100 -Compress
        if (-not $seen.Add($signature)) {
            return $false
        }
    }

    return $true
}

function Test-SchemaNode {
    param(
        $Value,

        [Parameter(Mandatory = $true)]
        $SchemaNode,

        [Parameter(Mandatory = $true)]
        $SchemaRoot,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [System.Collections.ArrayList]$ValidationIssues
    )

    $schema = Resolve-SchemaNode -SchemaRoot $SchemaRoot -SchemaNode $SchemaNode
    if ($null -eq $schema) {
        return
    }

    if (Test-ObjectProperty -Object $schema -PropertyName 'oneOf') {
        $matched = 0
        foreach ($option in (Get-ObjectValue -Object $schema -PropertyName 'oneOf')) {
            $optionErrors = New-Object System.Collections.ArrayList
            Test-SchemaNode -Value $Value -SchemaNode $option -SchemaRoot $SchemaRoot -Path $Path -ValidationIssues $optionErrors
            if ($optionErrors.Count -eq 0) {
                $matched++
            }
        }

        if ($matched -ne 1) {
            Add-ValidationError -ValidationIssues $ValidationIssues -Path $Path -Message "must match exactly one schema branch"
        }
        return
    }

    if (Test-ObjectProperty -Object $schema -PropertyName 'const') {
        $constValue = Get-ObjectValue -Object $schema -PropertyName 'const'
        if ($Value -ne $constValue) {
            Add-ValidationError -ValidationIssues $ValidationIssues -Path $Path -Message "must equal '$constValue'"
            return
        }
    }

    if (Test-ObjectProperty -Object $schema -PropertyName 'enum') {
        $allowed = @((Get-ObjectValue -Object $schema -PropertyName 'enum'))
        if (-not ($allowed -contains $Value)) {
            Add-ValidationError -ValidationIssues $ValidationIssues -Path $Path -Message "must be one of: $($allowed -join ', ')"
        }
    }

    if (Test-ObjectProperty -Object $schema -PropertyName 'type') {
        $actualType = Get-JsonKind $Value
        $expectedType = Get-ObjectValue -Object $schema -PropertyName 'type'
        if ($actualType -ne $expectedType) {
            Add-ValidationError -ValidationIssues $ValidationIssues -Path $Path -Message "expected type '$expectedType' but got '$actualType'"
            return
        }
    }

    if ($null -eq $Value) {
        return
    }

    if ((Test-ObjectProperty -Object $schema -PropertyName 'minLength') -and $Value -is [string]) {
        $minLength = [int](Get-ObjectValue -Object $schema -PropertyName 'minLength')
        if ($Value.Length -lt $minLength) {
            Add-ValidationError -ValidationIssues $ValidationIssues -Path $Path -Message "must have length >= $minLength"
        }
    }

    if ((Test-ObjectProperty -Object $schema -PropertyName 'pattern') -and $Value -is [string]) {
        $pattern = Get-ObjectValue -Object $schema -PropertyName 'pattern'
        if ($Value -notmatch $pattern) {
            Add-ValidationError -ValidationIssues $ValidationIssues -Path $Path -Message "must match pattern $pattern"
        }
    }

    if ((Test-ObjectProperty -Object $schema -PropertyName 'format') -and $Value -is [string]) {
        $format = Get-ObjectValue -Object $schema -PropertyName 'format'
        if ($format -eq 'date-time' -and -not (Test-DateTimeString -Value $Value)) {
            Add-ValidationError -ValidationIssues $ValidationIssues -Path $Path -Message "must be a valid ISO-8601 date-time"
        }
    }

    if ((Test-ObjectProperty -Object $schema -PropertyName 'minimum') -and ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal])) {
        $minimum = [double](Get-ObjectValue -Object $schema -PropertyName 'minimum')
        if ([double]$Value -lt $minimum) {
            Add-ValidationError -ValidationIssues $ValidationIssues -Path $Path -Message "must be >= $minimum"
        }
    }

    $actualType = Get-JsonKind $Value

    if ($actualType -eq 'array') {
        if (Test-ObjectProperty -Object $schema -PropertyName 'minItems') {
            $minItems = [int](Get-ObjectValue -Object $schema -PropertyName 'minItems')
            if ($Value.Count -lt $minItems) {
                Add-ValidationError -ValidationIssues $ValidationIssues -Path $Path -Message "must contain at least $minItems items"
            }
        }

        if ((Test-ObjectProperty -Object $schema -PropertyName 'uniqueItems') -and [bool](Get-ObjectValue -Object $schema -PropertyName 'uniqueItems')) {
            if (-not (Test-UniqueItems -Items $Value)) {
                Add-ValidationError -ValidationIssues $ValidationIssues -Path $Path -Message "must contain unique items"
            }
        }

        if (Test-ObjectProperty -Object $schema -PropertyName 'items') {
            $itemsSchema = Get-ObjectValue -Object $schema -PropertyName 'items'
            for ($index = 0; $index -lt $Value.Count; $index++) {
                Test-SchemaNode -Value $Value[$index] -SchemaNode $itemsSchema -SchemaRoot $SchemaRoot -Path "$Path[$index]" -ValidationIssues $ValidationIssues
            }
        }

        return
    }

    if ($actualType -eq 'object') {
        $properties = @{}
        foreach ($propName in (Get-ObjectProperties -Object $Value)) {
            $properties[$propName] = Get-ObjectValue -Object $Value -PropertyName $propName
        }

        if (Test-ObjectProperty -Object $schema -PropertyName 'required') {
            foreach ($requiredName in @((Get-ObjectValue -Object $schema -PropertyName 'required'))) {
                if (-not $properties.ContainsKey($requiredName)) {
                    Add-ValidationError -ValidationIssues $ValidationIssues -Path $Path -Message "missing required property '$requiredName'"
                }
            }
        }

        $allowedProperties = @{}
        if (Test-ObjectProperty -Object $schema -PropertyName 'properties') {
            $schemaProperties = Get-ObjectValue -Object $schema -PropertyName 'properties'
            foreach ($propName in (Get-ObjectProperties -Object $schemaProperties)) {
                $allowedProperties[$propName] = Get-ObjectValue -Object $schemaProperties -PropertyName $propName
            }
        }

        if ((Test-ObjectProperty -Object $schema -PropertyName 'additionalProperties') -and -not [bool](Get-ObjectValue -Object $schema -PropertyName 'additionalProperties')) {
            foreach ($propName in $properties.Keys) {
                if (-not $allowedProperties.ContainsKey($propName)) {
                    $extraPath = if ($Path -eq '$') { "`$.$propName" } else { "$Path.$propName" }
                    Add-ValidationError -ValidationIssues $ValidationIssues -Path $extraPath -Message "property is not allowed"
                }
            }
        }

        foreach ($propName in $allowedProperties.Keys) {
            if ($properties.ContainsKey($propName)) {
                $childPath = if ($Path -eq '$') { "`$.$propName" } else { "$Path.$propName" }
                Test-SchemaNode -Value $properties[$propName] -SchemaNode $allowedProperties[$propName] -SchemaRoot $SchemaRoot -Path $childPath -ValidationIssues $ValidationIssues
            }
        }
    }
}

$scriptBasePath = Resolve-BasePath
$skillRootPath = [System.IO.Path]::GetFullPath((Join-Path $scriptBasePath ".."))
$displayBasePath = (Get-Location).Path

if ([string]::IsNullOrWhiteSpace($SchemaPath)) {
    $resolvedSchemaPath = [System.IO.Path]::GetFullPath((Join-Path $skillRootPath "templates\TEMPLATE.schema.json"))
}
else {
    $resolvedSchemaPath = Resolve-InputPath -BasePath $displayBasePath -Path $SchemaPath
}

$inputPaths = @($Paths)
if ($inputPaths.Count -eq 0) {
    $inputPaths = @([System.IO.Path]::GetFullPath((Join-Path $skillRootPath "examples\ready\sample.json")))
}

if (-not (Test-Path -LiteralPath $resolvedSchemaPath)) {
    throw "Schema file not found: $resolvedSchemaPath"
}

$schemaRoot = Read-JsonDocument $resolvedSchemaPath
$failed = $false

foreach ($inputPath in $inputPaths) {
    $resolvedInputPath = Resolve-InputPath -BasePath $displayBasePath -Path $inputPath
    $displayPath = Get-DisplayPath -ResolvedPath $resolvedInputPath -BasePath $displayBasePath -OriginalPath $inputPath

    if (-not (Test-Path -LiteralPath $resolvedInputPath)) {
        Write-Host "[FAIL] $displayPath" -ForegroundColor Red
        Write-Host "  File not found: $resolvedInputPath"
        if ($ReviewMode) {
            Write-ReviewSummary -DisplayPath $displayPath -StructurePassed $false -DisplayBasePath $displayBasePath -SkillRootPath $skillRootPath -Json $null -ResolvedInputPath $resolvedInputPath
        }
        $failed = $true
        continue
    }

    try {
        $json = Read-JsonDocument $resolvedInputPath
    }
    catch {
        Write-Host "[FAIL] $displayPath" -ForegroundColor Red
        Write-Host "  Invalid JSON: $($_.Exception.Message)"
        if ($ReviewMode) {
            Write-ReviewSummary -DisplayPath $displayPath -StructurePassed $false -DisplayBasePath $displayBasePath -SkillRootPath $skillRootPath -Json $null -ResolvedInputPath $resolvedInputPath
        }
        $failed = $true
        continue
    }

    $validationIssues = New-Object System.Collections.ArrayList
    Test-SchemaNode -Value $json -SchemaNode $schemaRoot -SchemaRoot $schemaRoot -Path '$' -ValidationIssues $validationIssues

    if ($validationIssues.Count -eq 0) {
        Write-Host "[PASS] $displayPath" -ForegroundColor Green
        if ($ReviewMode) {
            Write-ReviewSummary -DisplayPath $displayPath -StructurePassed $true -DisplayBasePath $displayBasePath -SkillRootPath $skillRootPath -Json $json -ResolvedInputPath $resolvedInputPath
        }
        continue
    }

    Write-Host "[FAIL] $displayPath" -ForegroundColor Red
    foreach ($validationError in $validationIssues) {
        Write-Host "  - $validationError"
    }
    if ($ReviewMode) {
        Write-ReviewSummary -DisplayPath $displayPath -StructurePassed $false -DisplayBasePath $displayBasePath -SkillRootPath $skillRootPath -Json $json -ResolvedInputPath $resolvedInputPath
    }
    $failed = $true
}

if ($failed) {
    exit 1
}

exit 0