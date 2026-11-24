param(
    [string]$KksGameRoot = 'E:\Galgames\kks',
    [switch]$Deploy,
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot

$common = @('-c', 'Release', '--nologo', '-p:GameFlavor=KKS', "-p:KksGameRoot=$KksGameRoot")
if ($NoRestore) { $common += '--no-restore' }

dotnet build (Join-Path $taskRoot 'src\KoiSync.MainGame\KoiSync.MainGame.csproj') @common
if ($LASTEXITCODE -ne 0) { throw 'KoiSync.MainGame (KKS) build failed' }

dotnet build (Join-Path $taskRoot 'src\KoiSync.Studio\KoiSync.Studio.csproj') @common
if ($LASTEXITCODE -ne 0) { throw 'KoiSync.Studio (KKS) build failed' }

$releaseDir = Join-Path $taskRoot 'artifacts\release-kks'
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

$mainDll = Join-Path $taskRoot 'src\KoiSync.MainGame\bin\Release\net46\KoiSync.MainGame.dll'
$studioDll = Join-Path $taskRoot 'src\KoiSync.Studio\bin\Release\net46\KoiSync.Studio.dll'
Copy-Item -LiteralPath $mainDll -Destination (Join-Path $releaseDir 'KoiSync.MainGame.dll') -Force
Copy-Item -LiteralPath $studioDll -Destination (Join-Path $releaseDir 'KoiSync.Studio.dll') -Force

Get-Item (Join-Path $releaseDir 'KoiSync.MainGame.dll'), (Join-Path $releaseDir 'KoiSync.Studio.dll') | Select-Object FullName, Length, LastWriteTime

if ($Deploy) {
    $mainDst = Join-Path $KksGameRoot 'BepInEx\plugins\KoiSyncMainGame'
    $studioDst = Join-Path $KksGameRoot 'BepInEx\plugins\KoiSyncStudio'
    New-Item -ItemType Directory -Path $mainDst, $studioDst -Force | Out-Null
    Copy-Item -LiteralPath $mainDll -Destination (Join-Path $mainDst 'KoiSync.MainGame.dll') -Force
    Copy-Item -LiteralPath $studioDll -Destination (Join-Path $studioDst 'KoiSync.Studio.dll') -Force
    Write-Output "Deployed KKS plugins to $mainDst and $studioDst"
}
