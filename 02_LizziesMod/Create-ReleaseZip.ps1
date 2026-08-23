<#
.SYNOPSIS
    Create a Nexus-ready ZIP of the mod folder while excluding PowerShell scripts
    and common development artifacts (obj, .vs, etc.).

.DESCRIPTION
    This script packages the provided mod source folder into a ZIP file.
    By default it excludes files with the extensions in the default
    exclusion list (including .ps1) and directories named in the
    default exclude directory list (including 'obj' and '.vs').

.PARAMETER Source
    Path to the mod folder to package. Defaults to the script's folder when
    run from inside the mod directory, or '.' when dot-sourced.

.PARAMETER Output
    Optional full path for the output ZIP. If omitted, a Releases folder
    will be created next to the source and the ZIP will be named using
    the mod name and version from ModInfo.xml when available.
.PARAMETER ExcludeExtensions
    Array of file extensions (including the leading dot) to exclude.
    Defaults to '.ps1' to avoid Nexus flagging.

.PARAMETER ExcludeDirNames
    Array of directory names to exclude anywhere in the tree (case-insensitive).
    Defaults to 'obj' and '.vs'.

.PARAMETER SkipMods
    Array of exact mod folder names to skip when packaging multiple mods.

.PARAMETER ModName
    Exact mod folder name to package. This packages only that mod.

.PARAMETER ModPattern
    Text or wildcard pattern matched against mod folder names when packaging
    multiple mods. Only matching mods are included.

.PARAMETER IncludeAllMods
    Package all mod folders under the source folder. Use ModPattern or
    SkipMods to narrow the set.

.PARAMETER WhatIf
    When specified, the script only prints which files would be included
    and does not create the ZIP.

.EXAMPLE
    # Dry-run: list files that would be packaged
    .\Create-ReleaseZip.ps1 -Source . -WhatIf

    # Create ZIP using detected name/version
    .\Create-ReleaseZip.ps1 -Source .
#>

[CmdletBinding()]
param(
    [Parameter(Position=0)]
    [string]$Source = '.',

    [Parameter(Position=1)]
    [string]$Output = '',

    [string[]]$ExcludeExtensions = @('.ps1', '.pdb', '.user', '.suo', '.log', '.tmp', '.cache', '.bak'),

    [string[]]$ExcludeDirNames = @('obj', '.vs', 'bin', 'Debug', 'Release', 'packages', 'ConsoleCommandInbox', 'node_modules', '.git'),

    [string[]]$SkipMods = @(),

    [string]$ModName = '',

    [string]$ModPattern = '',

    [switch]$IncludeAllMods,

    [switch]$WhatIf
)

function Normalize-ExtList {
    param([string[]]$list)
    if (-not $list) { return @() }
    return $list | ForEach-Object { if ($_ -and $_ -notmatch '^\.') { '.' + $_ } else { $_ } } | ForEach-Object { $_.ToLower() }
}

try {
    # Resolve source path; default to script folder when invoked as a script
    if ($Source -eq '.' -and $MyInvocation.MyCommand.Definition) {
        $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
        $Source = $scriptDir
    }
    $sourcePath = (Resolve-Path -Path $Source).ProviderPath
} catch {
    Write-Error "Source path '$Source' not found. Provide a valid folder path."
    exit 2
}

$sourcePath = $sourcePath.TrimEnd([IO.Path]::DirectorySeparatorChar)
$selectMods = $IncludeAllMods -or -not [string]::IsNullOrWhiteSpace($ModName) -or -not [string]::IsNullOrWhiteSpace($ModPattern)

# Read ModInfo.xml when available to pick a friendly mod name/version for the ZIP
$detectedName = [IO.Path]::GetFileName($sourcePath)
$modVersion = '0.0.0'

# If IncludeAllMods is specified and the provided source is a single mod folder,
# move up to the parent folder so we can package all mods under it.
if ($selectMods) {
    $maybeModInfo = Join-Path $sourcePath 'ModInfo.xml'
    if (Test-Path $maybeModInfo) {
        $sourcePath = Split-Path -Parent $sourcePath
        $detectedName = [IO.Path]::GetFileName($sourcePath)
    }
}

$modInfoPath = Join-Path $sourcePath 'ModInfo.xml'
if (-not $selectMods -and (Test-Path $modInfoPath)) {
    try {
        $xml = [xml](Get-Content -Path $modInfoPath -Raw)
        $nameNode = $xml.SelectSingleNode('//Name')
        if ($nameNode -and $nameNode.Attributes['value']) { $detectedName = $nameNode.Attributes['value'].Value }
        $verNode = $xml.SelectSingleNode('//Version')
        if ($verNode -and $verNode.Attributes['value']) { $modVersion = $verNode.Attributes['value'].Value }
    } catch {
        Write-Verbose "Failed to parse ModInfo.xml: $_"
    }
}

if ($selectMods) {
    $archiveName = if ($ModName) { $ModName } elseif ($ModPattern) { $ModPattern } else { 'Mods' }
} else {
    $archiveName = $detectedName
}

# Determine output path and filename
if ([string]::IsNullOrWhiteSpace($Output)) {
    $date = (Get-Date).ToString('yyyyMMdd')
    $outDir = Join-Path $sourcePath 'Releases'
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
    $Output = Join-Path $outDir ("$($archiveName)-$($modVersion)-$date.zip")
} else {
    $Output = (Resolve-Path -Path $Output).ProviderPath
}

$ExcludeExtensions = Normalize-ExtList -list $ExcludeExtensions
$ExcludeDirNames = $ExcludeDirNames | ForEach-Object { $_.ToLower() }

Write-Host "Packing: $sourcePath"
Write-Host "Output: $Output"
Write-Host "Excluding extensions: $($ExcludeExtensions -join ', ')"
Write-Host "Excluding directories: $($ExcludeDirNames -join ', ')"
if ($SkipMods -and $SkipMods.Count -gt 0) { Write-Host "Skipping mods: $($SkipMods -join ', ')" }
if ($ModName) { Write-Host "Selected mod: $ModName" }
if ($ModPattern) { Write-Host "Mod pattern: $ModPattern" }

# Gather files
$filesToInclude = New-Object System.Collections.Generic.List[System.IO.FileInfo]

if ($selectMods) {
    $skipLower = $SkipMods | ForEach-Object { $_.ToLower() }
    $modDirs = Get-ChildItem -Path $sourcePath -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'ModInfo.xml') }
    foreach ($md in $modDirs) {
        if ($skipLower -contains $md.Name.ToLower()) { continue }
        if ($ModName -and $md.Name -ine $ModName) { continue }
        if ($ModPattern) {
            $pattern = if ($ModPattern -match '[*?\[\]]') { $ModPattern } else { "*$ModPattern*" }
            if ($md.Name -notlike $pattern) { continue }
        }
        $allFiles = Get-ChildItem -Path $md.FullName -Recurse -File -Force
        foreach ($f in $allFiles) {
            $ext = $f.Extension.ToLower()
            if ($ExcludeExtensions -contains $ext) { continue }
            $segments = $f.DirectoryName.Split([IO.Path]::DirectorySeparatorChar) | ForEach-Object { $_.ToLower() }
            $skip = $false
            foreach ($d in $ExcludeDirNames) { if ($segments -contains $d) { $skip = $true; break } }
            if ($skip) { continue }
            $filesToInclude.Add($f)
        }
    }
} else {
    $allFiles = Get-ChildItem -Path $sourcePath -Recurse -File -Force
    foreach ($f in $allFiles) {
        $ext = $f.Extension.ToLower()
        if ($ExcludeExtensions -contains $ext) { continue }
        $segments = $f.DirectoryName.Split([IO.Path]::DirectorySeparatorChar) | ForEach-Object { $_.ToLower() }
        $skip = $false
        foreach ($d in $ExcludeDirNames) { if ($segments -contains $d) { $skip = $true; break } }
        if ($skip) { continue }
        $filesToInclude.Add($f)
    }
}

# Ensure we don't accidentally include the output ZIP in the package (if it existed)
try {
    $absOutput = (Resolve-Path -Path $Output -ErrorAction SilentlyContinue).ProviderPath
    if ($absOutput) { $filesToInclude = $filesToInclude | Where-Object { $_.FullName -ne $absOutput } }
} catch {
    # ignore
}

if ($WhatIf) {
    Write-Host "WhatIf: $($filesToInclude.Count) files would be packaged (showing relative paths):"
    foreach ($f in $filesToInclude) {
        $rel = $f.FullName.Substring($sourcePath.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
        Write-Host " - $rel"
    }
    return
}

if (Test-Path $Output) {
    Write-Host "Removing existing file: $Output"
    Remove-Item -LiteralPath $Output -Force
}

# Use a temp folder to stage the files and Compress-Archive to create a reliable ZIP
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("lizziesmod_pack_{0}" -f ([guid]::NewGuid().ToString()))
$stagingRoot = Join-Path $tempRoot $archiveName
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

foreach ($f in $filesToInclude) {
    $relativePath = $f.FullName.Substring($sourcePath.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
    $destPath = Join-Path $stagingRoot $relativePath
    $destDir = Split-Path -Parent $destPath
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
    Copy-Item -LiteralPath $f.FullName -Destination $destPath -Force
}

try {
    Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $Output -Force -CompressionLevel Optimal
    Write-Host "Created ZIP: $Output"
} finally {
    # Clean up staging area
    if (Test-Path $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
