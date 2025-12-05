#!/usr/bin/env bash
set -euo pipefail

#===============================================================================
# Knutr Development Environment Setup
#===============================================================================
# This script sets up a complete development environment for Knutr including:
# - Docker services (Prometheus, Tempo, Grafana, Ollama, ngrok)
# - Optional GitLab CE instance for testing the GitLab plugin
# - .NET project build
# - User secrets initialization
#===============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Flags
WITH_GITLAB=false
SKIP_BUILD=false
RESET=false

#-------------------------------------------------------------------------------
# Helper functions
#-------------------------------------------------------------------------------
log_info() { echo -e "${BLUE}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[OK]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

show_help() {
    cat << EOF
Knutr Development Environment Setup

Usage: ./setup.sh [options]

Options:
    --with-gitlab    Include GitLab CE container (heavy, ~4GB RAM)
    --skip-build     Skip .NET build step
    --reset          Stop and remove all containers before starting
    -h, --help       Show this help message

Examples:
    ./setup.sh                     # Basic setup without GitLab
    ./setup.sh --with-gitlab       # Full setup with GitLab for testing
    ./setup.sh --reset             # Clean restart
EOF
}

check_command() {
    if ! command -v "$1" &> /dev/null; then
        log_error "$1 is not installed or not in PATH"
        return 1
    fi
    log_success "$1 found: $(command -v "$1")"
    return 0
}

wait_for_url() {
    local url="$1"
    local name="$2"
    local max_attempts="${3:-30}"
    local attempt=1

    log_info "Waiting for $name to be ready..."
    while [ $attempt -le $max_attempts ]; do
        if curl -sf "$url" > /dev/null 2>&1; then
            log_success "$name is ready"
            return 0
        fi
        echo -n "."
        sleep 2
        ((attempt++))
    done
    echo ""
    log_warn "$name did not become ready within timeout"
    return 1
}

wait_for_gitlab() {
    local max_attempts=90  # GitLab takes a while
    local attempt=1

    log_info "Waiting for GitLab to be ready (this can take 3-5 minutes)..."
    while [ $attempt -le $max_attempts ]; do
        # Check if GitLab health endpoint responds
        if curl -sf "http://localhost:8080/-/health" > /dev/null 2>&1; then
            log_success "GitLab is ready"
            return 0
        fi
        echo -n "."
        sleep 4
        ((attempt++))
    done
    echo ""
    log_warn "GitLab did not become ready within timeout (may still be starting)"
    return 1
}

#-------------------------------------------------------------------------------
# Parse arguments
#-------------------------------------------------------------------------------
while [[ $# -gt 0 ]]; do
    case $1 in
        --with-gitlab)
            WITH_GITLAB=true
            shift
            ;;
        --skip-build)
            SKIP_BUILD=true
            shift
            ;;
        --reset)
            RESET=true
            shift
            ;;
        -h|--help)
            show_help
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            show_help
            exit 1
            ;;
    esac
done

#-------------------------------------------------------------------------------
# Main setup
#-------------------------------------------------------------------------------
echo ""
echo "======================================"
echo "  Knutr Development Environment Setup"
echo "======================================"
echo ""

# Check prerequisites
log_info "Checking prerequisites..."
PREREQ_OK=true

check_command "docker" || PREREQ_OK=false
check_command "dotnet" || PREREQ_OK=false

# Check Docker is running
if ! docker info > /dev/null 2>&1; then
    log_error "Docker is not running. Please start Docker and try again."
    PREREQ_OK=false
fi

if [ "$PREREQ_OK" = false ]; then
    log_error "Prerequisites check failed. Please install missing tools."
    exit 1
fi

log_success "All prerequisites satisfied"
echo ""

# Reset if requested
if [ "$RESET" = true ]; then
    log_info "Stopping and removing existing containers..."
    cd "$SCRIPT_DIR"
    docker compose --profile gitlab down -v 2>/dev/null || true
    log_success "Containers removed"
    echo ""
fi

# Setup .env file
log_info "Setting up environment file..."
if [ ! -f "$SCRIPT_DIR/.env" ]; then
    cp "$SCRIPT_DIR/.env.example" "$SCRIPT_DIR/.env"
    log_success "Created .env from .env.example"
    log_warn "Edit dev/.env to add your NGROK_AUTHTOKEN and NGROK_DOMAIN if needed"
else
    log_success ".env file already exists"
fi
echo ""

# Start Docker services
log_info "Starting Docker services..."
cd "$SCRIPT_DIR"

if [ "$WITH_GITLAB" = true ]; then
    log_info "Including GitLab CE (this will use ~4GB RAM)..."
    docker compose --profile gitlab up -d
else
    docker compose up -d
fi

log_success "Docker services started"
echo ""

# Wait for services
log_info "Waiting for services to be healthy..."
wait_for_url "http://localhost:9090/-/healthy" "Prometheus" 15 || true
wait_for_url "http://localhost:3000/api/health" "Grafana" 15 || true
wait_for_url "http://localhost:11434/api/tags" "Ollama" 30 || true

if [ "$WITH_GITLAB" = true ]; then
    wait_for_gitlab || true
fi
echo ""

# Build .NET project
if [ "$SKIP_BUILD" = false ]; then
    log_info "Building .NET project..."
    cd "$ROOT_DIR"
    if dotnet build Knutr.sln; then
        log_success ".NET build completed"
    else
        log_error ".NET build failed"
        exit 1
    fi
    echo ""
fi

# Setup user secrets
log_info "Checking user secrets..."
HOSTING_DIR="$ROOT_DIR/src/Knutr.Hosting"

if [ ! -f "$HOSTING_DIR/secrets.json" ] && [ ! -d "$HOME/.microsoft/usersecrets" ]; then
    log_info "Initializing user secrets..."
    cd "$HOSTING_DIR"
    dotnet user-secrets init 2>/dev/null || true

    # Set placeholder values for development
    dotnet user-secrets set "Slack:BotToken" "xoxb-dev-placeholder" 2>/dev/null || true
    dotnet user-secrets set "Slack:SigningSecret" "dev-signing-secret" 2>/dev/null || true

    if [ "$WITH_GITLAB" = true ]; then
        dotnet user-secrets set "GitLab:BaseUrl" "http://localhost:8080" 2>/dev/null || true
        dotnet user-secrets set "GitLab:AccessToken" "dev-token-placeholder" 2>/dev/null || true
    fi

    log_success "User secrets initialized with placeholder values"
    log_warn "Update secrets with real values: dotnet user-secrets set \"Slack:BotToken\" \"xoxb-real-token\""
else
    log_success "User secrets already configured"
fi
echo ""

#-------------------------------------------------------------------------------
# Print summary
#-------------------------------------------------------------------------------
echo "======================================"
echo "  Setup Complete!"
echo "======================================"
echo ""
echo "Services running:"
echo "  - Prometheus:  http://localhost:9090"
echo "  - Grafana:     http://localhost:3000  (admin/admin)"
echo "  - Tempo:       http://localhost:3200"
echo "  - Ollama:      http://localhost:11434"
echo "  - ngrok UI:    http://localhost:4040"

if [ "$WITH_GITLAB" = true ]; then
    echo "  - GitLab:      http://localhost:8080"
    echo ""
    echo "GitLab Setup:"
    echo "  1. Wait for GitLab to fully start (check http://localhost:8080)"
    echo "  2. Get root password: docker exec knutr-gitlab grep 'Password:' /etc/gitlab/initial_root_password"
    echo "  3. Login as 'root' with that password"
    echo "  4. Create a personal access token with 'api' scope"
    echo "  5. Update user secrets: dotnet user-secrets set \"GitLab:AccessToken\" \"your-token\""
fi

echo ""
echo "To run Knutr:"
echo "  cd $ROOT_DIR"
echo "  dotnet run --project src/Knutr.Hosting"
echo ""
echo "To stop services:"
echo "  cd $SCRIPT_DIR"
if [ "$WITH_GITLAB" = true ]; then
    echo "  docker compose --profile gitlab down"
else
    echo "  docker compose down"
fi
echo ""
