[CmdletBinding()]
param(
    [switch]$Dev
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = Split-Path -Parent $PSScriptRoot
$distExe = Join-Path $root 'dist\PoliceImageToolkit.exe'
$projectPath = Join-Path $root 'src\PoliceImageToolkit\PoliceImageToolkit.csproj'

if (-not $Dev -and (Test-Path $distExe)) {
    Write-Output "啟動已發布單檔執行檔：$distExe"
    Start-Process -FilePath $distExe
    exit 0
}

$dotnet = 'dotnet'
if (Test-Path "$env:USERPROFILE\AppData\Local\Microsoft\dotnet\dotnet.exe") {
    $dotnet = "$env:USERPROFILE\AppData\Local\Microsoft\dotnet\dotnet.exe"
}

Write-Output "啟動開發模式專案：$projectPath"
& $dotnet run --project $projectPath -c Release
exit $LASTEXITCODE