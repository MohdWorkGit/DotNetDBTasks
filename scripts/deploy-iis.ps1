#requires -Version 5.1
<#
.SYNOPSIS
    Builds Bayan and deploys it as a single IIS site (ASP.NET Core in-process),
    configured so the in-process background workers keep running.

.DESCRIPTION
    Run this ON THE WINDOWS SERVER, as Administrator. It produces one IIS site that
    serves both the Angular SPA (from the API's wwwroot) and the API, which is what
    makes Windows SSO work without CORS or a reverse proxy.

    Most of this script is the app pool configuration, and those settings are not
    optional decoration - the app keeps scheduled exports, async query jobs and cached
    results in the worker process's own memory, so:

      * maxProcesses MUST stay 1. A web garden runs a second scheduler that fires every
        due task a second time, and splits the job store so a client polling for its
        result can be answered by the process that never ran it.
      * disallowOverlappingRotation MUST be true. On startup the job store deletes
        everything in its spill directory to clear orphans; during an overlapped recycle
        the new process does that while the old one is still streaming those files to
        someone's browser.
      * idleTimeout/periodicRestart MUST be off, or the pool stops overnight and the
        schedules it was supposed to fire never fire.

    Re-running this is an upgrade, not a reinstall: existing bindings, appsettings.json,
    logs\ and cache\ are all left alone.

.PARAMETER SiteName
    IIS site and app pool name. Default: Bayan.

.PARAMETER PhysicalPath
    Where the published app lives on disk. Default: C:\inetpub\Bayan.

.PARAMETER Port
    Binding port. Default: 80. Ignored if the binding already exists.

.PARAMETER Hostname
    Host header for the binding, e.g. bayan.corp.local. Strongly recommended: silent SSO
    needs users to reach the site by hostname, not by IP.

.PARAMETER AppPoolIdentity
    ApplicationPoolIdentity (default) or DomainAccount. See the notes printed at the end -
    this choice decides whether scheduled tasks can write to network shares.

.PARAMETER ServiceAccount
    DOMAIN\account, required when -AppPoolIdentity DomainAccount.

.PARAMETER ServicePassword
    Password for -ServiceAccount.

.PARAMETER EnableSso
    Sets Auth:EnableSso to true in the deployed appsettings.json. Without this the SSO
    endpoint stays switched off and users sign in with the form.

.PARAMETER SkipBuild
    Deploy whatever is already in api-publish\ instead of rebuilding.

.EXAMPLE
    ./scripts/deploy-iis.ps1 -Hostname bayan.corp.local -EnableSso

.EXAMPLE
    ./scripts/deploy-iis.ps1 -Hostname bayan.corp.local -EnableSso `
        -AppPoolIdentity DomainAccount -ServiceAccount 'CORP\svc-bayan' -ServicePassword '...'
#>
[CmdletBinding()]
param(
    [string]$SiteName = 'Bayan',
    [string]$PhysicalPath = 'C:\inetpub\Bayan',
    [int]$Port = 80,
    [string]$Hostname = '',
    [ValidateSet('ApplicationPoolIdentity', 'DomainAccount')]
    [string]$AppPoolIdentity = 'ApplicationPoolIdentity',
    [string]$ServiceAccount = '',
    [string]$ServicePassword = '',
    [switch]$EnableSso,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

# --- Resolve paths relative to the repo root (parent of this script's folder) ---
$RepoRoot   = Split-Path -Parent $PSScriptRoot
$Solution   = Join-Path $RepoRoot 'Bayan.sln'
$ApiCsproj  = Join-Path $RepoRoot 'src/Bayan.API/Bayan.API.csproj'
$ClientDir  = Join-Path $RepoRoot 'client'
$PublishDir = Join-Path $RepoRoot 'api-publish'

if (-not (Test-Path $Solution)) {
    throw "Could not find Bayan.sln at '$Solution'. Run this from the repo (scripts/ folder)."
}

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  [ok] $msg" -ForegroundColor Green }
function Write-Warn2($msg){ Write-Host "  [!!] $msg" -ForegroundColor Yellow }

# ---------------------------------------------------------------------------
# 1. Preflight
# ---------------------------------------------------------------------------
Write-Step "Preflight"

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "This script must run in an elevated PowerShell session (Run as Administrator)."
}
Write-Ok "Running elevated"

if ($AppPoolIdentity -eq 'DomainAccount') {
    if ([string]::IsNullOrWhiteSpace($ServiceAccount) -or [string]::IsNullOrWhiteSpace($ServicePassword)) {
        throw "-AppPoolIdentity DomainAccount requires both -ServiceAccount and -ServicePassword."
    }
}

Import-Module WebAdministration -ErrorAction Stop
Write-Ok "WebAdministration module loaded"

# ANCM is what runs a .NET process under IIS. It ships in the ASP.NET Core Hosting Bundle
# and is required even for a self-contained publish, which surprises people every time.
$ancm = Join-Path $env:windir 'System32\inetsrv\aspnetcorev2.dll'
if (-not (Test-Path $ancm)) {
    throw "ASP.NET Core Module (aspnetcorev2.dll) not found. Install the .NET 10 ASP.NET Core Hosting Bundle, then run: net stop was /y & net start w3svc"
}
Write-Ok "ASP.NET Core Module present"

# Application Initialization is what makes preloadEnabled do anything. Without it the pool
# starts but the app itself waits for a first request, so the workers do not start.
$appInit = Get-WindowsOptionalFeature -Online -FeatureName IIS-ApplicationInit -ErrorAction SilentlyContinue
if ($appInit -and $appInit.State -ne 'Enabled') {
    Write-Warn2 "IIS Application Initialization is not enabled - enabling it now"
    $null = Enable-WindowsOptionalFeature -Online -FeatureName IIS-ApplicationInit -NoRestart
    Write-Ok "IIS-ApplicationInit enabled"
} else {
    Write-Ok "IIS Application Initialization available"
}

if (-not $SkipBuild) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { throw "'dotnet' not found on PATH. Install the .NET 10 SDK, or pass -SkipBuild." }
    $npm = Get-Command npm -ErrorAction SilentlyContinue
    if (-not $npm) { throw "'npm' not found on PATH. Install Node.js 24.x, or pass -SkipBuild." }
    Write-Ok "Build toolchain present"
}

# ---------------------------------------------------------------------------
# 2. Build the Angular client and publish the API
# ---------------------------------------------------------------------------
if ($SkipBuild) {
    Write-Step "Skipping build (-SkipBuild)"
    if (-not (Test-Path (Join-Path $PublishDir 'Bayan.API.dll'))) {
        throw "-SkipBuild was passed but '$PublishDir' does not contain a published app."
    }
} else {
    Write-Step "Building the Angular client"
    Push-Location $ClientDir
    try {
        & npm run build:prod
        if ($LASTEXITCODE -ne 0) { throw "npm run build:prod failed with exit code $LASTEXITCODE." }
    } finally {
        Pop-Location
    }
    Write-Ok "Client built"

    Write-Step "Publishing the API"
    if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
    & dotnet publish $ApiCsproj -c Release -r win-x64 --self-contained -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
    Write-Ok "API published to $PublishDir"

    Write-Step "Merging the SPA into wwwroot"
    # The API serves the SPA itself (UseStaticFiles + MapFallbackToFile in Program.cs),
    # which is what puts the UI and the API on one origin.
    $browserDir = Join-Path $ClientDir 'dist/bayan-client/browser'
    if (-not (Test-Path $browserDir)) {
        throw "Angular build output not found at '$browserDir'."
    }
    $wwwroot = Join-Path $PublishDir 'wwwroot'
    $null = New-Item -ItemType Directory -Force -Path $wwwroot
    Copy-Item (Join-Path $browserDir '*') $wwwroot -Recurse -Force
    Write-Ok "SPA merged into wwwroot"
}

# ---------------------------------------------------------------------------
# 3. Stop the site so files are not locked, then sync
# ---------------------------------------------------------------------------
$poolPath = "IIS:\AppPools\$SiteName"
$sitePath = "IIS:\Sites\$SiteName"
$poolExists = Test-Path $poolPath
$siteExists = Test-Path $sitePath

if ($siteExists -or $poolExists) {
    Write-Step "Stopping '$SiteName' for the upgrade"
    if ($siteExists) {
        try { Stop-Website -Name $SiteName } catch { Write-Warn2 "Site was not running" }
    }
    if ($poolExists) {
        try { Stop-WebAppPool -Name $SiteName } catch { Write-Warn2 "App pool was not running" }
    }
    # w3wp does not release its file handles the instant the pool is told to stop.
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        $state = (Get-WebAppPoolState -Name $SiteName -ErrorAction SilentlyContinue).Value
        if (-not $state -or $state -eq 'Stopped') { break }
        Start-Sleep -Seconds 1
    }
    Write-Ok "Stopped"
}

Write-Step "Copying files to $PhysicalPath"
$null = New-Item -ItemType Directory -Force -Path $PhysicalPath

# /MIR to clear out files from the previous version, but /XD and /XF carve out everything
# that belongs to the deployment rather than the build. Without those exclusions an
# upgrade would delete the operator's connection string and every log file.
$existingSettings = Test-Path (Join-Path $PhysicalPath 'appsettings.json')
$robocopyArgs = @(
    $PublishDir, $PhysicalPath, '/MIR',
    '/XD', (Join-Path $PhysicalPath 'logs'), (Join-Path $PhysicalPath 'cache'),
    '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:3', '/W:2'
)
if ($existingSettings) {
    # First deploy takes the template; later ones must never clobber a configured file.
    $robocopyArgs += @('/XF', 'appsettings.json')
    Write-Ok "Preserving the existing appsettings.json"
}

& robocopy @robocopyArgs | Out-Null
# robocopy uses exit codes as a bit field: <8 means it did its job.
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE." }
$global:LASTEXITCODE = 0
Write-Ok "Files copied"

if (-not $existingSettings) {
    Write-Warn2 "A fresh appsettings.json was deployed - set the connection string, Jwt:Secret, Encryption:Key and Ldap:* before going live."
}

# ---------------------------------------------------------------------------
# 4. Application pool
# ---------------------------------------------------------------------------
Write-Step "Configuring the app pool"

if (-not $poolExists) {
    $null = New-WebAppPool -Name $SiteName
    Write-Ok "Created app pool '$SiteName'"
} else {
    Write-Ok "Reusing app pool '$SiteName'"
}

# No Managed Code: ANCM runs the .NET process, so the IIS CLR is not involved.
Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ''

# --- The settings that keep the background workers alive ---

# Never stop when idle. The scheduler polls in-process, so an idled pool means an export
# that was due at 02:00 simply never happens.
Set-ItemProperty $poolPath -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)

# Start with Windows rather than on the first request.
Set-ItemProperty $poolPath -Name startMode -Value 'AlwaysRunning'

# Exactly one worker process. See the header - a web garden duplicates every scheduled run.
Set-ItemProperty $poolPath -Name processModel.maxProcesses -Value 1

# Outermost rung of the shutdown ladder:
#   HostOptions.ShutdownTimeout (90s) < ANCM shutdownTimeLimit (100s, web.config) < this.
# A cancelled export needs this window to write its final status and flush the incremental
# checkpoints of the items that did finish.
Set-ItemProperty $poolPath -Name processModel.shutdownTimeLimit -Value ([TimeSpan]::FromMinutes(2))

# No overlapped recycle: the replacement process wipes the result spill directory on
# startup, which would pull files out from under the outgoing process's live readers.
Set-ItemProperty $poolPath -Name recycling.disallowOverlappingRotation -Value $true

# Turn off every automatic recycle trigger. Each one is an unannounced restart that drops
# in-flight query jobs and every cached result.
Set-ItemProperty $poolPath -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
Set-ItemProperty $poolPath -Name recycling.periodicRestart.requests -Value 0
Set-ItemProperty $poolPath -Name recycling.periodicRestart.memory -Value 0
Set-ItemProperty $poolPath -Name recycling.periodicRestart.privateMemory -Value 0
Clear-ItemProperty $poolPath -Name recycling.periodicRestart.schedule -ErrorAction SilentlyContinue

# --- Identity ---
if ($AppPoolIdentity -eq 'DomainAccount') {
    Set-ItemProperty $poolPath -Name processModel.identityType -Value 'SpecificUser'
    Set-ItemProperty $poolPath -Name processModel.userName -Value $ServiceAccount
    Set-ItemProperty $poolPath -Name processModel.password -Value $ServicePassword
    Write-Ok "Pool identity: $ServiceAccount"
} else {
    Set-ItemProperty $poolPath -Name processModel.identityType -Value 'ApplicationPoolIdentity'
    Write-Ok "Pool identity: ApplicationPoolIdentity"
}

Write-Ok "App pool configured"

# ---------------------------------------------------------------------------
# 5. Site
# ---------------------------------------------------------------------------
Write-Step "Configuring the site"

if (-not $siteExists) {
    if ([string]::IsNullOrWhiteSpace($Hostname)) {
        $null = New-Website -Name $SiteName -Port $Port -PhysicalPath $PhysicalPath -ApplicationPool $SiteName
    } else {
        $null = New-Website -Name $SiteName -Port $Port -HostHeader $Hostname -PhysicalPath $PhysicalPath -ApplicationPool $SiteName
    }
    Write-Ok "Created site '$SiteName'"
} else {
    # Deliberately not touching bindings on an existing site: an operator may have added
    # an HTTPS binding or extra host headers that this script knows nothing about.
    Set-ItemProperty $sitePath -Name physicalPath -Value $PhysicalPath
    Set-ItemProperty $sitePath -Name applicationPool -Value $SiteName
    Write-Ok "Reusing site '$SiteName' (bindings left untouched)"
}

# Warm the app straight after a start or restart instead of waiting for a visitor.
Set-ItemProperty $sitePath -Name applicationDefaults.preloadEnabled -Value $true
Write-Ok "Preload enabled"

# ---------------------------------------------------------------------------
# 6. Permissions and Windows authentication
# ---------------------------------------------------------------------------
Write-Step "Granting file permissions"

if ($AppPoolIdentity -eq 'DomainAccount') {
    $grantee = $ServiceAccount
} else {
    $grantee = "IIS AppPool\$SiteName"
}
# The app writes logs\ and spills large query results to cache\ under its own folder.
& icacls $PhysicalPath /grant "$($grantee):(OI)(CI)M" /T /Q
if ($LASTEXITCODE -ne 0) { Write-Warn2 "icacls returned $LASTEXITCODE - check permissions on $PhysicalPath manually." }
$global:LASTEXITCODE = 0
Write-Ok "Granted modify rights to $grantee"

Write-Step "Unlocking the IIS authentication sections"
# These are locked at server level by default, and the shipped web.config sets both - which
# is a 500.19 until they are unlocked. Once per server, harmless to repeat.
$appcmd = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
& $appcmd unlock config /section:windowsAuthentication | Out-Null
& $appcmd unlock config /section:anonymousAuthentication | Out-Null
$global:LASTEXITCODE = 0
Write-Ok "windowsAuthentication and anonymousAuthentication unlocked"

# ---------------------------------------------------------------------------
# 7. Optionally switch SSO on in the deployed configuration
# ---------------------------------------------------------------------------
if ($EnableSso) {
    Write-Step "Enabling SSO in appsettings.json"
    $settingsPath = Join-Path $PhysicalPath 'appsettings.json'
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    if ($null -eq $settings.Auth) {
        $settings | Add-Member -MemberType NoteProperty -Name Auth -Value ([PSCustomObject]@{ EnableSso = $true })
    } else {
        $settings.Auth.EnableSso = $true
    }
    $settings | ConvertTo-Json -Depth 20 | Out-File $settingsPath -Encoding utf8
    Write-Ok "Auth:EnableSso = true"
}

# ---------------------------------------------------------------------------
# 8. Start
# ---------------------------------------------------------------------------
Write-Step "Starting '$SiteName'"
Start-WebAppPool -Name $SiteName
Start-Website -Name $SiteName
Write-Ok "Started"

# ---------------------------------------------------------------------------
# 9. What the operator still has to do
# ---------------------------------------------------------------------------
$url = if ([string]::IsNullOrWhiteSpace($Hostname)) { "http://localhost:$Port" } else { "http://$Hostname" }

Write-Step "Done - remaining steps"

Write-Host "  Verify"
Write-Host "    1. $url loads the app, and a deep link such as $url/admin/queries survives a refresh."
Write-Host "    2. Leave it alone for 30+ minutes, then confirm a scheduled task still fires."
Write-Host "       That is the real proof the idle timeout is off."

if ($EnableSso) {
    Write-Host ""
    Write-Host "  Windows SSO"
    Write-Host "    3. Users must reach the site by HOSTNAME, not by IP, and that hostname must be in"
    Write-Host "       the browser's Local Intranet zone. Otherwise the browser prompts instead of"
    Write-Host "       signing in silently."
    if ($AppPoolIdentity -eq 'DomainAccount') {
        $spnHost = if ([string]::IsNullOrWhiteSpace($Hostname)) { '<hostname>' } else { $Hostname }
        Write-Host "    4. Register the SPN for the pool account:" -ForegroundColor Yellow
        Write-Host "         setspn -S HTTP/$spnHost $ServiceAccount" -ForegroundColor Yellow
    } else {
        Write-Host "    4. No SPN needed: under ApplicationPoolIdentity the machine account already"
        Write-Host "       covers this host's own name."
    }
    Write-Host "    5. Ldap:* must stay valid - SSO supplies the username, but the profile lookup"
    Write-Host "       and auto-provisioning still go through AD."
}

Write-Host ""
Write-Host "  Scheduled task output folders"
Write-Host "    Grant $grantee write access to every task's OutputFolder and ArchiveFolder."
Write-Host "    This script only covers $PhysicalPath - it cannot know those paths."

if ($AppPoolIdentity -ne 'DomainAccount') {
    Write-Warn2 "ApplicationPoolIdentity cannot write to remote UNC shares. Every scheduled task output folder must be a local path, or switch to -AppPoolIdentity DomainAccount."
}

Write-Host "`n  HTTPS: add a binding and certificate before exposing this beyond a trusted network.`n"
