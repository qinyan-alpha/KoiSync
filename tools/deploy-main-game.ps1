param(
    [string]$GameRoot='E:\Galgames\kk',
    [switch]$SkipBuild,
    [string]$SourceDll,
    [switch]$TryWhileRunning
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$game=[IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
if(!(Test-Path -LiteralPath (Join-Path $game 'Koikatu.exe'))){throw 'Not a Koikatu game directory'}
$gameExe=Join-Path $game 'Koikatu.exe'
$running=@(Get-Process -Name Koikatu -ErrorAction SilentlyContinue | Where-Object {!$_.Path -or $_.Path -eq $gameExe})
if($running.Count -gt 0 -and !$TryWhileRunning){throw 'Close Koikatu before replacing the main-game plugin; Studio can remain open'}
if(!$SkipBuild){
    & (Join-Path $projectRoot 'build.ps1') -GameRoot $game -MainGameReleaseOnly
}
$source=if($SourceDll){(Resolve-Path -LiteralPath $SourceDll).ProviderPath}else{Join-Path $projectRoot 'src\KoiSync.MainGame\bin\Release\net35\KoiSync.MainGame.dll'}
$assembly=[Reflection.AssemblyName]::GetAssemblyName($source)
if($assembly.Name -ne 'KoiSync.MainGame'){throw 'The source is not KoiSync.MainGame.dll'}
$install=Join-Path $game 'BepInEx\plugins\KoiSyncMainGame'
New-Item -ItemType Directory -Path $install -Force | Out-Null
$destination=Join-Path $install 'KoiSync.MainGame.dll'
$staged=Join-Path $install ('KoiSync.MainGame.'+[Guid]::NewGuid().ToString('N')+'.tmp')
$backup=$null
try {
    Copy-Item -LiteralPath $source -Destination $staged
    if(Test-Path -LiteralPath $destination){
        $backup=$destination+'.'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'.backup'
        [IO.File]::Replace($staged,$destination,$backup)
    } else {
        Move-Item -LiteralPath $staged -Destination $destination
    }
} finally {
    if(Test-Path -LiteralPath $staged){Remove-Item -LiteralPath $staged}
}
[PSCustomObject]@{Plugin=$destination;Version=[Reflection.AssemblyName]::GetAssemblyName($destination).Version.ToString();Backup=$backup;RunningKoikatu=@($running | Select-Object -ExpandProperty Id);Config=Join-Path $game 'BepInEx\config\local.koisync.maingame.cfg';Data=Join-Path $game 'UserData\KoiSyncMainGame'} | ConvertTo-Json
