#!/usr/bin/env bash
set -euo pipefail

#===============================================================================
# Knutr Development Environment Setup
#===============================================================================
# This script sets up a complete development environment for Knutr including:
# - Local DNS (hosts file entries for *.knutr.local)
# - Trusted SSL certificates (via mkcert)
# - Traefik reverse proxy
# - Docker services (Prometheus, Tempo, Grafana, Ollama, ngrok)
# - Optional GitLab CE instance for testing the GitLab plugin
# - Ollama model download
# - .NET project build
# - User secrets initialization (including Slack credentials)
#===============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
CERTS_DIR="$SCRIPT_DIR/certs"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Flags
WITH_GITLAB=false
SKIP_BUILD=false
RESET=false
CONFIGURE_SLACK=false
SKIP_HOSTS=false
SKIP_CERTS=false
OLLAMA_MODEL="${OLLAMA_MODEL:-llama3.2:1b}"

# Domains
KNUTR_DOMAINS=(
    "traefik.knutr.local"
    "grafana.knutr.local"
    "prometheus.knutr.local"
    "tempo.knutr.local"
    "ollama.knutr.local"
    "ngrok.knutr.local"
    "gitlab.knutr.local"
)

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
    --with-gitlab       Include GitLab CE container (heavy, ~4GB RAM)
    --skip-build        Skip .NET build step
    --skip-hosts        Skip hosts file configuration
    --skip-certs        Skip SSL certificate generation
    --reset             Stop and remove all containers before starting
    --configure-slack   Interactively configure Slack credentials
    --model <name>      Ollama model to pull (default: llama3.2:1b)
    -h, --help          Show this help message

Examples:
    ./setup.sh                          # Basic setup
    ./setup.sh --with-gitlab            # Full setup with GitLab for testing
    ./setup.sh --configure-slack        # Setup with Slack credential prompts
    ./setup.sh --model llama3           # Use a different Ollama model
    ./setup.sh --reset                  # Clean restart

Services (after setup):
    https://traefik.knutr.local     - Traefik dashboard
    https://grafana.knutr.local     - Grafana (admin/admin)
    https://prometheus.knutr.local  - Prometheus
    https://tempo.knutr.local       - Tempo
    https://ollama.knutr.local      - Ollama API
    https://gitlab.knutr.local      - GitLab CE (with --with-gitlab)

Environment Variables:
    OLLAMA_MODEL        Default Ollama model (default: llama3.2:1b)
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
        # Use -k (insecure) to skip certificate verification during health checks
        if curl -sSfk "$url" > /dev/null 2>&1; then
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

# Get the HTTPS port from env or default
get_https_port() {
    if [ -f "$SCRIPT_DIR/.env" ]; then
        local port=$(grep "^TRAEFIK_HTTPS_PORT=" "$SCRIPT_DIR/.env" 2>/dev/null | cut -d'=' -f2)
        echo "${port:-8443}"
    else
        echo "8443"
    fi
}

wait_for_gitlab() {
    local max_attempts=90
    local attempt=1
    local port=$(get_https_port)

    log_info "Waiting for GitLab to be ready (this can take 3-5 minutes)..."
    while [ $attempt -le $max_attempts ]; do
        if curl -sSfk "https://gitlab.knutr.local:${port}/-/health" > /dev/null 2>&1; then
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

pull_ollama_model() {
    local model="$1"

    log_info "Checking if Ollama model '$model' is available..."

    if docker exec knutr-ollama ollama list 2>/dev/null | grep -q "^$model"; then
        log_success "Model '$model' already available"
        return 0
    fi

    log_info "Pulling Ollama model '$model' (this may take a few minutes)..."
    if docker exec knutr-ollama ollama pull "$model"; then
        log_success "Model '$model' pulled successfully"
        return 0
    else
        log_warn "Failed to pull model '$model'. You can pull it manually with:"
        log_warn "  docker exec knutr-ollama ollama pull $model"
        return 1
    fi
}

#-------------------------------------------------------------------------------
# Hosts file configuration
#-------------------------------------------------------------------------------
configure_hosts() {
    log_info "Configuring hosts file..."

    local hosts_file="/etc/hosts"
    local hosts_entry="127.0.0.1"
    local needs_update=false

    # Check which domains are missing
    for domain in "${KNUTR_DOMAINS[@]}"; do
        if ! grep -q "$domain" "$hosts_file" 2>/dev/null; then
            hosts_entry="$hosts_entry $domain"
            needs_update=true
        fi
    done

    if [ "$needs_update" = false ]; then
        log_success "All domains already in hosts file"
        return 0
    fi

    log_info "Adding entries to hosts file (requires sudo)..."
    echo ""
    echo "The following entry will be added to $hosts_file:"
    echo -e "${CYAN}$hosts_entry${NC}"
    echo ""

    if [ "$(id -u)" -eq 0 ]; then
        echo "$hosts_entry" >> "$hosts_file"
    else
        echo "$hosts_entry" | sudo tee -a "$hosts_file" > /dev/null
    fi

    log_success "Hosts file updated"
}

#-------------------------------------------------------------------------------
# SSL certificate generation (mkcert)
#-------------------------------------------------------------------------------
setup_certificates() {
    log_info "Setting up SSL certificates..."

    # Check if certs already exist
    if [ -f "$CERTS_DIR/knutr.local.pem" ] && [ -f "$CERTS_DIR/knutr.local-key.pem" ]; then
        log_success "Certificates already exist"
        return 0
    fi

    # Check for mkcert
    if ! command -v mkcert &> /dev/null; then
        log_warn "mkcert not found. Installing..."

        if [[ "$OSTYPE" == "darwin"* ]]; then
            # macOS
            if command -v brew &> /dev/null; then
                brew install mkcert
            else
                log_error "Please install Homebrew first, then run: brew install mkcert"
                return 1
            fi
        elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
            # Linux
            if command -v apt-get &> /dev/null; then
                sudo apt-get update && sudo apt-get install -y libnss3-tools
                curl -JLO "https://dl.filippo.io/mkcert/latest?for=linux/amd64"
                chmod +x mkcert-v*-linux-amd64
                sudo mv mkcert-v*-linux-amd64 /usr/local/bin/mkcert
            elif command -v dnf &> /dev/null; then
                sudo dnf install -y nss-tools
                curl -JLO "https://dl.filippo.io/mkcert/latest?for=linux/amd64"
                chmod +x mkcert-v*-linux-amd64
                sudo mv mkcert-v*-linux-amd64 /usr/local/bin/mkcert
            else
                log_error "Please install mkcert manually: https://github.com/FiloSottile/mkcert#installation"
                return 1
            fi
        else
            log_error "Unsupported OS. Please install mkcert manually."
            return 1
        fi
    fi

    log_success "mkcert found"

    # Install local CA (one-time)
    log_info "Installing local CA (may require sudo)..."
    mkcert -install

    # Create certs directory
    mkdir -p "$CERTS_DIR"

    # Generate wildcard certificate
    log_info "Generating wildcard certificate for *.knutr.local..."
    cd "$CERTS_DIR"
    mkcert -cert-file knutr.local.pem -key-file knutr.local-key.pem \
        "*.knutr.local" "knutr.local" "localhost" "127.0.0.1" "::1"

    log_success "Certificates generated in $CERTS_DIR"
}

#-------------------------------------------------------------------------------
# Slack secrets configuration
#-------------------------------------------------------------------------------
prompt_secret() {
    local prompt="$1"
    local var_name="$2"
    local current_value="$3"

    if [ -n "$current_value" ] && [ "$current_value" != "xoxb-dev-placeholder" ] && [ "$current_value" != "dev-signing-secret" ]; then
        echo -e "${CYAN}$prompt${NC}"
        read -p "  Current value exists. Keep it? [Y/n]: " keep
        if [[ "$keep" =~ ^[Nn] ]]; then
            read -sp "  Enter new value: " value
            echo ""
            echo "$value"
        else
            echo "$current_value"
        fi
    else
        echo -e "${CYAN}$prompt${NC}"
        read -sp "  Enter value (or press Enter to skip): " value
        echo ""
        echo "$value"
    fi
}

configure_slack_secrets() {
    local hosting_dir="$1"

    echo ""
    echo -e "${CYAN}======================================${NC}"
    echo -e "${CYAN}  Slack Credentials Configuration${NC}"
    echo -e "${CYAN}======================================${NC}"
    echo ""
    echo "Your Slack credentials will be stored securely using .NET User Secrets."
    echo "They will NOT be committed to source control."
    echo ""
    echo "You can find these values in your Slack App settings:"
    echo "  - Bot Token: OAuth & Permissions → Bot User OAuth Token (starts with xoxb-)"
    echo "  - Signing Secret: Basic Information → App Credentials → Signing Secret"
    echo ""

    cd "$hosting_dir"

    local current_token=$(dotnet user-secrets list 2>/dev/null | grep "Slack:BotToken" | cut -d'=' -f2 | xargs || echo "")
    local current_secret=$(dotnet user-secrets list 2>/dev/null | grep "Slack:SigningSecret" | cut -d'=' -f2 | xargs || echo "")

    local bot_token
    bot_token=$(prompt_secret "Slack Bot Token (xoxb-...):" "Slack:BotToken" "$current_token")

    if [ -n "$bot_token" ]; then
        dotnet user-secrets set "Slack:BotToken" "$bot_token" > /dev/null
        log_success "Slack:BotToken configured"
    else
        log_warn "Slack:BotToken skipped"
    fi

    local signing_secret
    signing_secret=$(prompt_secret "Slack Signing Secret:" "Slack:SigningSecret" "$current_secret")

    if [ -n "$signing_secret" ]; then
        dotnet user-secrets set "Slack:SigningSecret" "$signing_secret" > /dev/null
        log_success "Slack:SigningSecret configured"
    else
        log_warn "Slack:SigningSecret skipped"
    fi

    echo ""
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
        --skip-hosts)
            SKIP_HOSTS=true
            shift
            ;;
        --skip-certs)
            SKIP_CERTS=true
            shift
            ;;
        --reset)
            RESET=true
            shift
            ;;
        --configure-slack)
            CONFIGURE_SLACK=true
            shift
            ;;
        --model)
            OLLAMA_MODEL="$2"
            shift 2
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
check_command "curl" || PREREQ_OK=false

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

# Configure hosts file
if [ "$SKIP_HOSTS" = false ]; then
    configure_hosts
    echo ""
fi

# Setup SSL certificates
if [ "$SKIP_CERTS" = false ]; then
    setup_certificates
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
HTTPS_PORT=$(get_https_port)
wait_for_url "https://traefik.knutr.local:${HTTPS_PORT}/api/overview" "Traefik" 30 || true
wait_for_url "https://grafana.knutr.local:${HTTPS_PORT}/api/health" "Grafana" 15 || true
wait_for_url "https://ollama.knutr.local:${HTTPS_PORT}/api/tags" "Ollama" 30 || true

if [ "$WITH_GITLAB" = true ]; then
    wait_for_gitlab || true
fi
echo ""

# Pull Ollama model
pull_ollama_model "$OLLAMA_MODEL" || true
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
log_info "Setting up user secrets..."
HOSTING_DIR="$ROOT_DIR/src/Knutr.Hosting"

cd "$HOSTING_DIR"

if ! dotnet user-secrets list > /dev/null 2>&1; then
    log_info "Initializing user secrets..."
    dotnet user-secrets init 2>/dev/null || true
fi

if [ "$CONFIGURE_SLACK" = true ]; then
    configure_slack_secrets "$HOSTING_DIR"
else
    SLACK_TOKEN=$(dotnet user-secrets list 2>/dev/null | grep "Slack:BotToken" | cut -d'=' -f2 | xargs || echo "")

    if [ -z "$SLACK_TOKEN" ] || [ "$SLACK_TOKEN" = "xoxb-dev-placeholder" ]; then
        log_warn "Slack credentials not configured."
        echo ""
        read -p "Would you like to configure Slack credentials now? [y/N]: " configure_now
        if [[ "$configure_now" =~ ^[Yy] ]]; then
            configure_slack_secrets "$HOSTING_DIR"
        else
            dotnet user-secrets set "Slack:BotToken" "xoxb-dev-placeholder" > /dev/null 2>&1 || true
            dotnet user-secrets set "Slack:SigningSecret" "dev-signing-secret" > /dev/null 2>&1 || true
            log_warn "Using placeholder values. Run with --configure-slack later to set real credentials."
        fi
    else
        log_success "Slack credentials already configured"
    fi
fi

if [ "$WITH_GITLAB" = true ]; then
    dotnet user-secrets set "GitLab:BaseUrl" "https://gitlab.knutr.local" > /dev/null 2>&1 || true

    GITLAB_TOKEN=$(dotnet user-secrets list 2>/dev/null | grep "GitLab:AccessToken" | cut -d'=' -f2 | xargs || echo "")
    if [ -z "$GITLAB_TOKEN" ] || [ "$GITLAB_TOKEN" = "dev-token-placeholder" ]; then
        dotnet user-secrets set "GitLab:AccessToken" "dev-token-placeholder" > /dev/null 2>&1 || true
        log_warn "GitLab token not configured. Set it after GitLab starts."
    fi
fi

echo ""

#-------------------------------------------------------------------------------
# Print summary
#-------------------------------------------------------------------------------
echo "======================================"
echo "  Setup Complete!"
echo "======================================"
echo ""
echo "Services available at:"
echo "  - https://traefik.knutr.local     Traefik Dashboard"
echo "  - https://grafana.knutr.local     Grafana (admin/admin)"
echo "  - https://prometheus.knutr.local  Prometheus"
echo "  - https://tempo.knutr.local       Tempo"
echo "  - https://ollama.knutr.local      Ollama API (model: $OLLAMA_MODEL)"
echo "  - https://ngrok.knutr.local       ngrok UI"

if [ "$WITH_GITLAB" = true ]; then
    echo "  - https://gitlab.knutr.local      GitLab CE"
    echo ""
    echo "GitLab Setup:"
    echo "  1. Wait for GitLab to fully start"
    echo "  2. Get root password: docker exec knutr-gitlab grep 'Password:' /etc/gitlab/initial_root_password"
    echo "  3. Login as 'root' with that password"
    echo "  4. Create a personal access token with 'api' scope"
    echo "  5. Update user secrets: dotnet user-secrets set \"GitLab:AccessToken\" \"your-token\""
    echo ""
    echo "GitLab Runner Registration:"
    echo "  1. In GitLab: Admin Area → CI/CD → Runners → New instance runner"
    echo "  2. Copy the registration token"
    echo "  3. Register the runner:"
    echo "     docker exec -it knutr-gitlab-runner gitlab-runner register \\"
    echo "       --url http://gitlab \\"
    echo "       --token YOUR_TOKEN \\"
    echo "       --executor docker \\"
    echo "       --docker-image alpine:latest \\"
    echo "       --docker-network-mode dev_knutr-network"
    echo ""
    echo "  4. Configure for inter-container communication:"
    echo "     docker exec knutr-gitlab-runner /scripts/configure-runner.sh"
fi

echo ""
echo "Secrets Management:"
echo "  - Configure Slack:  ./setup.sh --configure-slack"
echo "  - List secrets:     cd src/Knutr.Hosting && dotnet user-secrets list"
echo "  - Set a secret:     dotnet user-secrets set \"Key\" \"Value\""
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
