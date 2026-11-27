param(
    [string]$Only = 'settings,rules',
    [string]$Suffix = 'v211',
    [ValidateSet('en', 'zh')]
    [string]$Lang = 'en',
    [int]$Dpi = 144,
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$toolProj = Join-Path $root 'tools\ScreenshotGen\ScreenshotGen.csproj'

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $root 'docs\screenshots'
}

$argsList = @(
    'run', '--project', $toolProj, '-c', 'Release', '--nologo', '--',
    '--out', $OutDir,
    '--suffix', $Suffix,
    '--only', $Only,
    '--lang', $Lang,
    '--dpi', $Dpi.ToString()
)

Write-Host "Generating screenshots ($Only, suffix=$Suffix, lang=$Lang, dpi=$Dpi)..." -ForegroundColor Cyan
& dotnet @argsList
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
