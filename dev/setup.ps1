#Requires -Version 5.1
<#
.SYNOPSIS
    Knutr Development Environment Setup

.DESCRIPTION
    This script sets up a complete development environment for Knutr including:
    - Docker services (Prometheus, Tempo, Grafana, Ollama, ngrok)
    - Optional GitLab CE instance for testing the GitLab plugin
    - Ollama model download
    - .NET project build
    - User secrets initialization (including Slack credentials)

.PARAMETER WithGitLab
    Include GitLab CE container (heavy, ~4GB RAM required)

.PARAMETER SkipBuild
    Skip the .NET build step

.PARAMETER Reset
    Stop and remove all containers before starting

.PARAMETER ConfigureSlack
    Interactively configure Slack credentials

.PARAMETER Model
    Ollama model to pull (default: llama3.2:1b)

.EXAMPLE
    .\setup.ps1
    Basic setup without GitLab

.EXAMPLE
    .\setup.ps1 -WithGitLab
    Full setup with GitLab CE for testing

.EXAMPLE
    .\setup.ps1 -ConfigureSlack
    Setup with Slack credential prompts

.EXAMPLE
    .\setup.ps1 -Model llama3
    Use a different Ollama model

.EXAMPLE
    .\setup.ps1 -Reset
    Clean restart of all services
#>

[CmdletBinding()]
param(
    [switch]$WithGitLab,
    [switch]$SkipBuild,
    [switch]$Reset,
    [switch]$ConfigureSlack,
    [string]$Model = "llama3.2:1b"
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

function Install-OllamaModel {
    param([string]$ModelName)

    Write-Info "Checking if Ollama model '$ModelName' is available..."

    # Check if model already exists
    $modelList = docker exec knutr-ollama ollama list 2>$null
    if ($modelList -match "^$ModelName") {
        Write-Success "Model '$ModelName' already available"
        return $true
    }

    Write-Info "Pulling Ollama model '$ModelName' (this may take a few minutes)..."
    $result = docker exec knutr-ollama ollama pull $ModelName 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Success "Model '$ModelName' pulled successfully"
        return $true
    }
    else {
        Write-Warn "Failed to pull model '$ModelName'. You can pull it manually with:"
        Write-Warn "  docker exec knutr-ollama ollama pull $ModelName"
        return $false
    }
}

function Read-SecurePrompt {
    param(
        [string]$Prompt,
        [string]$CurrentValue
    )

    $isPlaceholder = ($CurrentValue -eq "xoxb-dev-placeholder") -or ($CurrentValue -eq "dev-signing-secret")

    if ($CurrentValue -and -not $isPlaceholder) {
        Write-Host $Prompt -ForegroundColor Cyan
        $keep = Read-Host "  Current value exists. Keep it? [Y/n]"
        if ($keep -match "^[Nn]") {
            $secure = Read-Host "  Enter new value" -AsSecureString
            $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
            return [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
        }
        return $CurrentValue
    }
    else {
        Write-Host $Prompt -ForegroundColor Cyan
        $secure = Read-Host "  Enter value (or press Enter to skip)" -AsSecureString
        $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        return [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
    }
}

function Set-SlackSecrets {
    param([string]$HostingDir)

    Write-Host ""
    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host "  Slack Credentials Configuration" -ForegroundColor Cyan
    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Your Slack credentials will be stored securely using .NET User Secrets."
    Write-Host "They will NOT be committed to source control."
    Write-Host ""
    Write-Host "You can find these values in your Slack App settings:"
    Write-Host "  - Bot Token: OAuth & Permissions -> Bot User OAuth Token (starts with xoxb-)"
    Write-Host "  - Signing Secret: Basic Information -> App Credentials -> Signing Secret"
    Write-Host ""

    Push-Location $HostingDir

    # Get current values
    $secretsList = dotnet user-secrets list 2>$null
    $currentToken = ""
    $currentSecret = ""

    if ($secretsList) {
        $tokenLine = $secretsList | Where-Object { $_ -match "Slack:BotToken" }
        if ($tokenLine) {
            $currentToken = ($tokenLine -split "=", 2)[1].Trim()
        }
        $secretLine = $secretsList | Where-Object { $_ -match "Slack:SigningSecret" }
        if ($secretLine) {
            $currentSecret = ($secretLine -split "=", 2)[1].Trim()
        }
    }

    # Prompt for Bot Token
    $botToken = Read-SecurePrompt "Slack Bot Token (xoxb-...):" $currentToken

    if ($botToken) {
        dotnet user-secrets set "Slack:BotToken" $botToken 2>$null | Out-Null
        Write-Success "Slack:BotToken configured"
    }
    else {
        Write-Warn "Slack:BotToken skipped"
    }

    # Prompt for Signing Secret
    $signingSecret = Read-SecurePrompt "Slack Signing Secret:" $currentSecret

    if ($signingSecret) {
        dotnet user-secrets set "Slack:SigningSecret" $signingSecret 2>$null | Out-Null
        Write-Success "Slack:SigningSecret configured"
    }
    else {
        Write-Warn "Slack:SigningSecret skipped"
    }

    Pop-Location
    Write-Host ""
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

# Pull Ollama model
$null = Install-OllamaModel $Model
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
Write-Info "Setting up user secrets..."
$hostingDir = Join-Path $RootDir "src\Knutr.Hosting"

Push-Location $hostingDir

# Initialize user secrets if needed
$secretsCheck = dotnet user-secrets list 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Info "Initializing user secrets..."
    dotnet user-secrets init 2>$null | Out-Null
}

# Configure Slack interactively if requested
if ($ConfigureSlack) {
    Set-SlackSecrets $hostingDir
}
else {
    # Check if Slack secrets exist
    $secretsList = dotnet user-secrets list 2>$null
    $slackToken = ""

    if ($secretsList) {
        $tokenLine = $secretsList | Where-Object { $_ -match "Slack:BotToken" }
        if ($tokenLine) {
            $slackToken = ($tokenLine -split "=", 2)[1].Trim()
        }
    }

    $isPlaceholder = ($slackToken -eq "xoxb-dev-placeholder") -or (-not $slackToken)

    if ($isPlaceholder) {
        Write-Warn "Slack credentials not configured."
        Write-Host ""
        $configureNow = Read-Host "Would you like to configure Slack credentials now? [y/N]"
        if ($configureNow -match "^[Yy]") {
            Set-SlackSecrets $hostingDir
        }
        else {
            # Set placeholder values
            dotnet user-secrets set "Slack:BotToken" "xoxb-dev-placeholder" 2>$null | Out-Null
            dotnet user-secrets set "Slack:SigningSecret" "dev-signing-secret" 2>$null | Out-Null
            Write-Warn "Using placeholder values. Run with -ConfigureSlack later to set real credentials."
        }
    }
    else {
        Write-Success "Slack credentials already configured"
    }
}

# Configure GitLab secrets if using GitLab
if ($WithGitLab) {
    dotnet user-secrets set "GitLab:BaseUrl" "http://localhost:8080" 2>$null | Out-Null

    $secretsList = dotnet user-secrets list 2>$null
    $gitlabToken = ""

    if ($secretsList) {
        $tokenLine = $secretsList | Where-Object { $_ -match "GitLab:AccessToken" }
        if ($tokenLine) {
            $gitlabToken = ($tokenLine -split "=", 2)[1].Trim()
        }
    }

    $isPlaceholder = ($gitlabToken -eq "dev-token-placeholder") -or (-not $gitlabToken)

    if ($isPlaceholder) {
        dotnet user-secrets set "GitLab:AccessToken" "dev-token-placeholder" 2>$null | Out-Null
        Write-Warn "GitLab token not configured. Set it after GitLab starts."
    }
}

Pop-Location
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
Write-Host "  - Ollama:      http://localhost:11434  (model: $Model)"
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
Write-Host "Secrets Management:" -ForegroundColor White
Write-Host "  - Configure Slack:  .\setup.ps1 -ConfigureSlack"
Write-Host "  - List secrets:     cd src\Knutr.Hosting; dotnet user-secrets list"
Write-Host "  - Set a secret:     dotnet user-secrets set `"Key`" `"Value`""
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
