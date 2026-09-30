$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$downloads = Join-Path $projectRoot '.downloads'
$toolsRoot = Join-Path $projectRoot '.tools'
New-Item -ItemType Directory -Path $downloads,$toolsRoot -Force | Out-Null
$packages = @(
    @{ Name='dotnet-sdk-8.0.425-win-x64.zip'; Url='https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.425/dotnet-sdk-8.0.425-win-x64.zip'; Algorithm='SHA512'; Hash='f0b6f15bf6f1a0507205c0cb102ab99e1dee875c4682c8ed94665be1d580186a06b21455e83b3a01a0ff7f4cd887b67420f2e2fe09ed985534a4cea488ae1af9'; Folder='dotnet' },
    @{ Name='BepInEx_win_x64_5.4.23.5.zip'; Url='https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip'; Algorithm='SHA256'; Hash='82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'; Folder='BepInEx-5.4.23.5' }
)
foreach ($package in $packages) {
    $zip = Join-Path $downloads $package.Name
    if (-not (Test-Path -LiteralPath $zip)) {
        Write-Output "Downloading $($package.Name)"
        Invoke-WebRequest -Uri $package.Url -OutFile $zip
    }
    if ((Get-FileHash -LiteralPath $zip -Algorithm $package.Algorithm).Hash -ne $package.Hash) { throw "Checksum mismatch: $zip" }
    $destination = Join-Path $toolsRoot $package.Folder
    if (-not (Test-Path -LiteralPath (Join-Path $destination '.extraction-complete'))) {
        Expand-Archive -LiteralPath $zip -DestinationPath $destination -Force
        Set-Content -LiteralPath (Join-Path $destination '.extraction-complete') -Value $package.Hash -Encoding ASCII
    }
    Write-Output "Verified and extracted $($package.Name)"
}
$packages | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $toolsRoot 'packages.lock.json') -Encoding UTF8
& (Join-Path $toolsRoot 'dotnet\dotnet.exe') --version
if ($LASTEXITCODE -ne 0) { throw 'Local SDK check failed.' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $toolsRoot 'cli-home'
$env:NUGET_PACKAGES = Join-Path $toolsRoot 'nuget-packages'
$env:DOTNET_ROOT = Join-Path $toolsRoot 'dotnet'
$ilspyDirectory = Join-Path $toolsRoot 'ilspy'
$ilspy = Join-Path $ilspyDirectory 'ilspycmd.exe'
if (-not (Test-Path -LiteralPath $ilspy)) {
    & (Join-Path $toolsRoot 'dotnet\dotnet.exe') tool install ilspycmd --version 9.1.0.7988 --tool-path $ilspyDirectory --add-source https://api.nuget.org/v3/index.json
    if ($LASTEXITCODE -ne 0) { throw 'ILSpy installation failed.' }
}
& $ilspy --version
if ($LASTEXITCODE -ne 0) { throw 'ILSpy check failed.' }
