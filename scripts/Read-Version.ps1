$ErrorActionPreference = 'Stop'
$versionSource = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\BlasphemousTrainer\ProjectInfo.cs'
$versionMatch = [regex]::Match([IO.File]::ReadAllText($versionSource), 'const string Version = "([0-9]+\.[0-9]+\.[0-9]+)";')
if (-not $versionMatch.Success) { throw 'ProjectInfo.Version is missing or malformed.' }
$versionMatch.Groups[1].Value
