param([string]$GameRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference = 'Stop'
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
$exe = Join-Path $GameRoot 'Blasphemous.exe'
$bytes = [IO.File]::ReadAllBytes($exe)
$peOffset = [BitConverter]::ToInt32($bytes, 60)
$files = @('Blasphemous.exe','UnityPlayer.dll','Blasphemous_Data\Managed\Assembly-CSharp.dll','Blasphemous_Data\Managed\mscorlib.dll')
$records = foreach ($relative in $files) {
    $path = Join-Path $GameRoot $relative
    $item = Get-Item -LiteralPath $path
    [ordered]@{ Path=$relative; Bytes=$item.Length; FileVersion=$item.VersionInfo.FileVersion; SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
}
$manifest = Join-Path (Split-Path (Split-Path $GameRoot -Parent) -Parent) 'appmanifest_774361.acf'
$buildId = $null
if (Test-Path -LiteralPath $manifest) {
    $text = Get-Content -LiteralPath $manifest -Raw
    if ($text -match '"buildid"\s+"(\d+)"') { $buildId = $Matches[1] }
}
$conflicts = @('BepInEx','Modding','winhttp.dll','version.dll','doorstop_config.ini') | Where-Object { Test-Path -LiteralPath (Join-Path $GameRoot $_) }
$report = [ordered]@{
    InspectedAt=(Get-Date -Format o); GameRoot=$GameRoot; AppId='774361'; BuildId=$buildId
    PEMachine=('0x{0:X4}' -f [BitConverter]::ToUInt16($bytes,$peOffset+4))
    MonoPresent=(Test-Path -LiteralPath (Join-Path $GameRoot 'Blasphemous_Data\Mono\EmbedRuntime\mono.dll'))
    NetStandardPresent=(Test-Path -LiteralPath (Join-Path $GameRoot 'Blasphemous_Data\Managed\netstandard.dll'))
    Running=[bool](Get-Process Blasphemous -ErrorAction SilentlyContinue)
    ExistingLoaderPaths=@($conflicts); Files=@($records)
}
$destination = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'environment.json') -Encoding UTF8
$report | ConvertTo-Json -Depth 6
