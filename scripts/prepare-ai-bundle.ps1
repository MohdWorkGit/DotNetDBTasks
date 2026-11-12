#requires -Version 5.1
<#
.SYNOPSIS
    Gathers the local AI model runtime (Ollama) and the AI models into one folder
    (offline-ai-bundle/ by default), to carry to the air-gapped server.

.DESCRIPTION
    Run this on an INTERNET-CONNECTED machine. It produces:
      - ollama/ollama-windows-amd64.zip   : the Ollama runtime (CPU + NVIDIA CUDA)
      - ollama/ollama-windows-amd64-rocm.zip : (with -IncludeRocm) the AMD GPU build
      - models/                           : the models, in Ollama's own folder layout
      - install-ai-service.ps1            : installs it all on the server (run as administrator)
      - MANIFEST.txt                      : versions, models and sizes
      - SHA256SUMS.txt                    : a checksum for every file, checked by the installer

    The AI bundle is kept apart from offline-bundle/ on purpose: the models are many gigabytes
    and rarely change, while the application bundle is rebuilt for every security patch.
    See docs/ai.html and docs/deploy.html#ai-bundle.

    The models are pulled through a private Ollama started from the downloaded zip, on its own
    port and models folder, so an Ollama already running on this machine is not touched.

.PARAMETER OutDir
    Output folder. Default: ./offline-ai-bundle (gitignored). Needs about 20 GB free with the
    default models; this machine's C: drive may be too full, so point it at another drive.

.PARAMETER Models
    Ollama model tags to carry. Default: gemma4:12b (a 12-16 GB GPU; best Arabic at its size)
    and qwen3.5:9b (an 8 GB GPU). Pick one at install time by the server's GPU memory.

.PARAMETER OllamaVersion
    The Ollama release to download. Pinned rather than "latest" so the bundle is repeatable;
    bump it deliberately.

.PARAMETER IncludeRocm
    Also download Ollama's AMD (ROCm) build, for a server with an AMD GPU.

.PARAMETER ReuseModelsFrom
    An Ollama models folder on this machine (e.g. $env:USERPROFILE\.ollama\models). Models found
    there are checked against their checksums and linked or copied into the bundle instead of
    being downloaded again; the rest are pulled.

.PARAMETER AllowUntrustedDownload
    For a machine that does not trust the certificate of GitHub's download host (it lacks a root
    such as ISRG Root X1). The Ollama zip is then fetched without the certificate check and kept
    only if it matches the SHA-256 that api.github.com publishes for it. Without a published
    checksum the download is refused.

.PARAMETER Clean
    Delete the output folder before starting.

.EXAMPLE
    ./scripts/prepare-ai-bundle.ps1 -OutDir 'Y:\Jadwal-AI-Bundle'

.EXAMPLE
    ./scripts/prepare-ai-bundle.ps1 -OutDir 'E:\ai' -Models 'gemma4:12b' -IncludeRocm -Clean

.EXAMPLE
    ./scripts/prepare-ai-bundle.ps1 -Models qwen3:4b -ReuseModelsFrom "$env:USERPROFILE\.ollama\models" -AllowUntrustedDownload
    A small bundle (about 4 GB) for a CPU-only or small-GPU server, reusing the model already
    pulled on this machine.
#>
[CmdletBinding()]
param(
    [string]$OutDir = "offline-ai-bundle",
    [string[]]$Models = @('gemma4:12b', 'qwen3.5:9b'),
    [string]$OllamaVersion = '0.40.16',
    [switch]$IncludeRocm,
    [string]$ReuseModelsFrom,
    [switch]$AllowUntrustedDownload,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is many times slower with the progress bar

$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($OutDir)) {
    $OutDir = Join-Path $RepoRoot $OutDir
}

# The private Ollama used to pull the models: its own port, so a running one is left alone.
$PullPort = 11535
# Rough sizes (GB) of the default models in their Q4 form, for the free-space check.
$KnownModelGb = @{ 'gemma4:12b' = 9.5; 'qwen3.5:9b' = 6.6; 'gpt-oss:20b' = 14; 'qwen3:4b' = 2.5 }

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  [ok] $msg" -ForegroundColor Green }
function Write-Warn2($msg){ Write-Host "  [!!] $msg" -ForegroundColor Yellow }

# Where a model's manifest sits in Ollama's folder layout.
function Get-ManifestPath($root, $model) {
    $name, $tag = $model.Split(':', 2)
    if (-not $tag) { $tag = 'latest' }
    if ($name -notmatch '/') { $name = "library/$name" }
    return Join-Path $root ("manifests\registry.ollama.ai\{0}\{1}" -f ($name -replace '/', '\'), $tag)
}

# --- Preflight: space on the output drive ---
Write-Step "Preflight"
if ($Clean -and (Test-Path $OutDir)) {
    Write-Host "  Deleting $OutDir"
    Remove-Item -Recurse -Force $OutDir
}
$null = New-Item -ItemType Directory -Force -Path $OutDir
# A model reused from a folder on the same drive is hard-linked, so it takes no new space.
$outRoot = [System.IO.Path]::GetPathRoot((Resolve-Path $OutDir).Path)
$neededGb = 2 + ($Models | ForEach-Object {
    $linked = $ReuseModelsFrom -and (Test-Path (Get-ManifestPath $ReuseModelsFrom $_)) -and
        ([System.IO.Path]::GetPathRoot((Resolve-Path $ReuseModelsFrom).Path) -eq $outRoot)
    if ($linked) { 0 } elseif ($KnownModelGb.ContainsKey($_)) { $KnownModelGb[$_] } else { 10 }
} | Measure-Object -Sum).Sum
$root = [System.IO.Path]::GetPathRoot((Resolve-Path $OutDir).Path)
$freeGb = $null
try { $freeGb = [math]::Round((New-Object System.IO.DriveInfo($root)).AvailableFreeSpace / 1GB, 1) } catch { }
if ($null -ne $freeGb) {
    if ($freeGb -lt $neededGb) {
        throw "About $neededGb GB is needed in '$OutDir', but $root has $freeGb GB free. Pass -OutDir on a drive with more space."
    }
    Write-Ok "$freeGb GB free on $root (about $neededGb GB needed)"
} else {
    Write-Warn2 "Could not read the free space on $root (a network share?). About $neededGb GB is needed."
}

# --- Ollama runtime ---
# Each file is checked against the SHA-256 that GitHub's API publishes for it. The API answers
# from api.github.com, a different host from the one serving the file, so the check holds even
# when the download itself cannot be verified (see -AllowUntrustedDownload).
$ollamaDir = Join-Path $OutDir 'ollama'
$null = New-Item -ItemType Directory -Force -Path $ollamaDir
$assets = @('ollama-windows-amd64.zip')
if ($IncludeRocm) { $assets += 'ollama-windows-amd64-rocm.zip' }

Write-Step "Reading the published checksums of Ollama $OllamaVersion"
$digests = @{}
try {
    $release = Invoke-RestMethod "https://api.github.com/repos/ollama/ollama/releases/tags/v$OllamaVersion" `
        -Headers @{ 'User-Agent' = 'jadwal-ai-bundle' } -UseBasicParsing
    foreach ($a in $release.assets) {
        if ($a.digest -match '^sha256:([0-9a-f]{64})$') { $digests[$a.name] = $Matches[1] }
    }
    Write-Ok "$($digests.Count) checksums"
} catch {
    Write-Warn2 "Could not read them from api.github.com: $($_.Exception.Message)"
}

function Test-Digest($file, $asset) {
    if (-not $digests.ContainsKey($asset)) { return $null }
    return (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -eq $digests[$asset]
}

foreach ($asset in $assets) {
    Write-Step "Downloading $asset (Ollama $OllamaVersion)"
    $target = Join-Path $ollamaDir $asset
    if ((Test-Path $target) -and (Test-Digest $target $asset) -ne $false) {
        Write-Ok "Already here"
        continue
    }
    $url = "https://github.com/ollama/ollama/releases/download/v$OllamaVersion/$asset"
    try {
        Invoke-WebRequest -Uri $url -OutFile $target -UseBasicParsing
    } catch {
        if (-not $AllowUntrustedDownload) {
            throw "Downloading $asset failed: $($_.Exception.Message) If the cause is an untrusted certificate (this machine lacks a root such as ISRG Root X1), rerun with -AllowUntrustedDownload: the file is then accepted only if it matches the published checksum."
        }
        if (-not $digests.ContainsKey($asset)) { throw "No published checksum for $asset, so it cannot be downloaded without a trusted certificate." }
        Write-Warn2 "The download host's certificate is not trusted here; fetching without the certificate check and verifying the checksum instead."
        & curl.exe --fail --location --silent --show-error --insecure --output $target $url
        if ($LASTEXITCODE -ne 0) { throw "Downloading $asset failed (curl exit $LASTEXITCODE)." }
    }
    $ok = Test-Digest $target $asset
    if ($ok -eq $false) {
        Remove-Item $target -Force
        throw "$asset does not match its published SHA-256; deleted. Do not use it."
    }
    if ($null -eq $ok) { Write-Warn2 "No published checksum to compare $asset with." }
    else { Write-Ok "Checksum matches GitHub's published SHA-256" }
    Write-Ok ("{0} ({1:N0} MB)" -f $asset, ((Get-Item $target).Length / 1MB))
}

# --- Models, in Ollama's own folder layout: manifests\registry.ollama.ai\library\<name>\<tag>
#     plus the blobs\sha256-<digest> files each manifest names.
Write-Step "Models: $($Models -join ', ')"
$modelsDir = Join-Path $OutDir 'models'
$null = New-Item -ItemType Directory -Force -Path (Join-Path $modelsDir 'blobs')

# Models already on this machine are linked (same drive) or copied, not downloaded again. A blob's
# file name is its own SHA-256, so each one is checked before it goes into the bundle.
$toPull = @()
foreach ($model in $Models) {
    $source = if ($ReuseModelsFrom) { Get-ManifestPath $ReuseModelsFrom $model } else { $null }
    if (-not $source -or -not (Test-Path $source)) { $toPull += $model; continue }
    $manifestJson = Get-Content $source -Raw | ConvertFrom-Json
    $digestsNeeded = @($manifestJson.config.digest) + @($manifestJson.layers | ForEach-Object { $_.digest })
    foreach ($d in $digestsNeeded) {
        $blobName = $d -replace ':', '-'
        $from = Join-Path $ReuseModelsFrom "blobs\$blobName"
        $to = Join-Path $modelsDir "blobs\$blobName"
        if (-not (Test-Path $from)) { throw "$model is incomplete in $ReuseModelsFrom ($blobName is missing); pull it instead." }
        if ((Get-FileHash $from -Algorithm SHA256).Hash.ToLowerInvariant() -ne ($d -replace '^sha256:', '')) {
            throw "$from does not match its own name: the local copy of $model is damaged."
        }
        if (-not (Test-Path $to)) {
            $sameDrive = [System.IO.Path]::GetPathRoot($from) -eq [System.IO.Path]::GetPathRoot($to)
            if ($sameDrive) { $null = New-Item -ItemType HardLink -Path $to -Target $from }
            else { Copy-Item $from $to }
        }
    }
    $manifestTarget = Get-ManifestPath $modelsDir $model
    $null = New-Item -ItemType Directory -Force -Path (Split-Path $manifestTarget)
    Copy-Item $source $manifestTarget -Force
    Write-Ok "$model taken from $ReuseModelsFrom (checked)"
}

# The rest are pulled by a private Ollama started from the downloaded zip, on its own port and
# models folder, so an Ollama already running on this machine is left alone.
if ($toPull.Count -gt 0) {
    $toolDir = Join-Path $env:TEMP "jadwal-ollama-$OllamaVersion"
    if (-not (Test-Path (Join-Path $toolDir 'ollama.exe'))) {
        Expand-Archive -Path (Join-Path $ollamaDir 'ollama-windows-amd64.zip') -DestinationPath $toolDir -Force
    }
    $ollamaExe = Join-Path $toolDir 'ollama.exe'
    $saved = @{ OLLAMA_MODELS = $env:OLLAMA_MODELS; OLLAMA_HOST = $env:OLLAMA_HOST }
    $env:OLLAMA_MODELS = $modelsDir
    $env:OLLAMA_HOST = "127.0.0.1:$PullPort"
    $server = Start-Process -FilePath $ollamaExe -ArgumentList 'serve' -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $OutDir 'pull-server.log') -RedirectStandardError (Join-Path $OutDir 'pull-server.err.log')
    try {
        $ready = $false
        for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
            try { Invoke-RestMethod "http://127.0.0.1:$PullPort/api/version" -TimeoutSec 2 | Out-Null; $ready = $true }
            catch { Start-Sleep -Seconds 1 }
        }
        if (-not $ready) { throw "The private Ollama did not start on port $PullPort; see pull-server.err.log." }
        foreach ($model in $toPull) {
            Write-Host "  pulling $model ..."
            & $ollamaExe pull $model
            if ($LASTEXITCODE -ne 0) { throw "Pulling $model failed (exit $LASTEXITCODE)." }
            Write-Ok $model
        }
    }
    finally {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        $env:OLLAMA_MODELS = $saved.OLLAMA_MODELS
        $env:OLLAMA_HOST = $saved.OLLAMA_HOST
    }
    Start-Sleep -Seconds 1
    Remove-Item (Join-Path $OutDir 'pull-server.log'), (Join-Path $OutDir 'pull-server.err.log') -ErrorAction SilentlyContinue
}

# What the bundle holds, read from its manifests (the same list "ollama list" shows).
$installed = foreach ($model in $Models) {
    $m = Get-Content (Get-ManifestPath $modelsDir $model) -Raw | ConvertFrom-Json
    "  {0,-16} {1,6:N1} GB" -f $model, ((@($m.layers | ForEach-Object { $_.size }) | Measure-Object -Sum).Sum / 1GB)
}

# --- Installer, manifest, checksums ---
Write-Step "Writing the installer, MANIFEST.txt and SHA256SUMS.txt"
Copy-Item (Join-Path $PSScriptRoot 'install-ai-service.ps1') $OutDir -Force

$modelSize = (Get-ChildItem $modelsDir -Recurse -File | Measure-Object Length -Sum).Sum
$manifest = @"
Jadwal offline AI bundle
Built:          $(Get-Date -Format 'yyyy-MM-dd HH:mm') on $env:COMPUTERNAME
Ollama:         $OllamaVersion  ($($assets -join ', '))
Models:         $($Models -join ', ')
Models size:    $([math]::Round($modelSize / 1GB, 1)) GB

Which model to use (set Ai:Model in the API's appsettings.json):
  - 12-16 GB GPU memory: gemma4:12b
  - 8 GB GPU memory:     qwen3.5:9b
  - CPU only, or a stopgap: qwen3:4b (answers take minutes on a CPU)
  The installer reads the GPU (nvidia-smi) and suggests one of the models in this bundle.

On the air-gapped server, as administrator:
  powershell -ExecutionPolicy Bypass -File .\install-ai-service.ps1
Then follow docs\deploy.html, "The AI bundle" (in the application
bundle's docs folder, or the Jadwal source).

Models in this bundle:
$($installed -join "`r`n")
"@
Set-Content -Path (Join-Path $OutDir 'MANIFEST.txt') -Value $manifest -Encoding UTF8

# One line per file, "<sha256>  <relative path>", as sha256sum writes it; the installer checks
# every line before it copies anything, so a damaged copy is caught on the server.
$sums = Get-ChildItem $OutDir -Recurse -File |
    Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
    ForEach-Object {
        $relative = $_.FullName.Substring($OutDir.TrimEnd('\').Length + 1)
        "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
    }
Set-Content -Path (Join-Path $OutDir 'SHA256SUMS.txt') -Value $sums -Encoding UTF8

$total = (Get-ChildItem $OutDir -Recurse -File | Measure-Object Length -Sum).Sum
Write-Step "Done"
Write-Ok ("{0} ({1:N1} GB). Copy the whole folder to the server." -f $OutDir, ($total / 1GB))
