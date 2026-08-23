param(
    [string]$ZipPath,
    [string]$Pattern
)
Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
if (-not (Test-Path $ZipPath)) { Write-Error "ZIP not found: $ZipPath"; exit 2 }
$z = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
$entries = $z.Entries | ForEach-Object { $_.FullName }
$z.Dispose()
$found = $entries | Where-Object { $_ -like "*$Pattern*" }
if ($found.Count -gt 0) {
    Write-Host "Found $($found.Count) matching entries (showing up to 10):"
    $found[0..([Math]::Min(9,$found.Count-1))] | ForEach-Object { Write-Host " - $_" }
} else {
    Write-Host "No entries matching '$Pattern' found"
}
