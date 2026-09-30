$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$game = Split-Path $project -Parent
New-Item -ItemType Directory -Path (Join-Path $project '.work') -Force | Out-Null
$out = Join-Path $project '.work\RulesTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:$out "$project\tests\RulesTests.cs" "$project\src\BlasphemousTrainer\TrainerRules.cs" "$project\src\BlasphemousTrainer\DefenseRules.cs" "$project\src\BlasphemousTrainer\PlaytimeRules.cs"
if ($LASTEXITCODE) { throw 'Test compilation failed.' }
& $out
if ($LASTEXITCODE) { throw 'Numeric tests failed.' }
$pointerOut = Join-Path $project '.work\PointerTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:$pointerOut "$project\tests\PointerTests.cs" "$project\src\BlasphemousTrainer\PointerClick.cs"
if ($LASTEXITCODE) { throw 'Pointer test compilation failed.' }
& $pointerOut
if ($LASTEXITCODE) { throw 'Pointer tests failed.' }
$panelOut = Join-Path $project '.work\PanelInputTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:$panelOut "$project\tests\PanelInputTests.cs" "$project\src\BlasphemousTrainer\PanelInputRules.cs"
if ($LASTEXITCODE) { throw 'Panel input test compilation failed.' }
& $panelOut
if ($LASTEXITCODE) { throw 'Panel input tests failed.' }
$memoryOut = Join-Path $project '.work\MemoryTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:$memoryOut "$project\tests\MemoryTests.cs" "$project\src\BlasphemousTrainer\CheatMemory.cs" "$project\src\BlasphemousTrainer\FeatureCatalog.cs"
if ($LASTEXITCODE) { throw 'Memory test compilation failed.' }
& $memoryOut
if ($LASTEXITCODE) { throw 'Memory tests failed.' }
$stateOut = Join-Path $project '.work\StateFlowTests.exe'
$stateSources = @('PlaytimeGuard','PlaytimeRules','FeatureSelection','DefenseRules','CheatMemory','FeatureCatalog','NoticeLayout','ControllerMapCache','PointerClick','PanelInputRules') | ForEach-Object { "$project\src\BlasphemousTrainer\$_.cs" }
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:$stateOut "$project\tests\StateFlowTests.cs" $stateSources
if ($LASTEXITCODE) { throw 'State flow test compilation failed.' }
& $stateOut
if ($LASTEXITCODE) { throw 'State flow tests failed.' }
Add-Type -Path (Join-Path $project '.tools\BepInEx-5.4.23.5\BepInEx\core\Mono.Cecil.dll')
$assemblies = @{}
foreach ($name in @('Assembly-CSharp','Assembly-CSharp-firstpass')) { $assemblies[$name] = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $game "Blasphemous_Data\Managed\$name.dll")) }
function Method($assembly, $type, $name, $parameters) {
    $t = @($assemblies[$assembly].MainModule.Types | Where-Object FullName -eq $type)
    if ($type -eq 'Framework.Managers.PoolManager/ObjectInstance') { $t = @($assemblies[$assembly].MainModule.Types | Where-Object FullName -eq 'Framework.Managers.PoolManager').NestedTypes | Where-Object Name -eq 'ObjectInstance' }
    $m = @($t.Methods | Where-Object { $_.Name -eq $name -and $_.Parameters.Count -eq $parameters })
    if ($m.Count -ne 1 -or -not $m[0].HasBody) { throw "Method mismatch: $type.$name" }
    return $m[0]
}
function Calls($method, $name, $count) {
    $found = @($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name }).Count
    if ($found -ne $count) { throw "IL mismatch: $($method.FullName) $name expected $count found $found" }
}
$controller = Method 'Assembly-CSharp-firstpass' 'CreativeSpore.SmartColliders.PlatformCharacterController' 'Update' 0
Calls $controller 'get_MaxWalkingSpeed' 1
Calls $controller 'get_HorizontalMovingAcc' 2
Calls $controller 'get_JumpingSpeed' 1
$reward = Method 'Assembly-CSharp' 'Gameplay.GameControllers.Penitent.Penitent' 'GetPurge' 1
Calls $reward 'set_Current' 1
foreach ($type in @('Tools.Items.PenitentCrawlerOrbsEffect','Tools.Items.PenitentLightBeamEffect')) {
    $m = Method 'Assembly-CSharp' $type 'OnApplyEffect' 0
    if ($m.ReturnType.FullName -ne 'System.Boolean') { throw 'Prayer return mismatch.' }
}
$null = Method 'Assembly-CSharp' 'Framework.Inventory.ToxicCloudEffect' 'InstantiateToxicCloud' 0
$null = Method 'Assembly-CSharp' 'Framework.Managers.PoolManager/ObjectInstance' 'Reuse' 2
$null = Method 'Assembly-CSharp' 'Gameplay.GameControllers.Enemies.Framework.Damage.EnemyDamageArea' 'TakeDamageAmount' 1
# Intro skip patch targets: splash video Awake, Landing.Start and the preloadMenu field.
$null = Method 'Assembly-CSharp' 'BlasphemousSplashScreen' 'Awake' 0
$null = Method 'Assembly-CSharp' 'Gameplay.UI.Others.MenuLogic.Landing' 'Start' 0
$landing = @($assemblies['Assembly-CSharp'].MainModule.Types | Where-Object FullName -eq 'Gameplay.UI.Others.MenuLogic.Landing')
$preload = @($landing.Fields | Where-Object Name -eq 'preloadMenu')
if ($preload.Count -ne 1 -or $preload[0].FieldType.FullName -ne 'UnityEngine.AsyncOperation') { throw 'Landing.preloadMenu mismatch.' }
# Reward transpiler is scoped to GetPurge: manual addition and refunds never enter that method.
foreach ($type in @('Framework.Managers.SkillManager','Framework.Managers.GuiltManager','Framework.Managers.PersistentManager')) {
    foreach ($m in ($assemblies['Assembly-CSharp'].MainModule.Types | Where-Object FullName -eq $type).Methods) { if ($m.HasBody) { Calls $m 'GetPurge' 0 } }
}
# Menu time guard: the clock is realtime-driven, and both of its read paths must be patchable.
$clock = Method 'Assembly-CSharp' 'Framework.Managers.PersistentManager' 'GetCurrentTimePlayed' 0
if ($clock.ReturnType.FullName -ne 'System.Single') { throw 'GetCurrentTimePlayed return mismatch.' }
Calls $clock 'get_realtimeSinceStartup' 1
$state = Method 'Assembly-CSharp' 'Framework.Managers.PersistentManager' 'GetCurrentPersistentState' 2
if ($state.ReturnType.FullName -ne 'Framework.Managers.PersistentManager/PersistentData') { throw 'GetCurrentPersistentState return mismatch.' }
Calls $state 'get_realtimeSinceStartup' 1
Calls (Method 'Assembly-CSharp' 'Framework.Managers.PersistentManager' 'GetCurrentTimePlayedForAC44' 0) 'GetCurrentTimePlayed' 1
# Baseline events that restart the running exclusion: load and new game.
$null = Method 'Assembly-CSharp' 'Framework.Managers.PersistentManager' 'SetCurrentPersistentState' 3
$null = Method 'Assembly-CSharp' 'Framework.Managers.PersistentManager' 'ResetPersistence' 0
$uiController = @($assemblies['Assembly-CSharp'].MainModule.Types | Where-Object FullName -eq 'Gameplay.UI.UIController')
$isShowing = @($uiController.Properties | Where-Object Name -eq 'IsShowingMenu')
if ($isShowing.Count -ne 1 -or $isShowing[0].PropertyType.FullName -ne 'System.Boolean' -or -not $isShowing[0].GetMethod.IsPublic) { throw 'UIController.IsShowingMenu mismatch.' }
$uiInstance = @($uiController.Fields | Where-Object { $_.Name -eq 'instance' -and $_.IsStatic })
if ($uiInstance.Count -ne 1 -or $uiInstance[0].FieldType.FullName -ne 'Gameplay.UI.UIController') { throw 'UIController.instance mismatch.' }
$ac44 = @($assemblies['Assembly-CSharp'].MainModule.Types | Where-Object Name -eq 'AC44Checker')
if ($ac44.Count -ne 1 -or @($ac44.Fields | Where-Object Name -eq 'MaximunMinutesForAC44').Count -ne 1) { throw 'AC44Checker mismatch.' }
$rulesAssembly = [Reflection.Assembly]::LoadFile($panelOut)
$gateRule = $rulesAssembly.GetType('BlasphemousTrainer.PanelGateRules').GetMethod('ReturnKind', [Reflection.BindingFlags]'Static,NonPublic')
function AllTypes($types) { foreach ($t in $types) { $t; if ($t.HasNestedTypes) { AllTypes $t.NestedTypes } } }
$inputReads = @{}
foreach ($assembly in $assemblies.Values) {
    foreach ($t in (AllTypes $assembly.MainModule.Types)) {
        foreach ($m in $t.Methods) {
            if (-not $m.HasBody) { continue }
            foreach ($instruction in $m.Body.Instructions) {
                $reference = $instruction.Operand
                if ($reference -isnot [Mono.Cecil.MethodReference] -or $reference.DeclaringType.FullName -ne 'Rewired.Player' -or -not $reference.Name.StartsWith('Get', [StringComparison]::Ordinal)) { continue }
                $expected = if ($reference.ReturnType.FullName -eq 'System.Boolean') { 1 } elseif ($reference.ReturnType.FullName -eq 'System.Single') { 2 } else { 0 }
                if ($expected -eq 0) { continue }
                if ($gateRule.Invoke($null, @($reference.Name)) -ne $expected) { throw "Uncovered game input read: $($reference.FullName)" }
                $inputReads[$reference.FullName] = $true
            }
        }
    }
}
"PASS: all $($inputReads.Count) distinct Rewired input reads used by both game assemblies covered by production gate rules."
foreach ($assembly in $assemblies.Values) { $assembly.Dispose() }
'PASS: extension target methods, movement/reward IL call sites, manual/load/refund isolation.'
