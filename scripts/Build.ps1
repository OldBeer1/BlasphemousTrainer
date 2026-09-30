$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$dotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
Push-Location $projectRoot
try {
    & $dotnet build '.\src\BlasphemousTrainer\BlasphemousTrainer.csproj' -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
    $dist = Join-Path $projectRoot 'dist\BlasphemousTrainer'
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
    Copy-Item -LiteralPath '.\src\BlasphemousTrainer\bin\Release\net35\BlasphemousTrainer.dll' -Destination $dist -Force
    if (@(Get-ChildItem -LiteralPath $dist -File | Where-Object Name -ne 'BlasphemousTrainer.dll').Count) { throw 'Unexpected distribution files.' }
    Get-FileHash -LiteralPath (Join-Path $dist 'BlasphemousTrainer.dll') -Algorithm SHA256
} finally { Pop-Location }
