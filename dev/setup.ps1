#Requires -Version 5.1
<#
.SYNOPSIS
    Knutr Development Environment Setup

.DESCRIPTION
    This script sets up a complete development environment for Knutr including:
    - Docker services (Prometheus, Tempo, Grafana, Ollama, ngrok)
    - Optional GitLab CE instance for testing the GitLab plugin
    - .NET project build
    - User secrets initialization

.PARAMETER WithGitLab
    Include GitLab CE container (heavy, ~4GB RAM required)

.PARAMETER SkipBuild
    Skip the .NET build step

.PARAMETER Reset
    Stop and remove all containers before starting

.EXAMPLE
    .\setup.ps1
    Basic setup without GitLab

.EXAMPLE
    .\setup.ps1 -WithGitLab
    Full setup with GitLab CE for testing

.EXAMPLE
    .\setup.ps1 -Reset
    Clean restart of all services
#>

[CmdletBinding()]
param(
    [switch]$WithGitLab,
    [switch]$SkipBuild,
    [switch]$Reset
)

$ErrorActionPreference = "Stop"

# Paths
$ScriptDir = $PSScriptRoot
$RootDir = Split-Path $ScriptDir -Parent

#-------------------------------------------------------------------------------
# Helper functions
#-------------------------------------------------------------------------------
function Write-Info { param([string]$Message) Write-Host "[INFO] $Message" -ForegroundColor Blue }
function Write-Success { param([string]$Message) Write-Host "[OK] $Message" -ForegroundColor Green }
function Write-Warn { param([string]$Message) Write-Host "[WARN] $Message" -ForegroundColor Yellow }
function Write-Err { param([string]$Message) Write-Host "[ERROR] $Message" -ForegroundColor Red }

function Test-Command {
    param([string]$Command)

    $cmd = Get-Command $Command -ErrorAction SilentlyContinue
    if ($null -eq $cmd) {
        Write-Err "$Command is not installed or not in PATH"
        return $false
    }
    Write-Success "$Command found: $($cmd.Source)"
    return $true
}

function Wait-ForUrl {
    param(
        [string]$Url,
        [string]$Name,
        [int]$MaxAttempts = 30
    )

    Write-Info "Waiting for $Name to be ready..."
    $attempt = 1

    while ($attempt -le $MaxAttempts) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2 -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 200) {
                Write-Success "$Name is ready"
                return $true
            }
        }
        catch {
            # Ignore errors, just retry
        }

        Write-Host "." -NoNewline
        Start-Sleep -Seconds 2
        $attempt++
    }

    Write-Host ""
    Write-Warn "$Name did not become ready within timeout"
    return $false
}

function Wait-ForGitLab {
    $MaxAttempts = 90  # GitLab takes a while
    $attempt = 1

    Write-Info "Waiting for GitLab to be ready (this can take 3-5 minutes)..."

    while ($attempt -le $MaxAttempts) {
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:8080/-/health" -UseBasicParsing -TimeoutSec 2 -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 200) {
                Write-Success "GitLab is ready"
                return $true
            }
        }
        catch {
            # Ignore errors, just retry
        }

        Write-Host "." -NoNewline
        Start-Sleep -Seconds 4
        $attempt++
    }

    Write-Host ""
    Write-Warn "GitLab did not become ready within timeout (may still be starting)"
    return $false
}

#-------------------------------------------------------------------------------
# Main setup
#-------------------------------------------------------------------------------
Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  Knutr Development Environment Setup" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Check prerequisites
Write-Info "Checking prerequisites..."
$prereqOk = $true

if (-not (Test-Command "docker")) { $prereqOk = $false }
if (-not (Test-Command "dotnet")) { $prereqOk = $false }

# Check Docker is running
try {
    $null = docker info 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Err "Docker is not running. Please start Docker and try again."
        $prereqOk = $false
    }
}
catch {
    Write-Err "Docker is not running. Please start Docker and try again."
    $prereqOk = $false
}

if (-not $prereqOk) {
    Write-Err "Prerequisites check failed. Please install missing tools."
    exit 1
}

Write-Success "All prerequisites satisfied"
Write-Host ""

# Reset if requested
if ($Reset) {
    Write-Info "Stopping and removing existing containers..."
    Push-Location $ScriptDir
    try {
        docker compose --profile gitlab down -v 2>$null
    }
    catch {
        # Ignore errors
    }
    Pop-Location
    Write-Success "Containers removed"
    Write-Host ""
}

# Setup .env file
Write-Info "Setting up environment file..."
$envFile = Join-Path $ScriptDir ".env"
$envExample = Join-Path $ScriptDir ".env.example"

if (-not (Test-Path $envFile)) {
    Copy-Item $envExample $envFile
    Write-Success "Created .env from .env.example"
    Write-Warn "Edit dev\.env to add your NGROK_AUTHTOKEN and NGROK_DOMAIN if needed"
}
else {
    Write-Success ".env file already exists"
}
Write-Host ""

# Start Docker services
Write-Info "Starting Docker services..."
Push-Location $ScriptDir

if ($WithGitLab) {
    Write-Info "Including GitLab CE (this will use ~4GB RAM)..."
    docker compose --profile gitlab up -d
}
else {
    docker compose up -d
}

Pop-Location

if ($LASTEXITCODE -ne 0) {
    Write-Err "Failed to start Docker services"
    exit 1
}

Write-Success "Docker services started"
Write-Host ""

# Wait for services
Write-Info "Waiting for services to be healthy..."
$null = Wait-ForUrl "http://localhost:9090/-/healthy" "Prometheus" 15
$null = Wait-ForUrl "http://localhost:3000/api/health" "Grafana" 15
$null = Wait-ForUrl "http://localhost:11434/api/tags" "Ollama" 30

if ($WithGitLab) {
    $null = Wait-ForGitLab
}
Write-Host ""

# Build .NET project
if (-not $SkipBuild) {
    Write-Info "Building .NET project..."
    Push-Location $RootDir

    $buildResult = dotnet build Knutr.sln

    if ($LASTEXITCODE -eq 0) {
        Write-Success ".NET build completed"
    }
    else {
        Write-Err ".NET build failed"
        Pop-Location
        exit 1
    }

    Pop-Location
    Write-Host ""
}

# Setup user secrets
Write-Info "Checking user secrets..."
$hostingDir = Join-Path $RootDir "src\Knutr.Hosting"
$userSecretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets"

$secretsExist = $false
if (Test-Path $userSecretsPath) {
    $secretsExist = (Get-ChildItem $userSecretsPath -Directory -ErrorAction SilentlyContinue).Count -gt 0
}

if (-not $secretsExist) {
    Write-Info "Initializing user secrets..."
    Push-Location $hostingDir

    try {
        dotnet user-secrets init 2>$null
        dotnet user-secrets set "Slack:BotToken" "xoxb-dev-placeholder" 2>$null
        dotnet user-secrets set "Slack:SigningSecret" "dev-signing-secret" 2>$null

        if ($WithGitLab) {
            dotnet user-secrets set "GitLab:BaseUrl" "http://localhost:8080" 2>$null
            dotnet user-secrets set "GitLab:AccessToken" "dev-token-placeholder" 2>$null
        }

        Write-Success "User secrets initialized with placeholder values"
        Write-Warn "Update secrets with real values: dotnet user-secrets set `"Slack:BotToken`" `"xoxb-real-token`""
    }
    catch {
        Write-Warn "Could not initialize user secrets: $_"
    }

    Pop-Location
}
else {
    Write-Success "User secrets already configured"
}
Write-Host ""

#-------------------------------------------------------------------------------
# Print summary
#-------------------------------------------------------------------------------
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  Setup Complete!" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Services running:" -ForegroundColor White
Write-Host "  - Prometheus:  http://localhost:9090"
Write-Host "  - Grafana:     http://localhost:3000  (admin/admin)"
Write-Host "  - Tempo:       http://localhost:3200"
Write-Host "  - Ollama:      http://localhost:11434"
Write-Host "  - ngrok UI:    http://localhost:4040"

if ($WithGitLab) {
    Write-Host "  - GitLab:      http://localhost:8080"
    Write-Host ""
    Write-Host "GitLab Setup:" -ForegroundColor Yellow
    Write-Host "  1. Wait for GitLab to fully start (check http://localhost:8080)"
    Write-Host "  2. Get root password: docker exec knutr-gitlab grep 'Password:' /etc/gitlab/initial_root_password"
    Write-Host "  3. Login as 'root' with that password"
    Write-Host "  4. Create a personal access token with 'api' scope"
    Write-Host "  5. Update user secrets: dotnet user-secrets set `"GitLab:AccessToken`" `"your-token`""
}

Write-Host ""
Write-Host "To run Knutr:" -ForegroundColor White
Write-Host "  cd $RootDir"
Write-Host "  dotnet run --project src\Knutr.Hosting"
Write-Host ""
Write-Host "To stop services:" -ForegroundColor White
Write-Host "  cd $ScriptDir"
if ($WithGitLab) {
    Write-Host "  docker compose --profile gitlab down"
}
else {
    Write-Host "  docker compose down"
}
Write-Host ""
