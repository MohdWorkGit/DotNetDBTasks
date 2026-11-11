#requires -Version 5.1
#requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the local AI model runtime (Ollama) and its models on the air-gapped Jadwal server,
    from the folder scripts/prepare-ai-bundle.ps1 produced. Run it from inside that folder.

.DESCRIPTION
    1. Checks every file against SHA256SUMS.txt, so a damaged copy stops here.
    2. Unzips Ollama to -InstallDir and copies the models to -ModelsDir.
    3. Writes start-ollama.cmd, which serves on 127.0.0.1 only — Ollama has no login of its
       own, so it must never listen on the network — keeps the model loaded, and answers one
       request at a time.
    4. Registers a scheduled task, "Jadwal AI (Ollama)", that starts it at boot as SYSTEM and
       restarts it if it stops; then starts it and checks that the models answer.
    5. Suggests the model for the GPU and prints the appsettings.json section to set.

    Run it again to upgrade: it replaces the runtime and adds models, keeping the ones already there.

.PARAMETER InstallDir
    Where Ollama goes. Default: C:\Ollama.

.PARAMETER ModelsDir
    Where the models go (they need 7-20 GB). Default: C:\OllamaModels. A local disk, not a share.

.PARAMETER Port
    The local port Jadwal reaches Ollama on. Default: 11434 (Ollama's own default).

.PARAMETER UseRocm
    Install the AMD GPU build (the bundle must have been made with -IncludeRocm).

.PARAMETER SkipVerify
    Skip the checksum check. Only when it has already passed on this copy.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = 'C:\Ollama',
    [string]$ModelsDir = 'C:\OllamaModels',
    [int]$Port = 11434,
    [switch]$UseRocm,
    [switch]$SkipVerify
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Bundle = $PSScriptRoot
$TaskName = 'Jadwal AI (Ollama)'

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  [ok] $msg" -ForegroundColor Green }
function Write-Warn2($msg){ Write-Host "  [!!] $msg" -ForegroundColor Yellow }

# --- 1. Checksums ---
if (-not $SkipVerify) {
    Write-Step "Checking the bundle against SHA256SUMS.txt"
    $sums = Join-Path $Bundle 'SHA256SUMS.txt'
    if (-not (Test-Path $sums)) { throw "SHA256SUMS.txt is missing: this is not a complete AI bundle." }
    $bad = 0
    foreach ($line in Get-Content $sums) {
        if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { continue }
        $file = Join-Path $Bundle $Matches[2]
        if (-not (Test-Path $file)) { Write-Warn2 "missing: $($Matches[2])"; $bad++; continue }
        if ((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Matches[1]) {
            Write-Warn2 "damaged: $($Matches[2])"; $bad++
        }
    }
    if ($bad -gt 0) { throw "$bad file(s) missing or damaged. Copy the bundle again." }
    Write-Ok "Every file matches"
}

# --- 2. Runtime and models ---
Write-Step "Installing Ollama to $InstallDir"
$zip = Join-Path $Bundle ('ollama\ollama-windows-amd64{0}.zip' -f $(if ($UseRocm) { '-rocm' } else { '' }))
if (-not (Test-Path $zip)) { throw "$zip is not in the bundle." }
$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Get-Process ollama -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}
$null = New-Item -ItemType Directory -Force -Path $InstallDir
Expand-Archive -Path $zip -DestinationPath $InstallDir -Force
Write-Ok (Join-Path $InstallDir 'ollama.exe')

Write-Step "Copying the models to $ModelsDir"
$null = New-Item -ItemType Directory -Force -Path $ModelsDir
# robocopy: exit codes below 8 mean success.
robocopy (Join-Path $Bundle 'models') $ModelsDir /E /NFL /NDL /NJH /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying the models failed (robocopy exit $LASTEXITCODE)." }
Write-Ok "Models copied"

# --- 3. Start script: local only, model kept loaded, one request at a time ---
$cmd = @"
@echo off
rem Written by install-ai-service.ps1. Listens on 127.0.0.1 only: Ollama has no login.
set OLLAMA_HOST=127.0.0.1:$Port
set OLLAMA_MODELS=$ModelsDir
set OLLAMA_KEEP_ALIVE=-1
set OLLAMA_NUM_PARALLEL=1
"%~dp0ollama.exe" serve >> "%~dp0ollama.log" 2>&1
"@
Set-Content -Path (Join-Path $InstallDir 'start-ollama.cmd') -Value $cmd -Encoding ASCII
# The same settings for the ollama command an administrator types (ollama list, ollama ps).
[Environment]::SetEnvironmentVariable('OLLAMA_HOST', "127.0.0.1:$Port", 'Machine')
[Environment]::SetEnvironmentVariable('OLLAMA_MODELS', $ModelsDir, 'Machine')

# --- 4. Scheduled task at boot, restarted if it stops ---
Write-Step "Registering the startup task '$TaskName'"
$action = New-ScheduledTaskAction -Execute (Join-Path $InstallDir 'start-ollama.cmd') -WorkingDirectory $InstallDir
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 `
    -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal `
    -Settings $settings -Description 'Local AI model for Jadwal (docs/LOCAL-AI-OPTIONS.md).' -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName
Write-Ok "Registered and started"

Write-Step "Checking that Ollama answers on 127.0.0.1:$Port"
$tags = $null
for ($i = 0; $i -lt 60 -and -not $tags; $i++) {
    try { $tags = Invoke-RestMethod "http://127.0.0.1:$Port/api/tags" -TimeoutSec 2 } catch { Start-Sleep -Seconds 1 }
}
if (-not $tags) { throw "Ollama did not answer. See $(Join-Path $InstallDir 'ollama.log')." }
$names = @($tags.models | ForEach-Object { $_.name })
Write-Ok ("Models: " + ($names -join ', '))

# --- 5. Which model, and the setting ---
$suggested = $names | Where-Object { $_ -like 'gemma4:*' } | Select-Object -First 1
$gpuNote = 'No NVIDIA GPU found (nvidia-smi); on CPU only, answers take minutes.'
$smi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
if ($smi) {
    $memMb = (& nvidia-smi --query-gpu=memory.total --format=csv,noheader,nounits | Select-Object -First 1).Trim()
    $gpuNote = "NVIDIA GPU with $memMb MB."
    if ([int]$memMb -lt 12000) {
        $suggested = $names | Where-Object { $_ -like 'qwen3.5:*' } | Select-Object -First 1
    }
}
if (-not $suggested) { $suggested = $names | Select-Object -First 1 }

Write-Step "Done"
Write-Host "  $gpuNote"
Write-Host "  Suggested model: $suggested"
Write-Host ""
Write-Host "  In the API's appsettings.json, set:" -ForegroundColor Cyan
Write-Host @"
  "Ai": {
    "Enabled": true,
    "BaseUrl": "http://127.0.0.1:$Port/v1",
    "Model": "$suggested",
    "TimeoutSeconds": 360,
    "DisableThinking": true
  }
"@
Write-Host "  Then restart the API, open System Settings > AI assistant (it should say Ready), and"
Write-Host "  grant the 'Use the AI assistant' permission to the roles that should have it."
