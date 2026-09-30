param(
    [string]$Source = (Join-Path $env:USERPROFILE 'AppData\LocalLow\TheGameKitchen\Blasphemous'),
    [string]$BackupRoot = (Join-Path $env:LOCALAPPDATA 'BlasphemousTrainerBackups')
)
$ErrorActionPreference = 'Stop'
if (Get-Process Blasphemous -ErrorAction SilentlyContinue) { throw 'Exit Blasphemous before backing up saves.' }
$sourcePath = (Resolve-Path -LiteralPath $Source).Path.TrimEnd('\')
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath 'Savegames'))) { throw 'Expected Savegames directory was not found; verify the real save location.' }
$destination = Join-Path $BackupRoot (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
if ([IO.Path]::GetFullPath($destination).StartsWith($sourcePath+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Backup must be outside source.' }
New-Item -ItemType Directory -Path $destination | Out-Null
$records = foreach ($file in Get-ChildItem -LiteralPath $sourcePath -Recurse -File) {
    $relative = $file.FullName.Substring($sourcePath.Length+1)
    $target = Join-Path (Join-Path $destination 'files') $relative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    $before = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $file.FullName -Destination $target
    $after = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $copy = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($before -ne $after -or $before -ne $copy) { throw "Backup verification failed: $relative" }
    [ordered]@{ RelativePath=$relative; Bytes=$file.Length; SHA256=$copy; LastWriteTimeUtc=$file.LastWriteTimeUtc.ToString('o') }
}
if (@($records).Count -eq 0) { throw 'No files backed up.' }
[ordered]@{ CreatedAt=(Get-Date -Format o); Source=$sourcePath; Files=@($records) } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding UTF8
$artifacts = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
[ordered]@{ BackupDirectory=$destination; Files=@($records).Count; Verified=$true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'backup-summary.json') -Encoding UTF8
"Backup verified: $(@($records).Count) files. Location: $destination"

