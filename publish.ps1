# publish.ps1 — Build and package Monitor Brightness Controller
# Runs dotnet publish, then invokes Inno Setup (ISCC.exe) to produce the installer.
# Usage: .\publish.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ─── 1. Read version from .csproj ────────────────────────────────────────────

$csprojPath = Join-Path $PSScriptRoot 'MonitorBrightnessController\MonitorBrightnessController.csproj'

if (-not (Test-Path $csprojPath)) {
    Write-Error "ERROR: Cannot find .csproj at '$csprojPath'."
    exit 1
}

[xml]$csproj = Get-Content $csprojPath
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

if (-not $version) {
    Write-Error "ERROR: Could not read <Version> from '$csprojPath'."
    exit 1
}

Write-Host "Version: $version" -ForegroundColor Cyan

# ─── 2. Run dotnet publish ───────────────────────────────────────────────────

Write-Host "Running dotnet publish..." -ForegroundColor Cyan

$publishArgs = @(
    'publish'
    $csprojPath
    '-c', 'Release'
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "ERROR: dotnet publish failed with exit code $LASTEXITCODE."
    exit 1
}

Write-Host "dotnet publish succeeded." -ForegroundColor Green

# ─── 3. Locate ISCC.exe ─────────────────────────────────────────────────────

$isccPath = $null

# Check common Inno Setup installation paths
$candidatePaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 5\ISCC.exe"
)

foreach ($candidate in $candidatePaths) {
    if (Test-Path $candidate) {
        $isccPath = $candidate
        break
    }
}

# Fall back to PATH
if (-not $isccPath) {
    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($cmd) {
        $isccPath = $cmd.Source
    }
}

if (-not $isccPath) {
    Write-Error "ERROR: ISCC.exe (Inno Setup Compiler) not found. Please install Inno Setup 6 or ensure ISCC.exe is on your PATH."
    exit 1
}

Write-Host "Using ISCC: $isccPath" -ForegroundColor Cyan

# ─── 4. Compile installer with Inno Setup ───────────────────────────────────

$issPath = Join-Path $PSScriptRoot 'MonitorBrightnessControllerSetup.iss'

if (-not (Test-Path $issPath)) {
    Write-Error "ERROR: Cannot find Inno Setup script at '$issPath'."
    exit 1
}

Write-Host "Compiling installer (version $version)..." -ForegroundColor Cyan

& $isccPath "/DMyAppVersion=$version" $issPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "ERROR: Inno Setup compilation failed with exit code $LASTEXITCODE."
    exit 1
}

Write-Host "Installer compiled successfully." -ForegroundColor Green

# ─── 5. Clean up builds folder ──────────────────────────────────────────────
# Ensure only the installer executable remains in builds/v{VERSION}/

$buildsDir = Join-Path $PSScriptRoot "builds\v$version"

if (Test-Path $buildsDir) {
    $installerName = "MonitorBrightnessControllerSetup-$version.exe"
    
    Get-ChildItem -Path $buildsDir -File | Where-Object { $_.Name -ne $installerName } | ForEach-Object {
        Write-Host "Removing: $($_.Name)" -ForegroundColor Yellow
        Remove-Item $_.FullName -Force
    }
}

# ─── Done ────────────────────────────────────────────────────────────────────

$installerPath = Join-Path $buildsDir "MonitorBrightnessControllerSetup-$version.exe"
Write-Host ""
Write-Host "Build complete! Installer: $installerPath" -ForegroundColor Green
