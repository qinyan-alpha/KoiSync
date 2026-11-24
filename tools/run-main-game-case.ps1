param([int]$Mode=0,[string]$Group='aibu',[string]$CaseRoot='')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if([string]::IsNullOrWhiteSpace($CaseRoot)){$caseRoot=Join-Path $projectRoot 'artifacts\main-game-v010'}else{$caseRoot=[IO.Path]::GetFullPath($CaseRoot)}
$runtime=Join-Path $caseRoot 'runtime'
$executable=Join-Path $runtime 'Koikatu.exe'
Get-Process -Name Koikatu -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $executable} | ForEach-Object {Stop-Process -InputObject $_; $_.WaitForExit()}
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\KoiSync.MainGame\bin\Release\net35\KoiSync.MainGame.dll') -Destination (Join-Path $runtime 'BepInEx\plugins\KoiSyncMainGame\KoiSync.MainGame.dll') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'tests\KoiSync.MainGameLive\bin\Release\net35\KoiSync.MainGameLive.dll') -Destination (Join-Path $runtime 'BepInEx\plugins\KoiSyncMainGame\KoiSync.MainGameLive.dll') -Force
$log=Join-Path $caseRoot ('case-'+$Mode+'-'+$Group+'.log')
$nativeProcess=Start-Process -FilePath $executable -WorkingDirectory $runtime -ArgumentList @('--koisync-main-validation',('--koisync-main-output='+$caseRoot),'-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',$log) -WindowStyle Hidden -PassThru
Write-Output ('Case PID '+$nativeProcess.Id+' mode '+$Mode+' group '+$Group)
# Wait until this process has replaced the previous probe's snapshot.
$deadline=[DateTime]::UtcNow.AddSeconds(40)
$snapshot=Join-Path $caseRoot 'live.xml'
while((!(Test-Path -LiteralPath $snapshot)) -or (Get-Item -LiteralPath $snapshot).LastWriteTimeUtc -lt $nativeProcess.StartTime.ToUniversalTime()){
    if([DateTime]::UtcNow -ge $deadline){throw 'Live probe did not start'}
    Start-Sleep -Milliseconds 200
}
python -X utf8 (Join-Path $projectRoot 'tools\main-game-live.py') --boot --mode $Mode --out $caseRoot
if($LASTEXITCODE -ne 0){throw 'Native scene bootstrap failed'}
if(![string]::IsNullOrWhiteSpace($Group)){
    Set-Content -LiteralPath (Join-Path $caseRoot 'command.txt') -Value @('batch',$Group) -Encoding utf8
    Write-Output ('Started native batch '+$Group)
}
