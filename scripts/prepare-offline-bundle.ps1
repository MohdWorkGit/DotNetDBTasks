#requires -Version 5.1
<#
.SYNOPSIS
    Gathers everything needed to edit and rebuild Bayan on an air-gapped
    machine, into a single gitignored folder (offline-bundle/ by default).

.DESCRIPTION
    Run this on an INTERNET-CONNECTED machine. It produces:
      - nuget-packages/  : the full NuGet dependency closure (offline restore source)
      - npm-cache/       : the npm cache for the Angular client (offline npm ci)
      - api-publish/     : (optional) self-contained .NET build, ready to run
      - queryrunner-publish/ : (optional) self-contained QueryRunner console tool
      - client-dist/     : (optional) production Angular build
      - installers/      : (optional) .NET SDK + Node.js offline installers
      - MANIFEST.txt     : what's inside + how to use it on the target

    Copy the resulting folder to the air-gapped machine. See DEPLOY-AIRGAPPED.md (Part B).

.PARAMETER OutDir
    Output folder. Default: ./offline-bundle (gitignored).

.PARAMETER Runtime
    Runtime identifier for the self-contained publish. Default: win-x64.

.PARAMETER IncludeBuild
    Also produce a self-contained API publish and a production Angular build.

.PARAMETER IncludeInstallers
    Best-effort download of the .NET 10 SDK and Node.js offline installers.
    URLs may change over time — verify against the MANIFEST if a download fails.

.PARAMETER Clean
    Delete the output folder before starting.

.EXAMPLE
    ./scripts/prepare-offline-bundle.ps1

.EXAMPLE
    ./scripts/prepare-offline-bundle.ps1 -IncludeBuild -IncludeInstallers -Clean
#>
[CmdletBinding()]
param(
    [string]$OutDir = "offline-bundle",
    [string]$Runtime = "win-x64",
    [switch]$IncludeBuild,
    [switch]$IncludeInstallers,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

# --- Resolve paths relative to the repo root (parent of this script's folder) ---
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $RepoRoot 'Bayan.sln'
$ApiCsproj = Join-Path $RepoRoot 'src/Bayan.API/Bayan.API.csproj'
$ClientDir = Join-Path $RepoRoot 'client'

if (-not (Test-Path $Solution)) {
    throw "Could not find Bayan.sln at '$Solution'. Run this from the repo (scripts/ folder)."
}

# Make OutDir absolute under the repo root if a relative path was given
if (-not [System.IO.Path]::IsPathRooted($OutDir)) {
    $OutDir = Join-Path $RepoRoot $OutDir
}

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  [ok] $msg" -ForegroundColor Green }
function Write-Warn2($msg){ Write-Host "  [!!] $msg" -ForegroundColor Yellow }

# --- Preflight: required toolchains ---
Write-Step "Preflight"
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw "'dotnet' not found on PATH. Install the .NET 10 SDK first." }
$dotnetVersion = (& dotnet --version)
Write-Ok ".NET SDK $dotnetVersion"

$npm = Get-Command npm -ErrorAction SilentlyContinue
if (-not $npm) { throw "'npm' not found on PATH. Install Node.js 24.x first." }
$nodeVersion = (& node --version)
$npmVersion  = (& npm --version)
Write-Ok "Node $nodeVersion / npm $npmVersion"

# --- Prepare output folder ---
if ($Clean -and (Test-Path $OutDir)) {
    Write-Step "Cleaning $OutDir"
    Remove-Item -Recurse -Force $OutDir
}
$null = New-Item -ItemType Directory -Force -Path $OutDir

$NugetDir     = Join-Path $OutDir 'nuget-packages'
$NpmCacheDir  = Join-Path $OutDir 'npm-cache'
$InstallerDir = Join-Path $OutDir 'installers'

# --- 1. NuGet dependency closure ---
Write-Step "Restoring NuGet dependency closure -> nuget-packages/"
$null = New-Item -ItemType Directory -Force -Path $NugetDir
# Restore into a local global-style packages folder. This captures the full
# transitive closure (build + the win-x64/linux-x64 runtime assets).
& dotnet restore $Solution --packages $NugetDir --runtime $Runtime
& dotnet restore $Solution --packages $NugetDir   # also the no-RID graph, for editing
# The QueryRunner tool is deliberately NOT in the solution — restore it too, or
# its packages (and any version it pins that the solution doesn't) are missing.
$QueryRunnerCsproj = Join-Path $RepoRoot 'tools/Bayan.QueryRunner/Bayan.QueryRunner.csproj'
& dotnet restore $QueryRunnerCsproj --packages $NugetDir --runtime $Runtime
& dotnet restore $QueryRunnerCsproj --packages $NugetDir
$nupkgCount = (Get-ChildItem -Path $NugetDir -Recurse -Filter *.nupkg -ErrorAction SilentlyContinue).Count
Write-Ok "$nupkgCount .nupkg files cached"

# Emit a nuget.config the target can drop next to the .sln to restore offline.
$nugetConfig = @'
<?xml version="1.0" encoding="utf-8"?>
<!-- Copy this file next to Bayan.sln on the air-gapped machine. -->
<configuration>
  <config>
    <!-- Point the global packages folder at the bundled cache. -->
    <add key="globalPackagesFolder" value="OFFLINE_BUNDLE_PATH/nuget-packages" />
  </config>
  <packageSources>
    <clear />
    <add key="offline" value="OFFLINE_BUNDLE_PATH/nuget-packages" />
  </packageSources>
</configuration>
'@
Set-Content -Path (Join-Path $OutDir 'nuget.config.template') -Value $nugetConfig -Encoding UTF8
Write-Ok "Wrote nuget.config.template"

# --- 2. npm cache for the Angular client ---
Write-Step "Populating npm cache -> npm-cache/"
$null = New-Item -ItemType Directory -Force -Path $NpmCacheDir
# Populate the cache WITHOUT touching the working client/node_modules. Running
# 'npm ci' inside client/ wipes and rebuilds node_modules, which fails with EPERM
# when a native binary (e.g. esbuild.exe) is locked by a dev server, editor, or AV.
# Instead we stage just the lockfiles in a throwaway temp dir and install there
# with scripts disabled - only the downloaded tarballs (the cache) matter here.
$lockFile = Join-Path $ClientDir 'package-lock.json'
if (-not (Test-Path $lockFile)) {
    throw "client/package-lock.json not found - run 'npm install' in client/ once to generate it."
}
$npmStage = Join-Path ([System.IO.Path]::GetTempPath()) ("dnbt-npm-" + [System.Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Force -Path $npmStage
try {
    Copy-Item (Join-Path $ClientDir 'package.json') $npmStage
    Copy-Item $lockFile $npmStage
    Push-Location $npmStage
    try {
        & npm ci --cache $NpmCacheDir --ignore-scripts --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE" }
        Write-Ok "npm cache populated from package-lock.json (client/node_modules untouched)"
    } finally {
        Pop-Location
    }
} finally {
    Remove-Item -Recurse -Force $npmStage -ErrorAction SilentlyContinue
}

# --- 3. Optional: build artifacts ---
if ($IncludeBuild) {
    Write-Step "Building self-contained API -> api-publish/"
    $ApiOut = Join-Path $OutDir 'api-publish'
    & dotnet publish $ApiCsproj -c Release -r $Runtime --self-contained `
        --packages $NugetDir -o $ApiOut
    Write-Ok "API published ($Runtime, self-contained)"

    Write-Step "Building self-contained QueryRunner -> queryrunner-publish/"
    $QueryRunnerOut = Join-Path $OutDir 'queryrunner-publish'
    & dotnet publish $QueryRunnerCsproj -c Release -r $Runtime --self-contained `
        --packages $NugetDir -o $QueryRunnerOut
    Write-Ok "QueryRunner published ($Runtime, self-contained)"

    Write-Step "Building Angular production bundle -> client-dist/"
    $ClientOut = Join-Path $OutDir 'client-dist'
    Push-Location $ClientDir
    try {
        & npm run build:prod
        $browser = Join-Path $ClientDir 'dist/bayan-client/browser'
        if (Test-Path $browser) {
            $null = New-Item -ItemType Directory -Force -Path $ClientOut
            Copy-Item -Recurse -Force (Join-Path $browser '*') $ClientOut
            Write-Ok "Angular bundle copied to client-dist/"
        } else {
            Write-Warn2 "Expected build output not found at $browser"
        }
    } finally {
        Pop-Location
    }
}

# --- 4. Optional: offline installers (best effort) ---
# These URLs are point-in-time; verify them if a download fails.
$dotnetSdkUrl = 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.103/dotnet-sdk-10.0.103-win-x64.exe'
$nodeMsiUrl   = 'https://nodejs.org/dist/v24.14.0/node-v24.14.0-x64.msi'
# Required on the target server for IIS hosting (Part C): it supplies the ASP.NET Core Module,
# without which IIS cannot run the app at all - and it is needed even though we publish
# self-contained, which is the part people get caught by.
$hostingBundleUrl = 'https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/10.0.3/dotnet-hosting-10.0.3-win.exe'
if ($IncludeInstallers) {
    Write-Step "Downloading offline installers -> installers/"
    $null = New-Item -ItemType Directory -Force -Path $InstallerDir
    foreach ($d in @(
        @{ Url = $dotnetSdkUrl;     Name = 'dotnet-sdk-10-win-x64.exe' },
        @{ Url = $nodeMsiUrl;       Name = 'node-v24-x64.msi' },
        @{ Url = $hostingBundleUrl; Name = 'dotnet-hosting-10-win.exe' }
    )) {
        $dest = Join-Path $InstallerDir $d.Name
        try {
            Write-Host "  downloading $($d.Name) ..."
            Invoke-WebRequest -Uri $d.Url -OutFile $dest -UseBasicParsing
            Write-Ok $d.Name
        } catch {
            Write-Warn2 "Failed to download $($d.Name): $($_.Exception.Message)"
            Write-Warn2 "Download manually from: $($d.Url)"
        }
    }
}

# --- 5. Manifest ---
Write-Step "Writing MANIFEST.txt"
$now = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
if ($IncludeBuild) {
    $buildLine = "api-publish/           Self-contained API, ready to run.`r`nqueryrunner-publish/   Self-contained QueryRunner console tool (Task Scheduler).`r`nclient-dist/           Production Angular bundle."
} else {
    $buildLine = "(api-publish / queryrunner-publish / client-dist not included - rerun with -IncludeBuild)"
}
if ($IncludeInstallers) {
    $installerLine = "installers/            .NET SDK + Node.js + ASP.NET Core Hosting Bundle (IIS)."
} else {
    $installerLine = "(installers not included - rerun with -IncludeInstallers, or download manually below)"
}
$manifest = @"
Bayan - Offline Build Bundle
Generated: $now
Built with: .NET SDK $dotnetVersion, Node $nodeVersion, npm $npmVersion
Target runtime: $Runtime

CONTENTS
--------
nuget-packages/        NuGet dependency closure ($nupkgCount packages).
nuget.config.template  Drop next to Bayan.sln (replace OFFLINE_BUNDLE_PATH).
npm-cache/             npm cache for the Angular client.
$buildLine
$installerLine

INSTALLERS TO BRING (if not in installers/)
-------------------------------------------
.NET 10 SDK (offline, $Runtime): $dotnetSdkUrl
Node.js 24.x (Windows MSI):      $nodeMsiUrl
ASP.NET Core Hosting Bundle:     $hostingBundleUrl

NOT IN THIS BUNDLE - IIS FEATURES COME FROM WINDOWS MEDIA
---------------------------------------------------------
Hosting on IIS (DEPLOY-AIRGAPPED.md Part C) also needs four Windows features,
which ship with Windows and cannot be downloaded into this bundle:

  Install-WindowsFeature Web-Server, Web-Windows-Auth, Web-AppInit, Web-Scripting-Tools

On a server with no internet add -Source <ISO>\sources\sxs. Web-AppInit is not
optional: the app's web.config declares an <applicationInitialization> element,
and IIS answers HTTP 500.19 if the feature is absent.

USAGE ON THE AIR-GAPPED MACHINE
-------------------------------
1. Install the .NET 10 SDK and Node.js from installers/ (or your own copies).
   For an IIS target, also install dotnet-hosting-10-win.exe there.

2. Copy this whole bundle somewhere stable, e.g. C:\offline-bundle.

3. NuGet (choose ONE):
   a) Copy nuget.config.template next to Bayan.sln, rename to nuget.config,
      and replace OFFLINE_BUNDLE_PATH with the bundle's full path; then:
         dotnet restore Bayan.sln
   b) Or restore straight against the cache:
         dotnet restore Bayan.sln --packages C:\offline-bundle\nuget-packages

4. npm (in client/):
      npm ci --offline --cache C:\offline-bundle\npm-cache

5. Rebuild:
      dotnet build Bayan.sln -c Release
      cd client; npm run build:prod

See DEPLOY-AIRGAPPED.md (Part B) for the full walkthrough.
"@
Set-Content -Path (Join-Path $OutDir 'MANIFEST.txt') -Value $manifest -Encoding UTF8
Write-Ok "MANIFEST.txt written"

Write-Step "Done"
Write-Host "Bundle ready at: $OutDir" -ForegroundColor Green
Write-Host "Verify it offline (disable networking) before carrying it across - see DEPLOY-AIRGAPPED.md B5." -ForegroundColor Green
