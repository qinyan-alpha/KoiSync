param([string]$GameRoot='E:\Galgames\kk')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$source=Join-Path $projectRoot 'src\KoiSync.Studio\bin\Release\net35\KoiSync.Studio.dll'
if(!(Test-Path -LiteralPath $source)){throw 'Build KoiSync.Studio first'}
$game=[IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
if(!(Test-Path -LiteralPath (Join-Path $game 'CharaStudio.exe'))){throw 'Not a Studio game directory'}
$running=@(Get-Process -Name CharaStudio -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq (Join-Path $game 'CharaStudio.exe')})
if($running.Count -gt 0){throw 'Close this game directory''s Studio before replacing its loaded plugin'}

# Preserve connection addresses only. Analysis profiles and old mappings are removed.
$oldConfig=Join-Path $game 'BepInEx\config\local.koisync.cfg'
$oldValues=@{}
if(Test-Path -LiteralPath $oldConfig)
{
    foreach($line in Get-Content -LiteralPath $oldConfig)
    {if($line -match '^(Endpoint[0-2]|Argument[0-2]|Transport)\s*=\s*(.*)$'){$oldValues[$Matches[1]]=$Matches[2]}}
}
$install=Join-Path $game 'BepInEx\plugins\KoiSyncStudio'
New-Item -ItemType Directory -Path $install -Force|Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $install 'KoiSync.Studio.dll') -Force
$config=Join-Path $game 'BepInEx\config\local.koisync.studio.cfg'
if(!(Test-Path -LiteralPath $config))
{
    $lines=@('[Device]')
    foreach($key in @('Transport','Endpoint0','Argument0','Endpoint1','Argument1','Endpoint2','Argument2'))
    {if($oldValues.ContainsKey($key)){$lines+=$key+' = '+$oldValues[$key]}}
    $lines|Set-Content -LiteralPath $config -Encoding UTF8
}

$removed=@()
$relativeTargets=@('BepInEx\plugins\KoiSync','UserData\KoiSync','BepInEx\config\local.koisync.cfg','BepInEx\config\KoiSync.scan-stage.txt',
    'scripts\KoiSyncStrokeCapture.cs','scripts\KoiSyncV127Probe.cs','scripts\KoiSyncTimelineHipsApply.cs','scripts\KoiSyncMissionaryLiveBridge.cs')
foreach($relative in $relativeTargets)
{
    $target=[IO.Path]::GetFullPath((Join-Path $game $relative))
    if(!$target.StartsWith($game+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup target escaped game directory'}
    if(Test-Path -LiteralPath $target)
    {
        $item=Get-Item -LiteralPath $target
        if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "Unexpected link at cleanup target $target"}
        $count=if($item.PSIsContainer){@(Get-ChildItem -LiteralPath $target -Recurse -File).Count}else{1}
        Remove-Item -LiteralPath $target -Recurse -Force
        $removed+=[PSCustomObject]@{Path=$target;Files=$count}
    }
}
$out=Join-Path $projectRoot 'artifacts\studio-lite-v020'
New-Item -ItemType Directory -Path $out -Force|Out-Null
[PSCustomObject]@{Version=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $install 'KoiSync.Studio.dll')).Version.ToString();Installed=Join-Path $install 'KoiSync.Studio.dll';Removed=$removed;PreviewZipmodRetained=Test-Path -LiteralPath (Join-Path $game 'mods\qinnyann\KoiSync')}|
    ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $out 'deployment.json') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $out 'deployment.json')
