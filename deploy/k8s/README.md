# Knutr - Local Development Guide

Getting Knutr running on a local Kind cluster from a fresh WSL install.

## Prerequisites

### 1. Generate an SSH key and add it to GitHub

```bash
ssh-keygen -t ed25519 -C "your-email@example.com"
eval "$(ssh-agent -s)"
ssh-add ~/.ssh/id_ed25519
cat ~/.ssh/id_ed25519.pub
```

Copy the output and add it at https://github.com/settings/ssh/new.

Test the connection:

```bash
ssh -T git@github.com
```

### 2. Install tools

```bash
sudo apt update && sudo apt upgrade -y

# Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
newgrp docker

# .NET 9 SDK
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
source ~/.bashrc

# kubectl
curl -LO "https://dl.k8s.io/release/$(curl -Ls https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl"
chmod +x kubectl && sudo mv kubectl /usr/local/bin/

# kind
curl -Lo kind https://kind.sigs.k8s.io/dl/latest/kind-linux-amd64
chmod +x kind && sudo mv kind /usr/local/bin/
```

## Clone

```bash
git clone git@github.com:nicktolhurst/knutr.git
cd knutr
```

## Create the Kind Cluster

```bash
cat <<EOF | kind create cluster --name knutr --config=-
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
nodes:
- role: control-plane
  extraPortMappings:
  - containerPort: 30071
    hostPort: 7071
    protocol: TCP
EOF
```

## Build and Load Images

```bash
# Core bot
docker build -t knutr-core:latest .

# Joke plugin service
docker build -f src/Knutr.Plugins.Joke/Dockerfile -t knutr-plugin-joke:latest .

# Load into kind (kind doesn't use your local Docker registry)
kind load docker-image knutr-core:latest --name knutr
kind load docker-image knutr-plugin-joke:latest --name knutr
```

## Deploy

```bash
kubectl apply -k deploy/k8s/base
```

This creates the `knutr` namespace with:
- `knutr-core` — the bot (deployment + NodePort service on 30071)
- `knutr-plugin-joke` — the joke plugin service
- `ollama` — local LLM inference server
- ConfigMap, Secret, ServiceAccount

## Verify

```bash
# Check all resources
kubectl -n knutr get all

# Watch pods come up
kubectl -n knutr get pods -w

# Test health endpoint (once pods are ready)
curl http://localhost:7071/health
```

Expected: `{"status":"ok"}`

## Watch Logs

```bash
# Core bot logs
kubectl -n knutr logs -l app.kubernetes.io/name=knutr-core -f

# Joke plugin logs
kubectl -n knutr logs -l app.kubernetes.io/name=knutr-plugin-joke -f

# All pods in the namespace
kubectl -n knutr logs -l app.kubernetes.io/part-of=knutr -f --prefix
```

## Test with the Testbed CLI

The testbed simulates Slack — it sends slash commands and messages to the bot without needing a real Slack workspace.

```bash
dotnet run --project tools/Knutr.Testbed
```

At the prompt:

```
knutr> /ping
knutr> /knutr joke
knutr> !health
```

### Testbed options

```bash
# Custom URL (e.g. port-forwarded pod)
dotnet run --project tools/Knutr.Testbed -- --url http://localhost:8080

# Custom callback port
dotnet run --project tools/Knutr.Testbed -- --callback-port 9999
```

## Useful Commands

```bash
# Restart after a config change
kubectl -n knutr rollout restart deployment knutr-core

# Port-forward to a specific pod (bypass NodePort)
kubectl -n knutr port-forward svc/knutr-core 7071:80

# Describe a pod (troubleshoot startup issues)
kubectl -n knutr describe pod -l app.kubernetes.io/name=knutr-core

# Check plugin service discovery
kubectl -n knutr logs -l app.kubernetes.io/name=knutr-core | grep -i "plugin"

# Exec into a pod
kubectl -n knutr exec -it deploy/knutr-core -- /bin/sh

# View all knutr resources
kubectl -n knutr get all
```

## Rebuild and Redeploy

After making code changes:

```bash
# Rebuild
docker build -t knutr-core:latest .

# Reload into kind
kind load docker-image knutr-core:latest --name knutr

# Restart the deployment to pick up the new image
kubectl -n knutr rollout restart deployment knutr-core
```

## Tear Down

```bash
kubectl delete namespace knutr
kind delete cluster --name knutr
```
