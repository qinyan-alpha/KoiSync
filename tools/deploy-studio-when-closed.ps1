param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$Destination,
    [Parameter(Mandatory=$true)][string]$StatusPath
)
$ErrorActionPreference='Stop'

function Get-Sha256([string]$Path)
{
    $stream=[IO.File]::OpenRead($Path)
    try
    {
        $sha=[Security.Cryptography.SHA256]::Create()
        try{return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','')}
        finally{$sha.Dispose()}
    }
    finally{$stream.Dispose()}
}

$sourcePath=[IO.Path]::GetFullPath($Source)
$destinationPath=[IO.Path]::GetFullPath($Destination)
$statusFile=[IO.Path]::GetFullPath($StatusPath)
if(!(Test-Path -LiteralPath $sourcePath)){throw "Release DLL not found: $sourcePath"}
$expected=Get-Sha256 $sourcePath

while($true)
{
    while(@(Get-Process -Name CharaStudio -ErrorAction SilentlyContinue).Count -gt 0)
    {Start-Sleep -Milliseconds 500}
    try
    {
        New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force|Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
        $actual=Get-Sha256 $destinationPath
        if($actual-ne$expected){throw 'Installed DLL hash does not match the prepared release'}
        $version=[Reflection.AssemblyName]::GetAssemblyName($destinationPath).Version.ToString()
        New-Item -ItemType Directory -Path (Split-Path -Parent $statusFile) -Force|Out-Null
        [PSCustomObject]@{State='deployed';Version=$version;SHA256=$actual;Installed=$destinationPath;Completed=(Get-Date).ToString('o')}|
            ConvertTo-Json|Set-Content -LiteralPath $statusFile -Encoding UTF8
        break
    }
    catch
    {
        if(@(Get-Process -Name CharaStudio -ErrorAction SilentlyContinue).Count -eq 0){throw}
        Start-Sleep -Milliseconds 500
    }
}
