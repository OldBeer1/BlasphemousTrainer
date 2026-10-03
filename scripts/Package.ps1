$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$version = & (Join-Path $PSScriptRoot 'Read-Version.ps1')
$out = Join-Path $project ('dist\BlasphemousTrainer-'+$version)
New-Item -ItemType Directory -Path (Join-Path $out 'payload') -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:anycpu /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /out:"$out\安装工具.exe" "$project\installer\Installer.cs" "$project\src\BlasphemousTrainer\ProjectInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
[IO.File]::WriteAllText((Join-Path $out 'Backend.ps1'), [IO.File]::ReadAllText((Join-Path $project 'installer\Backend.ps1')), [Text.UTF8Encoding]::new($true))
Copy-Item -LiteralPath (Join-Path $project 'dist\BlasphemousTrainer\BlasphemousTrainer.dll') -Destination (Join-Path $out 'payload') -Force
[ordered]@{Version=$version;PluginSHA256=(Get-FileHash -LiteralPath (Join-Path $out 'payload\BlasphemousTrainer.dll')).Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'release.json') -Encoding UTF8
New-Item -ItemType Directory -Path (Join-Path $out 'docs') -Force | Out-Null
$currentDocs = @('开发与维护.md')
foreach ($doc in $currentDocs) { Copy-Item -LiteralPath (Join-Path $project ('docs\'+$doc)) -Destination (Join-Path $out 'docs') -Force }
Copy-Item -LiteralPath (Join-Path $project 'README.md') -Destination $out -Force
$allowed = @('安装工具.exe','Backend.ps1','release.json','payload\BlasphemousTrainer.dll')
$allowed += @('README.md') + @($currentDocs | ForEach-Object { 'docs\'+$_ })
foreach ($file in Get-ChildItem -LiteralPath $out -Recurse -File) { if ($file.FullName.Substring($out.Length+1) -notin $allowed) { throw "Unexpected package file: $($file.Name)" } }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath (Join-Path $project ('dist\BlasphemousTrainer-'+$version+'.zip')) -Force
$sourceOut = Join-Path $project ('.work\source-package-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sourceOut -Force | Out-Null
foreach ($folder in @('src','installer','scripts','tests','docs')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $project $folder) -Recurse -File) {
        $relative = $file.FullName.Substring($project.Length+1)
        if ($relative -match '\\(bin|obj)\\' -or $file.Extension -notin @('.cs','.csproj','.ps1','.md','.json','.png')) { continue }
        $target = Join-Path $sourceOut $relative
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
foreach ($file in @('README.md','BlasphemousTrainer.sln','global.json','.gitignore')) { Copy-Item -LiteralPath (Join-Path $project $file) -Destination $sourceOut }
Compress-Archive -Path (Join-Path $sourceOut '*') -DestinationPath (Join-Path $project ('dist\BlasphemousTrainer-'+$version+'-source.zip')) -Force
# Remove only this run's generated staging folder, never retained source or test data.
$sourceFull = [IO.Path]::GetFullPath($sourceOut)
$workFull = [IO.Path]::GetFullPath((Join-Path $project '.work')).TrimEnd('\')
if (-not $sourceFull.StartsWith($workFull+'\',[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $sourceFull -Leaf) -notmatch '^source-package-[0-9a-f]{32}$') { throw 'Unexpected source staging cleanup path.' }
if ((Get-Item -LiteralPath $sourceFull).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source staging is a directory link.' }
if (@(Get-ChildItem -LiteralPath $sourceFull -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Source staging contains a link.' }
Remove-Item -LiteralPath $sourceFull -Recurse -Force
Write-Output "Package ready: $out"
