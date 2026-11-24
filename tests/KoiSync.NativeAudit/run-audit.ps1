$ErrorActionPreference = 'Stop'
$auditProject = $PSScriptRoot
$projectRoot = [IO.Path]::GetFullPath((Join-Path $auditProject '..\..'))
$runtimeRoot = Join-Path $projectRoot 'artifacts\runtime-smoke'
$auditOutput = Join-Path $projectRoot 'artifacts\native-audit-20260913'
$runtimeExe = Join-Path $runtimeRoot 'Koikatu.exe'
$runtimeConfig = Join-Path $runtimeRoot 'BepInEx\config\local.koisync.cfg'
if (-not (Test-Path -LiteralPath $runtimeExe)) { throw 'Isolated Koikatu runtime is missing.' }
$configText = Get-Content -LiteralPath $runtimeConfig -Raw
if ($configText -notmatch '(?m)^AutoConnect\s*=\s*false\s*$' -or $configText -notmatch '(?m)^AutoEnableSync\s*=\s*false\s*$') { throw 'Audit requires AutoConnect=false and AutoEnableSync=false in the isolated runtime.' }
if (Get-CimInstance Win32_Process -Filter "Name='Koikatu.exe'" | Where-Object ExecutablePath -eq $runtimeExe) { throw 'The isolated runtime is already running.' }
dotnet build (Join-Path $auditProject 'KoiSync.NativeAudit.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Audit build failed.' }
Copy-Item -LiteralPath (Join-Path $auditProject 'bin\Release\net35\KoiSync.RuntimeChecks.dll') -Destination (Join-Path $runtimeRoot 'BepInEx\plugins\KoiSync.RuntimeChecks.dll') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\KoiSync.Plugin\bin\Release\net35\KoiSync.Plugin.dll') -Destination (Join-Path $runtimeRoot 'BepInEx\plugins\KoiSync.Plugin.dll') -Force
foreach ($name in @('complete.txt','progress.txt')) { $artifact=Join-Path $auditOutput $name; if (Test-Path -LiteralPath $artifact) { Remove-Item -LiteralPath $artifact } }
$process = Start-Process -FilePath $runtimeExe -WorkingDirectory $runtimeRoot -ArgumentList '--koisync-native-audit -screen-width 800 -screen-height 600 -screen-fullscreen 0 -logFile native-audit.log' -WindowStyle Hidden -PassThru
try {
    $deadline = (Get-Date).AddMinutes(15)
    while (-not (Test-Path -LiteralPath (Join-Path $auditOutput 'complete.txt'))) {
        if ($process.HasExited) { throw 'Audit runtime exited before completion. Check catalog-run.txt and native-audit.log.' }
        if ((Get-Date) -ge $deadline) { throw 'Audit timed out. Partial evidence remains in the output directory.' }
        Start-Sleep -Seconds 3
    }
    python -X utf8 (Join-Path $auditProject 'summarize.py')
    if ($LASTEXITCODE -ne 0) { throw 'Audit summary failed.' }
} finally {
    if (-not $process.HasExited) {
        if ($process.Path -ne $runtimeExe) { throw 'Refusing to stop a process outside the isolated runtime.' }
        Stop-Process -Id $process.Id
        $process.WaitForExit()
    }
}
Write-Output "Audit complete: $auditOutput"
