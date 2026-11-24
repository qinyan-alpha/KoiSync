param(
    [ValidateSet("zh", "ja", "en")]
    [string]$Language = "zh",
    [int]$Port = 41295,
    [string]$KoiSyncUrl = "http://127.0.0.1:41289",
    [switch]$Restart,
    [switch]$Stop
)

$ErrorActionPreference = "SilentlyContinue"
$selfPid = $PID

function Stop-ExistingVoiceDirector {
    try {
        Invoke-RestMethod -Uri ("http://127.0.0.1:$Port/shutdown") -Method Post -ContentType "application/json" -Body '{}' -TimeoutSec 1 | Out-Null
        Start-Sleep -Milliseconds 500
    } catch {}

    $existing = Get-CimInstance Win32_Process | Where-Object {
        $_.ProcessId -ne $selfPid -and (
            $_.CommandLine -like '*voice_director\server.py*' -or
            $_.CommandLine -like '*voice_director/server.py*'
        )
    }
    foreach ($p in $existing) {
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

if ($Stop) {
    Write-Host "[KoiSync.VoiceDirector] Stopping background service on port $Port..." -ForegroundColor Yellow
    Stop-ExistingVoiceDirector
    Write-Host "[KoiSync.VoiceDirector] Stopped." -ForegroundColor Green
    exit 0
}

# Single-instance launcher mutex prevents duplicate CUDA loads on rapid clicks!
$createdNew = $false
$mutexName = "Local\KoiSync_VoiceDirector_Launch_$Port"
$mutex = New-Object System.Threading.Mutex($true, $mutexName, [ref]$createdNew)
if (-not $createdNew) {
    Write-Host "[KoiSync.VoiceDirector] Another launcher is already starting/running on port $Port. Exiting safely." -ForegroundColor Yellow
    exit 0
}

try {
    if (-not $Restart) {
        try {
            $health = Invoke-RestMethod -Uri ("http://127.0.0.1:$Port/health") -TimeoutSec 1
            if ($health -and $health.ok) {
                Write-Host "[KoiSync.VoiceDirector] Already online on http://127.0.0.1:$Port (skipping duplicate launch)." -ForegroundColor Green
                exit 0
            }
        } catch {}
    }

    # Clean up any half-started / zombie voice_director server.py before launching a single clean instance
    Stop-ExistingVoiceDirector

    $env:HF_HOME = "E:\project\c#\KoiSync\artifacts\voice_lab\hf_cache"
    $env:HUGGINGFACE_HUB_CACHE = "$env:HF_HOME\hub"
    $env:TORCH_HOME = "E:\project\c#\KoiSync\artifacts\voice_lab\torch_cache"
    $env:TMP = "E:\project\c#\KoiSync\artifacts\voice_lab\tmp"
    $env:TEMP = $env:TMP
    $env:HF_HUB_OFFLINE = "1"
    $env:TRANSFORMERS_OFFLINE = "1"

    $py = "E:\project\c#\KoiSync\artifacts\voice_lab\.venv\Scripts\python.exe"
    $srv = "E:\project\c#\KoiSync\voice_director\server.py"

    Write-Host "Starting KoiSync.VoiceDirector v0.4.1 (single-instance, lang=$Language, port=$Port, koisync=$KoiSyncUrl)..." -ForegroundColor Cyan
    Write-Host "Live Co-Play Console: http://127.0.0.1:$Port/ui" -ForegroundColor Green
    & $py $srv --lang $Language --port $Port --koisync-url $KoiSyncUrl
}
finally {
    try { $mutex.ReleaseMutex() } catch {}
    try { $mutex.Dispose() } catch {}
}
