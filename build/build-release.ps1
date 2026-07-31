<#
.SYNOPSIS
    Builds every release artifact for Reshot: the app, the settings window, a portable
    ZIP and (when Inno Setup is present) the installer.

.DESCRIPTION
    reshot ships as two executables that must sit side by side:

      reshot.exe        the WPF app (tray, overlay, capture, recording)
      reshot-tauri.exe  the settings window, launched as a child process

    App.ResolveSettingsExe looks for reshot-tauri.exe next to reshot.exe first, which is
    exactly the layout this script produces.

    The app is published self-contained, so users do not need the .NET 8 runtime.

.PARAMETER Version
    Version stamped into the artifact names. Defaults to the <Version> in
    Directory.Build.props, so bumping it there is enough.

.PARAMETER SkipSettings
    Skip the Tauri build (slow, needs the Rust toolchain) and reuse the existing binary.

.EXAMPLE
    pwsh build/build-release.ps1
    pwsh build/build-release.ps1 -Version 1.0.1 -SkipSettings
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipSettings
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo 'dist'
$stage = Join-Path $dist 'reshot'

function Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Fail($text) { Write-Host "ERROR: $text" -ForegroundColor Red; exit 1 }

# ---- Version -----------------------------------------------------------------

if (-not $Version) {
    $props = Get-Content (Join-Path $repo 'Directory.Build.props') -Raw
    if ($props -match '<Version>([^<]+)</Version>') { $Version = $Matches[1] }
    else { Fail 'Could not read <Version> from Directory.Build.props.' }
}
Write-Host "reshot $Version" -ForegroundColor Green

# A running instance locks its own exe, which makes the build fail halfway through.
foreach ($name in 'reshot', 'reshot-tauri') {
    Get-Process $name -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Stopping running $name (pid $($_.Id))"
        Stop-Process -Id $_.Id -Force
    }
}

# ---- Clean -------------------------------------------------------------------

Step 'Preparing dist/'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

# ---- Tests -------------------------------------------------------------------

Step 'Running tests'
dotnet test (Join-Path $repo 'tests/Reshot.Core.Tests/Reshot.Core.Tests.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { Fail 'Tests failed; refusing to package.' }

# ---- App ---------------------------------------------------------------------

Step 'Publishing reshot.exe (self-contained x64)'
dotnet publish (Join-Path $repo 'src/Reshot.App/Reshot.App.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $stage --nologo
if ($LASTEXITCODE -ne 0) { Fail 'dotnet publish failed.' }

# Publishing drops the pdb/xml noise next to the exe; the release doesn't need it.
Get-ChildItem $stage -Include *.pdb, *.xml -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

# ---- Settings window ---------------------------------------------------------

$settingsExe = Join-Path $repo 'src/reshot-tauri/src-tauri/target/release/reshot-tauri.exe'

if (-not $SkipSettings) {
    Step 'Building the settings window (Tauri release)'
    Push-Location (Join-Path $repo 'src/reshot-tauri')
    try {
        if (-not (Test-Path 'node_modules')) {
            Write-Host 'Installing npm dependencies...'
            npm ci
            if ($LASTEXITCODE -ne 0) { Fail 'npm ci failed.' }
        }
        # --no-bundle: we package it ourselves. A plain `cargo build` would produce a DEV
        # binary that loads its UI from the vite dev server and shows a blank page.
        npm run tauri build -- --no-bundle
        if ($LASTEXITCODE -ne 0) { Fail 'Tauri build failed.' }
    }
    finally { Pop-Location }
}

if (-not (Test-Path $settingsExe)) {
    Fail "Settings binary missing: $settingsExe (run without -SkipSettings)."
}
Copy-Item $settingsExe (Join-Path $stage 'reshot-tauri.exe') -Force

# ---- Extra files -------------------------------------------------------------

Copy-Item (Join-Path $repo 'LICENSE') $stage -Force
Copy-Item (Join-Path $repo 'README.md') $stage -Force

# ---- Portable ZIP ------------------------------------------------------------

Step 'Packing the portable ZIP'
$zip = Join-Path $dist "reshot-$Version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "  $zip"

# ---- Installer ---------------------------------------------------------------

Step 'Building the installer'
$iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
if (-not $iscc) {
    # Inno Setup installs per-machine or per-user depending on how it was installed;
    # winget's package lands in LocalAppData, which the Program Files guesses miss.
    foreach ($guess in @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $guess) { $iscc = Get-Item $guess; break }
    }
}

if ($iscc) {
    & $iscc.FullName (Join-Path $PSScriptRoot 'installer.iss') `
        /DAppVersion=$Version /DPayloadDir=$stage /DOutputDir=$dist
    if ($LASTEXITCODE -ne 0) { Fail 'Inno Setup failed.' }
    Write-Host "  $(Join-Path $dist "reshot-$Version-setup.exe")"
}
else {
    Write-Host '  Inno Setup 6 not found; skipping the installer.' -ForegroundColor Yellow
    Write-Host '  Install it from https://jrsoftware.org/isdl.php and re-run.' -ForegroundColor Yellow
}

# ---- Checksums ---------------------------------------------------------------

Step 'Checksums'
$sums = Join-Path $dist 'SHA256SUMS.txt'
# -Include needs a wildcard path (or -Recurse) to match anything at all, hence dist\*.
Get-ChildItem (Join-Path $dist '*') -File -Include '*.zip', '*.exe' | ForEach-Object {
    $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
    "$h  $($_.Name)"
} | Set-Content $sums -Encoding utf8

if (Test-Path $sums) { Get-Content $sums | Write-Host }
else { Write-Host '  No artifacts to hash.' -ForegroundColor Yellow }

Step "Done: $dist"
