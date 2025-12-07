#!/bin/bash
#===============================================================================
# GitLab Runner Configuration Helper
#===============================================================================
# This script updates the GitLab Runner config.toml with settings needed for
# the Knutr development environment (inter-container communication).
#
# Run this AFTER registering the runner with:
#   docker exec knutr-gitlab-runner /scripts/configure-runner.sh
#===============================================================================

CONFIG_FILE="/etc/gitlab-runner/config.toml"

if [ ! -f "$CONFIG_FILE" ]; then
    echo "ERROR: Config file not found at $CONFIG_FILE"
    echo "Please register the runner first using:"
    echo "  docker exec -it knutr-gitlab-runner gitlab-runner register \\"
    echo "    --url http://gitlab \\"
    echo "    --token YOUR_TOKEN \\"
    echo "    --executor docker \\"
    echo "    --docker-image alpine:latest \\"
    echo "    --docker-network-mode knutr-network"
    exit 1
fi

echo "Updating GitLab Runner configuration..."

# Add clone_url if not present (for inter-container communication)
if ! grep -q "clone_url" "$CONFIG_FILE"; then
    # Insert clone_url after the [[runners]] section
    sed -i '/^\[\[runners\]\]/a\  clone_url = "http://gitlab"' "$CONFIG_FILE"
    echo "✓ Added clone_url = http://gitlab"
else
    echo "✓ clone_url already configured"
fi

# Ensure the runner uses the correct network for Docker-in-Docker
if ! grep -q 'network_mode = "knutr-network"' "$CONFIG_FILE" && ! grep -q 'network_mode = "dev_knutr-network"' "$CONFIG_FILE"; then
    # Add network_mode in the [runners.docker] section
    sed -i '/^\[runners\.docker\]/a\    network_mode = "dev_knutr-network"' "$CONFIG_FILE"
    echo "✓ Added network_mode = dev_knutr-network"
else
    echo "✓ network_mode already configured"
fi

# Set extra_hosts to resolve gitlab hostname
if ! grep -q 'extra_hosts' "$CONFIG_FILE"; then
    sed -i '/^\[runners\.docker\]/a\    extra_hosts = ["gitlab:host-gateway"]' "$CONFIG_FILE"
    echo "✓ Added extra_hosts for gitlab resolution"
else
    echo "✓ extra_hosts already configured"
fi

echo ""
echo "Configuration updated. Current config:"
echo "========================================"
cat "$CONFIG_FILE"
echo "========================================"
echo ""
echo "Restarting runner to apply changes..."
gitlab-runner restart

echo "Done!"
