param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sln = Join-Path $root 'LinkToBrowser.sln'
$csproj = Join-Path $root 'LinkToBrowser.csproj'
$releaseDir = Join-Path $root 'bin\Release\net48'
$distDir = Join-Path $root 'dist'
$portableDir = Join-Path $distDir 'LinkToBrowser'
$stageRoot = Join-Path $distDir '_stage'
$stageDir = Join-Path $stageRoot 'LinkToBrowser'
$zipPath = Join-Path $distDir 'LinkToBrowser-portable.zip'

if (-not $SkipTests) {
    Write-Host 'Running unit tests (Release)...' -ForegroundColor Cyan
    dotnet test $sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host 'Building LinkToBrowser (Release, portable folder)...' -ForegroundColor Cyan
dotnet build $csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$outExe = Join-Path $releaseDir 'LinkToBrowser.exe'
if (-not (Test-Path $outExe)) {
    throw "Build succeeded but output executable not found: $outExe"
}

if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
New-Item -ItemType Directory -Path $portableDir -Force | Out-Null

$runtimeFiles = Get-ChildItem $releaseDir -File | Where-Object { $_.Extension -in '.exe', '.dll', '.config' }
foreach ($file in $runtimeFiles) {
    Copy-Item $file.FullName -Destination $stageDir -Force
    Copy-Item $file.FullName -Destination $portableDir -Force
}

Compress-Archive -Path $stageDir -DestinationPath $zipPath -CompressionLevel Optimal -Force

$dirBytes = (Get-ChildItem $stageDir -File | Measure-Object -Property Length -Sum).Sum
$zipBytes = (Get-Item $zipPath).Length
Remove-Item $stageRoot -Recurse -Force

$dirMb = [Math]::Round($dirBytes / 1MB, 2)
$zipMb = [Math]::Round($zipBytes / 1MB, 2)

Write-Host ("Built portable folder: $portableDir ($dirMb MB / $dirBytes bytes)") -ForegroundColor Green
Write-Host ("Built release package: $zipPath ($zipMb MB / $zipBytes bytes)") -ForegroundColor Green

if ($dirBytes -ge 20MB) {
    throw "Portable folder exceeded 20 MB limit ($dirMb MB)"
}
