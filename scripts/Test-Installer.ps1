$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$game = Split-Path $project -Parent
$version = & (Join-Path $PSScriptRoot 'Read-Version.ps1')
$package = Join-Path $project ('dist\BlasphemousTrainer-'+$version)
$fixture = Join-Path $project ('.work\installer-tests-'+[Guid]::NewGuid().ToString('N'))
$testHome = Join-Path $fixture 'profile'
$testLocal = Join-Path $fixture 'local'
New-Item -ItemType Directory -Path "$testHome\AppData\LocalLow\TheGameKitchen\Blasphemous\Savegames","$testLocal\BlasphemousTrainerInstaller\BepInEx-5.4.23.5" -Force | Out-Null
'fixture-save' | Set-Content -LiteralPath "$testHome\AppData\LocalLow\TheGameKitchen\Blasphemous\Savegames\slot.save"
Copy-Item -LiteralPath "$project\.downloads\BepInEx_win_x64_5.4.23.5.zip" -Destination "$testLocal\BlasphemousTrainerInstaller\BepInEx-5.4.23.5"
$checks = 0
function Assert($condition, $message) { if (-not $condition) { throw $message }; $script:checks++ }
function NewFixture([string]$name) {
    $root = Join-Path $fixture $name
    New-Item -ItemType Directory -Path "$root\Blasphemous_Data\Managed" -Force | Out-Null
    foreach ($relative in @('Blasphemous.exe','Blasphemous_Data\Managed\Assembly-CSharp.dll','Blasphemous_Data\Managed\Assembly-CSharp-firstpass.dll')) { Copy-Item -LiteralPath (Join-Path $game $relative) -Destination (Join-Path $root $relative) }
    return $root
}
function RunBackend([string]$root, [string]$mode, [int]$expected=0, [string]$driver='', [bool]$removeConfig=$false) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
    $script = if ($driver) { $driver } else { Join-Path $package 'Backend.ps1' }
    $info.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$script+'" -Mode '+$mode+' -GameRoot "'+$root+'"'
    if ($removeConfig) { $info.Arguments += ' -RemoveConfig' }
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $info.StandardOutputEncoding=[Text.Encoding]::UTF8; $info.StandardErrorEncoding=[Text.Encoding]::UTF8
    $info.EnvironmentVariables['USERPROFILE']=$testHome; $info.EnvironmentVariables['LOCALAPPDATA']=$testLocal
    $p=[Diagnostics.Process]::Start($info)
    $stdout=$p.StandardOutput.ReadToEndAsync(); $stderr=$p.StandardError.ReadToEndAsync(); $p.WaitForExit()
    $output=$stdout.Result+$stderr.Result
    $output | Set-Content -LiteralPath (Join-Path $fixture ($mode+'-'+[Guid]::NewGuid().ToString('N')+'.log')) -Encoding UTF8
    Assert ($p.ExitCode -eq $expected) "$mode unexpected exit $($p.ExitCode): $output"
    $p.Dispose()
}
$root=NewFixture 'normal'
$plugin=Join-Path $root 'BepInEx\plugins\BlasphemousTrainer\BlasphemousTrainer.dll'
RunBackend $root 'Check'
Assert (-not (Test-Path -LiteralPath $plugin)) 'Preview changed plugin'
RunBackend $root 'Install'
Assert ((Get-FileHash $plugin).Hash -eq (Get-FileHash "$package\payload\BlasphemousTrainer.dll").Hash) 'Install hash'
RunBackend $root 'Install'
New-Item -ItemType Directory -Path "$root\BepInEx\plugins\Other" -Force | Out-Null
'other-plugin' | Set-Content "$root\BepInEx\plugins\Other\other.dll"
'user-config' | Set-Content "$root\BepInEx\config\local.blasphemous.trainer.cfg"
RunBackend $root 'Uninstall'
Assert (-not (Test-Path $plugin)) 'Uninstall failed'
Assert (Test-Path "$root\BepInEx\core\BepInEx.dll") 'Shared loader removed'
Assert ((Get-Content "$root\BepInEx\plugins\Other\other.dll") -eq 'other-plugin') 'Other plugin changed'
Assert ((Get-Content "$root\BepInEx\config\local.blasphemous.trainer.cfg") -eq 'user-config') 'Config removed by default'
RunBackend $root 'Install'
RunBackend $root 'Uninstall' 0 '' $true
Assert (-not (Test-Path "$root\BepInEx\config\local.blasphemous.trainer.cfg")) 'Optional config removal failed'
RunBackend $root 'Install'
[IO.File]::AppendAllText($plugin, 'tampered')
RunBackend $root 'Uninstall' 1
RunBackend $root 'Install' 1
Assert (Test-Path $plugin) 'Tampered plugin deleted'
$conflict=NewFixture 'conflict'
'unknown' | Set-Content "$conflict\version.dll"
RunBackend $conflict 'Install' 1
Assert (-not (Test-Path "$conflict\BepInEx")) 'Conflict wrote files'
$unknown=NewFixture 'unknown-version'
[IO.File]::AppendAllText("$unknown\Blasphemous_Data\Managed\Assembly-CSharp-firstpass.dll", 'unknown')
RunBackend $unknown 'Install' 1
Assert (-not (Test-Path "$unknown\BepInEx")) 'Unknown version wrote files'
$loaderConflict=NewFixture 'loader-content-conflict'
'unknown-loader' | Set-Content "$loaderConflict\winhttp.dll"
RunBackend $loaderConflict 'Install' 1
Assert ((Get-Content "$loaderConflict\winhttp.dll") -eq 'unknown-loader') 'Unknown loader overwritten'
# Inject a deterministic copy failure only in this test child, after loader files were created.
$rollback=NewFixture 'rollback'
$driver=Join-Path $fixture 'fail-copy.ps1'
$driverText=@'
param($Mode,$GameRoot)
function Copy-Item {
 param([string]$LiteralPath,[string]$Destination,[switch]$Force)
 if ($LiteralPath -like '*payload*BlasphemousTrainer.dll') { throw 'TEST injected plugin copy failure' }
 Microsoft.PowerShell.Management\Copy-Item -LiteralPath $LiteralPath -Destination $Destination -Force:$Force
}
& '__BACKEND__' -Mode $Mode -GameRoot $GameRoot
exit $LASTEXITCODE
'@
$driverText.Replace('__BACKEND__',(Join-Path $package 'Backend.ps1').Replace("'","''")) | Set-Content -LiteralPath $driver -Encoding UTF8
RunBackend $rollback 'Install' 1 $driver
Assert (-not (Test-Path "$rollback\winhttp.dll")) 'Rollback left created loader'
Assert (-not (Test-Path "$rollback\BepInEx\core\BepInEx.dll")) 'Rollback left created core'
Assert (-not (Test-Path "$rollback\BepInEx\plugins\BlasphemousTrainer\BlasphemousTrainer.dll")) 'Rollback left plugin'
# Simulate an owned previous version, then fail while writing its new manifest.
$upgrade=NewFixture 'upgrade-rollback'
RunBackend $upgrade 'Install'
$upgradePlugin = "$upgrade\BepInEx\plugins\BlasphemousTrainer\BlasphemousTrainer.dll"
$upgradeManifest = "$upgrade\BepInEx\config\local.blasphemous.trainer.install.json"
[IO.File]::WriteAllText($upgradePlugin,'owned-old-plugin')
$oldHash=(Get-FileHash $upgradePlugin).Hash
$owned=Get-Content $upgradeManifest -Raw | ConvertFrom-Json
$owned.PluginSHA256=$oldHash
$owned | ConvertTo-Json -Depth 6 | Set-Content $upgradeManifest -Encoding UTF8
$manifestHash=(Get-FileHash $upgradeManifest).Hash
$upgradeDriver=Join-Path $fixture 'fail-manifest.ps1'
$upgradeDriverText=@'
param($Mode,$GameRoot)
function Set-Content {
 param([Parameter(ValueFromPipeline=$true)]$Value,[string]$LiteralPath,[string]$Encoding)
 process {
  if ($LiteralPath -like '*local.blasphemous.trainer.install.json') { throw 'TEST injected manifest failure' }
  Microsoft.PowerShell.Management\Set-Content -Value $Value -LiteralPath $LiteralPath -Encoding $Encoding
 }
}
& '__BACKEND__' -Mode $Mode -GameRoot $GameRoot
exit $LASTEXITCODE
'@
$upgradeDriverText.Replace('__BACKEND__',(Join-Path $package 'Backend.ps1').Replace("'","''")) | Set-Content -LiteralPath $upgradeDriver -Encoding UTF8
RunBackend $upgrade 'Install' 1 $upgradeDriver
Assert ((Get-FileHash $upgradePlugin).Hash -eq $oldHash) 'Upgrade rollback did not restore old plugin'
Assert ((Get-FileHash $upgradeManifest).Hash -eq $manifestHash) 'Upgrade rollback did not restore manifest'
RunBackend $upgrade 'Install'
Assert ((Get-FileHash $upgradePlugin).Hash -eq (Get-FileHash "$package\payload\BlasphemousTrainer.dll").Hash) 'Upgrade failed'
Assert ((Get-Content "$testHome\AppData\LocalLow\TheGameKitchen\Blasphemous\Savegames\slot.save") -eq 'fixture-save') 'Save changed'
"PASS: $checks installer checks. Fixture logs: $fixture"
