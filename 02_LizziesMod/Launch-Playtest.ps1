<#!
.SYNOPSIS
Builds LizziesMod and starts the native 7 Days To Die quick-continue flow.

.DESCRIPTION
7 Days To Die's -loadsavegame=true launch preference loads the save that is
currently selected in the client profile. The default test target is
Playtesting/a; select it once through the normal Continue screen before using
this launcher for the first time.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die',
    [string]$WorldName = 'Limbo',
    [string]$SaveName = 'test',
    [switch]$SkipBuild,
    [switch]$MainMenu
)

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'LizziesMod.csproj'
$backroomsProjectPath = Join-Path (Join-Path $PSScriptRoot '..\LizziesMod_Backrooms') 'LizziesMod_Backrooms.csproj'
$gameExecutable = Join-Path $GameRoot '7DaysToDie.exe'
$saveDirectory = Join-Path (Join-Path (Join-Path $env:APPDATA '7DaysToDie\Saves') $WorldName) $SaveName
$saveMarker = Join-Path $saveDirectory 'main.ttw'
$logDirectory = Join-Path $env:APPDATA '7DaysToDie\logs'
$timestamp = Get-Date -Format 'yyyy-MM-dd__HH-mm-ss'
$logPath = Join-Path $logDirectory ("output_log_client__{0}.txt" -f $timestamp)

if (-not (Test-Path -LiteralPath $gameExecutable -PathType Leaf)) {
    throw "7 Days To Die was not found at '$gameExecutable'. Use -GameRoot to provide its install folder."
}

if (-not (Test-Path -LiteralPath $saveMarker -PathType Leaf)) {
    throw "The requested test save '$WorldName/$SaveName' does not contain main.ttw at '$saveDirectory'."
}

$runningClient = Get-Process -Name '7DaysToDie', '7DaysToDie_EAC' -ErrorAction SilentlyContinue
if ($null -ne $runningClient) {
    throw "7 Days To Die is already running (PID $($runningClient.Id -join ', ')). Exit it before launching a fresh test client."
}

if (-not $SkipBuild -and $PSCmdlet.ShouldProcess($projectPath, 'Build LizziesMod')) {
    & dotnet msbuild $projectPath /t:Build /p:Configuration=Debug /p:Platform=AnyCPU
    if ($LASTEXITCODE -ne 0) {
        throw "LizziesMod build failed with exit code $LASTEXITCODE."
    }

    if (Test-Path -LiteralPath $backroomsProjectPath -PathType Leaf) {
        & dotnet msbuild $backroomsProjectPath /t:Build /p:Configuration=Debug /p:Platform=AnyCPU
        if ($LASTEXITCODE -ne 0) {
            throw "LizziesMod_Backrooms build failed with exit code $LASTEXITCODE."
        }
    }
}

if (-not (Test-Path -LiteralPath $logDirectory -PathType Container) -and $PSCmdlet.ShouldProcess($logDirectory, 'Create log directory')) {
    New-Item -ItemType Directory -Path $logDirectory | Out-Null
}

$arguments = @(
    '-skipintro',
    '-force-d3d12',
    '-nogs',
    '-noeac',
    '-skipnewsscreen=true'
)

if ($MainMenu) {
    Write-Host 'Prepared a main-menu launch.'
}
else {
    $arguments += '-loadsavegame=true'
    Write-Host "Prepared native quick-continue for '$WorldName/$SaveName'."
}

$arguments += '-logfile'
$arguments += ('"{0}"' -f $logPath)

Write-Host "Client log: $logPath"
if (-not $MainMenu) {
    Write-Host "The game loads the last selected local save; select '$WorldName/$SaveName' once in Continue if this is the first launch."
}

if ($PSCmdlet.ShouldProcess($gameExecutable, $(if ($MainMenu) { 'Launch main menu' } else { "Launch $WorldName/$SaveName with native quick-continue" }))) {
    Start-Process -FilePath $gameExecutable -ArgumentList $arguments -WorkingDirectory $GameRoot
}