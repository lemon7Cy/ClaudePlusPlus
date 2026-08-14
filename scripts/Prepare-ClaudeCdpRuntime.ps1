param(
    [Parameter(Mandatory = $true)]
    [string]$SourceAppDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedAsarSha256,

    [string]$RuntimeRoot = (Join-Path $env:LOCALAPPDATA 'ClaudePlusPlus\runtime'),

    [string]$UserDataDirectory = (Join-Path $env:LOCALAPPDATA 'ClaudePlusPlus\profiles\cdp'),

    [int]$Port = 9344,

    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $parentPath = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    $childPath = [IO.Path]::GetFullPath($Child)
    if (-not $childPath.StartsWith($parentPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside $parentPath : $childPath"
    }
}

function Find-PatternOffsets {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Bytes,
        [Parameter(Mandatory = $true)][byte[]]$Pattern
    )

    $offsets = [Collections.Generic.List[int]]::new()
    for ($index = 0; $index -le $Bytes.Length - $Pattern.Length; $index++) {
        if ($Bytes[$index] -ne $Pattern[0]) {
            continue
        }

        $matches = $true
        for ($patternIndex = 1; $patternIndex -lt $Pattern.Length; $patternIndex++) {
            if ($Bytes[$index + $patternIndex] -ne $Pattern[$patternIndex]) {
                $matches = $false
                break
            }
        }

        if ($matches) {
            $offsets.Add($index)
            $index += $Pattern.Length - 1
        }
    }

    return $offsets
}

function Set-EmbeddedAsarIntegrityFuseDisabled {
    param([Parameter(Mandatory = $true)][string]$ExecutablePath)

    $bytes = [IO.File]::ReadAllBytes($ExecutablePath)
    $sentinel = [Text.Encoding]::ASCII.GetBytes('dL7pKGdnNz796PbbjQWNKmHXBZaB9tsX')
    $offsets = @(Find-PatternOffsets -Bytes $bytes -Pattern $sentinel)
    if ($offsets.Count -ne 1) {
        throw "Electron fuse sentinel count is $($offsets.Count), expected 1."
    }

    $sentinelEnd = $offsets[0] + $sentinel.Length
    $fuseVersion = $bytes[$sentinelEnd]
    $fuseCount = $bytes[$sentinelEnd + 1]
    $integrityFuseIndex = 4
    if ($fuseVersion -ne 1 -or $fuseCount -le $integrityFuseIndex) {
        throw "Unsupported Electron fuse wire: version=$fuseVersion, count=$fuseCount"
    }

    $fuseOffset = $sentinelEnd + 2 + $integrityFuseIndex
    if ($bytes[$fuseOffset] -eq [byte][char]'0') {
        return
    }
    if ($bytes[$fuseOffset] -ne [byte][char]'1') {
        throw "Unexpected EnableEmbeddedAsarIntegrityValidation value: $($bytes[$fuseOffset])"
    }

    $bytes[$fuseOffset] = [byte][char]'0'
    [IO.File]::WriteAllBytes($ExecutablePath, $bytes)
}

function Test-EmbeddedAsarIntegrityFuseDisabled {
    param([Parameter(Mandatory = $true)][string]$ExecutablePath)

    $bytes = [IO.File]::ReadAllBytes($ExecutablePath)
    $sentinel = [Text.Encoding]::ASCII.GetBytes('dL7pKGdnNz796PbbjQWNKmHXBZaB9tsX')
    $offsets = @(Find-PatternOffsets -Bytes $bytes -Pattern $sentinel)
    if ($offsets.Count -ne 1) {
        return $false
    }

    $sentinelEnd = $offsets[0] + $sentinel.Length
    return $bytes.Length -gt $sentinelEnd + 6 -and
        $bytes[$sentinelEnd] -eq 1 -and
        $bytes[$sentinelEnd + 1] -gt 4 -and
        $bytes[$sentinelEnd + 6] -eq [byte][char]'0'
}

$sourceApp = [IO.Path]::GetFullPath($SourceAppDirectory)
$sourceAsar = Join-Path $sourceApp 'resources\app.asar'
$sourceExe = Join-Path $sourceApp 'Claude.exe'
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "Claude.exe not found: $sourceExe"
}
if (-not (Test-Path -LiteralPath $sourceAsar -PathType Leaf)) {
    throw "app.asar not found: $sourceAsar"
}

$actualHash = (Get-FileHash -LiteralPath $sourceAsar -Algorithm SHA256).Hash
if (-not $actualHash.Equals($ExpectedAsarSha256, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsupported app.asar hash. Expected $ExpectedAsarSha256, found $actualHash"
}

$runtimeBase = [IO.Path]::GetFullPath($RuntimeRoot)
$safeVersion = $Version -replace '[^0-9A-Za-z._-]', '_'
$runtimeDirectory = Join-Path $runtimeBase $safeVersion
$stagingDirectory = "$runtimeDirectory.staging"
Assert-ChildPath -Parent $runtimeBase -Child $runtimeDirectory
Assert-ChildPath -Parent $runtimeBase -Child $stagingDirectory

$manifestPath = Join-Path $runtimeDirectory 'claudeplusplus-runtime.json'
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $runtimeExe = Join-Path $runtimeDirectory 'Claude.exe'
    $runtimeAsar = Join-Path $runtimeDirectory 'resources\app.asar'
    if ($manifest.sourceAsarSha256 -eq $actualHash -and
        (Test-Path -LiteralPath $runtimeExe -PathType Leaf) -and
        (Test-Path -LiteralPath $runtimeAsar -PathType Leaf) -and
        (Test-EmbeddedAsarIntegrityFuseDisabled -ExecutablePath $runtimeExe)) {
        Write-Output "Reusing prepared runtime: $runtimeDirectory"
    }
    else {
        throw "Existing runtime failed validation: $runtimeDirectory"
    }
}
else {
    if (Test-Path -LiteralPath $runtimeDirectory) {
        throw "Runtime directory exists without a valid manifest: $runtimeDirectory"
    }
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $runtimeBase -Force | Out-Null
    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
    & robocopy.exe $sourceApp $stagingDirectory /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -gt 7) {
        throw "robocopy failed with exit code $LASTEXITCODE"
    }

    $runtimeAsar = Join-Path $stagingDirectory 'resources\app.asar'
    $bytes = [IO.File]::ReadAllBytes($runtimeAsar)
    $encoding = [Text.Encoding]::UTF8
    $originalGuard = $encoding.GetBytes('yae(process.argv)&&!f9()&&process.exit(1)')
    $patchedGuard = $encoding.GetBytes('yae(process.argv)&&!1   &&process.exit(1)')
    if ($originalGuard.Length -ne $patchedGuard.Length) {
        throw 'Patch guard lengths differ.'
    }

    $originalOffsets = @(Find-PatternOffsets -Bytes $bytes -Pattern $originalGuard)
    $patchedOffsets = @(Find-PatternOffsets -Bytes $bytes -Pattern $patchedGuard)
    if ($originalOffsets.Count -ne 1 -or $patchedOffsets.Count -ne 0) {
        throw "Patch signature mismatch. original=$($originalOffsets.Count), patched=$($patchedOffsets.Count)"
    }

    [Array]::Copy($patchedGuard, 0, $bytes, $originalOffsets[0], $patchedGuard.Length)
    [IO.File]::WriteAllBytes($runtimeAsar, $bytes)

    $verifiedBytes = [IO.File]::ReadAllBytes($runtimeAsar)
    $verifiedOriginal = @(Find-PatternOffsets -Bytes $verifiedBytes -Pattern $originalGuard)
    $verifiedPatched = @(Find-PatternOffsets -Bytes $verifiedBytes -Pattern $patchedGuard)
    if ($verifiedOriginal.Count -ne 0 -or $verifiedPatched.Count -ne 1) {
        throw "Patched app.asar verification failed. original=$($verifiedOriginal.Count), patched=$($verifiedPatched.Count)"
    }

    $runtimeExe = Join-Path $stagingDirectory 'Claude.exe'
    Set-EmbeddedAsarIntegrityFuseDisabled -ExecutablePath $runtimeExe
    if (-not (Test-EmbeddedAsarIntegrityFuseDisabled -ExecutablePath $runtimeExe)) {
        throw 'Electron ASAR integrity fuse verification failed.'
    }

    $patchedHash = (Get-FileHash -LiteralPath $runtimeAsar -Algorithm SHA256).Hash
    [ordered]@{
        schemaVersion = 1
        claudeVersion = $Version
        sourceAppDirectory = $sourceApp
        sourceAsarSha256 = $actualHash
        patchedAsarSha256 = $patchedHash
        patch = 'disable-cdp-auth-exit-guard-v1'
        executablePatch = 'disable-embedded-asar-integrity-validation-v1'
        createdAtUtc = [DateTime]::UtcNow.ToString('O')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stagingDirectory 'claudeplusplus-runtime.json') -Encoding UTF8

    Move-Item -LiteralPath $stagingDirectory -Destination $runtimeDirectory
    Write-Output "Prepared runtime: $runtimeDirectory"
}

if ($PrepareOnly) {
    exit 0
}

$runtimeExe = Join-Path $runtimeDirectory 'Claude.exe'
New-Item -ItemType Directory -Path $UserDataDirectory -Force | Out-Null

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $runtimeExe
$startInfo.WorkingDirectory = $runtimeDirectory
$startInfo.UseShellExecute = $false
$startInfo.Arguments = "--remote-debugging-address=127.0.0.1 --remote-debugging-port=$Port --remote-allow-origins=http://127.0.0.1:$Port"
$startInfo.Environment['CLAUDE_USER_DATA_DIR'] = [IO.Path]::GetFullPath($UserDataDirectory)
$process = [Diagnostics.Process]::Start($startInfo)
if ($null -eq $process) {
    throw 'Failed to start patched Claude runtime.'
}

$deadline = [DateTime]::UtcNow.AddSeconds(30)
$targets = $null
while ([DateTime]::UtcNow -lt $deadline) {
    if ($process.HasExited) {
        throw "Patched Claude exited before CDP was ready (exit code $($process.ExitCode))."
    }

    try {
        $targets = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/json/list" -TimeoutSec 2
        if ($targets) {
            break
        }
    }
    catch {
        Start-Sleep -Milliseconds 350
    }
}

if (-not $targets) {
    throw "CDP did not become ready on port $Port within 30 seconds."
}

$target = $targets | Where-Object { $_.type -eq 'page' -and $_.webSocketDebuggerUrl } | Select-Object -First 1
if ($null -eq $target) {
    throw 'CDP responded, but no page target was available.'
}

$webSocketUri = [Uri]$target.webSocketDebuggerUrl
$inspectorUrl = "http://127.0.0.1:$Port/devtools/inspector.html?ws=127.0.0.1:$Port$($webSocketUri.AbsolutePath)"
[ordered]@{
    processId = $process.Id
    title = $target.title
    url = $target.url
    webSocketDebuggerUrl = $target.webSocketDebuggerUrl
    inspectorUrl = $inspectorUrl
} | ConvertTo-Json
