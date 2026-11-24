param(
    [int]$Mode = 0,
    [string]$Subdir = 'default',
    [string]$Card = '',
    [string]$Command = 'visual-audit',
    [string]$Specs = 'sonyu_0:WLoop,SLoop,OLoop;sonyu_2:WLoop,SLoop;sonyu_2@anal:A_WLoop,A_SLoop;sonyu_4:WLoop,SLoop;houshi_7:WLoop,SLoop;houshi_1:WLoop,SLoop;houshi_11:WLoop,SLoop;aibu_0:aibu;kplug_khs_f_80:WLoop,SLoop',
    [string]$CaseRoot = 'E:\project\c#\KoiSync\artifacts\main-game-visual-v020',
    [int]$TimeoutSeconds = 240
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$caseRoot = [IO.Path]::GetFullPath($CaseRoot)
$runtime = Join-Path $caseRoot 'runtime'
$executable = Join-Path $runtime 'Koikatu.exe'
$auditRoot = Join-Path (Join-Path $caseRoot 'visual-audit') $Subdir

# Stop ONLY any previous isolated validation process matching this exact runtime path
Get-Process -Name Koikatu -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable } | ForEach-Object {
    Stop-Process -InputObject $_ -Force
    $_.WaitForExit()
}

# Sync latest compiled MainGame + MainGameLive DLLs into isolated runtime
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\KoiSync.MainGame\bin\Release\net35\KoiSync.MainGame.dll') -Destination (Join-Path $runtime 'BepInEx\plugins\KoiSyncMainGame\KoiSync.MainGame.dll') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'tests\KoiSync.MainGameLive\bin\Release\net35\KoiSync.MainGameLive.dll') -Destination (Join-Path $runtime 'BepInEx\plugins\KoiSyncMainGame\KoiSync.MainGameLive.dll') -Force

# Clear stale markers
foreach ($m in @('complete.txt', 'fatal-error.txt', 'started.txt')) {
    $mp = Join-Path $auditRoot $m
    if (Test-Path -LiteralPath $mp) { Remove-Item -LiteralPath $mp -Force }
}

$log = Join-Path $caseRoot ("visual-audit-mode-$Mode.log")
$proc = Start-Process -FilePath $executable -WorkingDirectory $runtime `
    -ArgumentList @('--koisync-main-validation', ("--koisync-main-output=$caseRoot"), '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-logFile', $log) `
    -WindowStyle Hidden -PassThru
Write-Host "Started isolated visual-audit host PID=$($proc.Id) Mode=$Mode"

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $snapshot = Join-Path $caseRoot 'live.xml'
    while ((!(Test-Path -LiteralPath $snapshot)) -or (Get-Item -LiteralPath $snapshot).LastWriteTimeUtc -lt $proc.StartTime.ToUniversalTime()) {
        if ($proc.HasExited) { throw "Isolated host exited early (code $($proc.ExitCode))" }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'LiveProbe did not start within 60s' }
        Start-Sleep -Milliseconds 250
    }

    $bootArgs = @('-X', 'utf8', (Join-Path $projectRoot 'tools\main-game-live.py'), '--boot', '--mode', $Mode, '--out', $caseRoot)
    if (![string]::IsNullOrWhiteSpace($Card)) { $bootArgs += @('--card', $Card) }
    & E:\project\c#\KoikatuModFactory\.venv\Scripts\python.exe @bootArgs
    if ($LASTEXITCODE -ne 0) { throw 'FreeH bootstrap failed' }

    Set-Content -LiteralPath (Join-Path $caseRoot 'command.txt') -Value @($Command, "$Subdir|$Specs") -Encoding utf8
    Write-Host "Dispatched $Command [$Subdir] specs: $Specs"

    $completeFile = Join-Path $auditRoot 'complete.txt'
    $fatalFile = Join-Path $auditRoot 'fatal-error.txt'
    $auditDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        if (Test-Path -LiteralPath $completeFile) {
            Write-Host "Visual audit COMPLETE ($(Get-Content -LiteralPath $completeFile))"
            break
        }
        if (Test-Path -LiteralPath $fatalFile) {
            throw ("VisualAudit fatal error:`n" + (Get-Content -LiteralPath $fatalFile -Raw))
        }
        if ($proc.HasExited) {
            throw "Isolated host exited unexpectedly during VisualAudit (code $($proc.ExitCode))"
        }
        if ([DateTime]::UtcNow -ge $auditDeadline) {
            throw "VisualAudit timed out after ${TimeoutSeconds}s"
        }
        Start-Sleep -Milliseconds 500
    }
}
finally {
    if ($proc -and !$proc.HasExited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        $proc.WaitForExit(5000) | Out-Null
        Write-Host "Stopped isolated visual-audit host PID=$($proc.Id)"
    }
}
