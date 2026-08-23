param(
    [string]$ZipPath
)

if (-not (Test-Path $ZipPath)) { Write-Error "ZIP not found: $ZipPath"; exit 2 }

Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
$z = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
$entries = $z.Entries | ForEach-Object { $_.FullName }
$z.Dispose()

$bad = $entries | Where-Object { $_ -match '\.ps1$' -or $_ -match '(^|/)obj(/|$)' -or $_ -match '(^|/)\.vs(/|$)' }
if ($bad.Count -gt 0) {
    Write-Host "Bad entries found:"; $bad
} else {
    Write-Host "No bad entries found"
}
Write-Host "Total entries: $($entries.Count)"
