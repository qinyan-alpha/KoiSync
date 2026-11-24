param(
    [string]$GameRoot = 'E:\Galgames\kk',
    [switch]$SkipChecks,
    [switch]$NoRestore,
    [switch]$MainGameReleaseOnly
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$buildArguments = @('build', (Join-Path $taskRoot 'KoiSync.sln'), '-c', 'Release', '--nologo', "-p:GameRoot=$GameRoot")
if ($NoRestore) { $buildArguments += '--no-restore' }
dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'KoiSync solution build failed' }

if (-not $SkipChecks) {
    foreach ($suite in @('KoiSync.Checks', 'KoiSync.StudioDriveChecks', 'KoiSync.MainGameChecks')) {
        dotnet (Join-Path $taskRoot "tests\$suite\bin\Release\net10.0\$suite.dll")
        if ($LASTEXITCODE -ne 0) { throw "$suite failed" }
    }
    & (Join-Path $taskRoot 'tests\KoiSync.TransportChecks\bin\Release\net48\KoiSync.TransportChecks.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Transport checks failed' }
}

$taskRelease = Join-Path $taskRoot 'artifacts\release'
$mainDll = Join-Path $taskRoot 'src\KoiSync.MainGame\bin\Release\net35\KoiSync.MainGame.dll'
$mainVersion = [Reflection.AssemblyName]::GetAssemblyName($mainDll).Version.ToString(3)
$mainRelease = Join-Path $taskRelease "KoiSync.MainGame-$mainVersion"
New-Item -ItemType Directory -Path $mainRelease -Force | Out-Null
Copy-Item -LiteralPath $mainDll -Destination $mainRelease -Force
Get-Item -LiteralPath (Join-Path $mainRelease 'KoiSync.MainGame.dll') | Select-Object FullName, Length
if (-not $MainGameReleaseOnly) {
    $studioRelease = Join-Path $taskRelease 'KoiSync.Studio.dll'
    Copy-Item -LiteralPath (Join-Path $taskRoot 'src\KoiSync.Studio\bin\Release\net35\KoiSync.Studio.dll') -Destination $studioRelease
    Get-Item -LiteralPath $studioRelease | Select-Object FullName, Length
}
