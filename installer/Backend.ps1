param(
    [ValidateSet('Check','Install','Uninstall','Backup')][string]$Mode = 'Check',
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [switch]$RemoveConfig
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Archive\Microsoft.PowerShell.Archive.psd1') -ErrorAction Stop
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$root = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
$package = $PSScriptRoot
$pluginRelative = 'BepInEx\plugins\BlasphemousTrainer\BlasphemousTrainer.dll'
$configRelative = 'BepInEx\config\local.blasphemous.trainer.cfg'
$manifestRelative = 'BepInEx\config\local.blasphemous.trainer.install.json'
function SafePath([string]$relative) {
    if ([IO.Path]::IsPathRooted($relative)) { throw '清单包含绝对路径。' }
    $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $path.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '目标超出游戏目录。' }
    $check = $path
    while ($check -and $check.Length -ge $root.Length) {
        if ((Test-Path -LiteralPath $check) -and ((Get-Item -LiteralPath $check).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '安装路径含目录链接，请使用实际游戏目录。' }
        $check = Split-Path $check -Parent
    }
    return $path
}
function Hash([string]$path) { return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function BackupSaves {
    $source = Join-Path $env:USERPROFILE 'AppData\LocalLow\TheGameKitchen\Blasphemous'
    if (-not (Test-Path -LiteralPath $source)) { Write-Output '未发现本机默认存档目录，未创建存档备份。'; return }
    $destination = Join-Path $env:LOCALAPPDATA ('BlasphemousTrainerBackups\'+[Guid]::NewGuid().ToString('N'))
    $records = @()
    foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) {
        $relative = $file.FullName.Substring($source.Length+1)
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        $before = Hash $file.FullName
        Copy-Item -LiteralPath $file.FullName -Destination $target
        if ((Hash $target) -ne $before -or (Hash $file.FullName) -ne $before) { throw '存档备份校验失败，已停止安装。' }
        $records += [pscustomobject]@{Path=$relative;SHA256=$before}
    }
    if ($records.Count -eq 0) { throw '存档目录为空，请核实位置。' }
    $records | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'backup-manifest.json') -Encoding UTF8
    Write-Output "存档备份并校验完成：$destination"
}
function GetLoader {
    $cache = Join-Path $env:LOCALAPPDATA 'BlasphemousTrainerInstaller\BepInEx-5.4.23.5'
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $zip = Join-Path $cache 'BepInEx_win_x64_5.4.23.5.zip'
    $expected = '82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4'
    if (-not (Test-Path -LiteralPath $zip) -or (Hash $zip) -ne $expected) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip' -OutFile $zip
    }
    if ((Hash $zip) -ne $expected) { throw '官方加载器下载校验失败。' }
    $expanded = Join-Path $cache ([Guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $zip -DestinationPath $expanded
    return $expanded
}
try {
    $mutex = New-Object Threading.Mutex($false, 'Local\BlasphemousTrainerInstaller')
    if (-not $mutex.WaitOne(0)) { throw '另一个安装工具正在操作，请等待其结束。' }
    if (Get-Process Blasphemous -ErrorAction SilentlyContinue) { throw '请先退出游戏，再进行检查、安装、卸载或备份。' }
    $exe = SafePath 'Blasphemous.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw '请选择包含 Blasphemous.exe 的游戏目录。' }
    if ($Mode -eq 'Backup') { BackupSaves; exit 0 }
    $manifestPath = SafePath $manifestRelative
    $plugin = SafePath $pluginRelative
    $old = $null
    if (Test-Path -LiteralPath $manifestPath) { $old = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json }
    # Recognize the earlier development deployment without taking ownership of its loader.
    if (-not $old) {
        $legacy = SafePath 'TrainerSource\artifacts\installation-manifest.json'
        if (Test-Path -LiteralPath $legacy) {
            $record = @((Get-Content -LiteralPath $legacy -Raw | ConvertFrom-Json).Files | Where-Object RelativePath -eq $pluginRelative)
            if ($record.Count -eq 1) { $old = [pscustomobject]@{PluginSHA256=$record[0].SHA256} }
        }
    }
    if ($Mode -eq 'Uninstall') {
        if (-not (Test-Path -LiteralPath $plugin)) { Write-Output '本插件未安装；没有删除其他文件。'; exit 0 }
        if (-not $old -or (Hash $plugin) -ne $old.PluginSHA256) { throw '插件内容与安装清单不一致，未卸载。' }
        $archive = SafePath ('TrainerBackups\uninstall-'+[Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $archive -Force | Out-Null
        $targets = @($plugin)
        if (Test-Path -LiteralPath $manifestPath) { $targets += $manifestPath }
        $config = SafePath $configRelative
        if ($RemoveConfig -and (Test-Path -LiteralPath $config)) { $targets += $config }
        $removed = @()
        try {
            foreach ($target in $targets) { Copy-Item -LiteralPath $target -Destination (Join-Path $archive ([IO.Path]::GetFileName($target))); $removed += $target; Remove-Item -LiteralPath $target }
        } catch {
            foreach ($target in $removed) { Copy-Item -LiteralPath (Join-Path $archive ([IO.Path]::GetFileName($target))) -Destination $target -Force }
            throw
        }
        Write-Output "已卸载本插件；共享加载器、其他 MOD 和存档保留。备份：$archive"; exit 0
    }
    if ((Hash $exe) -ne '3FF1A014FD0366747FB6D283C9494A5A8CFF9BC19239FE97B9DC12D831369477' -or
        (Hash (SafePath 'Blasphemous_Data\Managed\Assembly-CSharp.dll')) -ne 'F12C2F1D40AEF47287F07E910A16904DEEB11BB2D007331F30E50327C128A4CE' -or
        (Hash (SafePath 'Blasphemous_Data\Managed\Assembly-CSharp-firstpass.dll')) -ne '992A8AB3C4C4AD9AE774FB5BD96FF08077A4A76835F0EA1C9A4C87E5E243C024') { throw '游戏版本或位数不在已支持的指纹范围内，停止安装。' }
    foreach ($conflict in @('Modding','version.dll','doorstop.dll')) { if (Test-Path -LiteralPath (SafePath $conflict)) { throw "发现未核验加载器冲突：$conflict" } }
    $payload = Join-Path $package 'payload\BlasphemousTrainer.dll'
    $release = Get-Content -LiteralPath (Join-Path $package 'release.json') -Raw | ConvertFrom-Json
    if ((Hash $payload) -ne $release.PluginSHA256) { throw '发行包插件校验失败，请重新解压完整包。' }
    if ((Test-Path -LiteralPath $plugin) -and (Hash $plugin) -ne $release.PluginSHA256 -and (-not $old -or (Hash $plugin) -ne $old.PluginSHA256)) { throw '同名插件来源或内容不明，未覆盖。' }
    $loader = GetLoader
    $plan = @()
    foreach ($source in Get-ChildItem -LiteralPath $loader -Recurse -File) {
        $relative = $source.FullName.Substring($loader.Length+1)
        if ($relative -eq 'changelog.txt') { continue }
        $target = SafePath $relative
        if ((Test-Path -LiteralPath $target) -and (Hash $target) -ne (Hash $source.FullName)) { throw "已有加载器文件不匹配，未覆盖：$relative" }
        if (-not (Test-Path -LiteralPath $target)) { $plan += [pscustomobject]@{Source=$source.FullName;Target=$target;Relative=$relative;SHA256=(Hash $source.FullName)} }
    }
    if (-not (Test-Path -LiteralPath $plugin) -or (Hash $plugin) -ne $release.PluginSHA256) { $plan += [pscustomobject]@{Source=$payload;Target=$plugin;Relative=$pluginRelative;SHA256=$release.PluginSHA256} }
    Write-Output "目标：$root`r`n支持的游戏指纹已匹配；待创建或更新文件数：$($plan.Count)"
    if ($Mode -eq 'Check') { $plan | ForEach-Object { Write-Output $_.Relative }; Write-Output '这只是文件检查，不代表游戏加载或功能已验收。'; exit 0 }
    BackupSaves
    if (Get-Process Blasphemous -ErrorAction SilentlyContinue) { throw '游戏已启动，停止部署。' }
    $archive = SafePath ('TrainerBackups\install-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $archive -Force | Out-Null
    $changes = @()
    $config = SafePath $configRelative
    if (Test-Path -LiteralPath $config) { Copy-Item -LiteralPath $config -Destination (Join-Path $archive 'previous-config.cfg') }
    try {
        foreach ($item in $plan) {
            $backup = $null
            if (Test-Path -LiteralPath $item.Target) { $backup = Join-Path $archive ([Guid]::NewGuid().ToString('N')); Copy-Item -LiteralPath $item.Target -Destination $backup }
            $changes += [pscustomobject]@{Target=$item.Target;Backup=$backup}
            New-Item -ItemType Directory -Path (Split-Path $item.Target -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $item.Source -Destination $item.Target -Force
            if ((Hash $item.Target) -ne $item.SHA256) { throw '安装后的文件校验失败。' }
        }
        $backup = $null
        if (Test-Path -LiteralPath $manifestPath) { $backup = Join-Path $archive 'previous-manifest.json'; Copy-Item -LiteralPath $manifestPath -Destination $backup }
        $changes += [pscustomobject]@{Target=$manifestPath;Backup=$backup}
        New-Item -ItemType Directory -Path (Split-Path $manifestPath -Parent) -Force | Out-Null
        [ordered]@{Version=$release.Version;PluginSHA256=$release.PluginSHA256;InstalledAt=(Get-Date -Format o);CreatedLoaderFiles=@($plan | Where-Object Relative -ne $pluginRelative | Select-Object Relative,SHA256);RuntimeVerified=$false} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
        $changes | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $archive 'rollback-files.json') -Encoding UTF8
    } catch {
        [array]::Reverse($changes)
        foreach ($change in $changes) { if ($change.Backup) { Copy-Item -LiteralPath $change.Backup -Destination $change.Target -Force } elseif (Test-Path -LiteralPath $change.Target) { Remove-Item -LiteralPath $change.Target } }
        throw
    }
    Write-Output "v$($release.Version) 文件安装完成；游戏内加载和新增功能仍需测试。`r`n更新备份：$archive"
} catch { Write-Output ('操作停止：'+$_.Exception.Message); exit 1 }
