# Knutr

A plugin-first Slack bot that runs on Kubernetes. Core bot handles Slack ingress/egress, command routing, and NL fallback via Ollama. Plugin services are separate pods discovered at startup over HTTP.

## Architecture

```
Slack ──webhook──► knutr-core ──HTTP──► knutr-plugin-joke
                   │                    knutr-plugin-...
                   ├── Command router
                   ├── NL engine (Ollama)
                   └── Plugin service discovery (K8s DNS)
```

- **Core bot** — always running, handles Slack events/commands, routes to plugins or NL fallback
- **Plugin services** — independent pods, expose `/manifest` and `/execute` endpoints
- **Testbed CLI** — simulates Slack locally, no workspace needed

## Quick Start

### Prerequisites

Docker, .NET 9 SDK, kubectl, kind. See [deploy/k8s/README.md](deploy/k8s/README.md) for a full install guide from a fresh WSL environment.

### Setup

```bash
git clone git@github.com:nicktolhurst/knutr.git
cd knutr
source ./init
```

`source ./init` adds the repo root to your `PATH` so the `knutr` CLI is available for the session.

### Create cluster and deploy

```bash
# Create a Kind cluster (see deploy/k8s/README.md for the kind config)
kind create cluster --name knutr

# Build all services and load into kind
knutr build --all

# Deploy everything
knutr apply
```

### Verify

```bash
knutr status
```

## Developer CLI

The `knutr` command wraps common development workflows into a single tool.

### `knutr watch [lines]`

Tail colorized, interleaved logs from all knutr services (excludes ollama). Logs use a pastel color scheme with per-service coloring and Serilog-style template value highlighting.

```bash
knutr watch        # tail last 100 lines
knutr watch 500    # tail last 500 lines
```

### `knutr testbed [args...]`

Launch an interactive CLI that simulates Slack. Sends slash commands and messages to the core bot and captures `response_url` callbacks. Auto-detects the Kind cluster gateway for callback routing.

```bash
knutr testbed
```

At the prompt:

```
knutr> /ping                    # slash command
knutr> /joke                    # slash command to plugin
knutr> hello knutr              # message event (triggers scan + NL)
knutr> !health                  # check /health endpoint
knutr> !manifest <url>          # fetch a plugin manifest
knutr> !clear                   # clear screen
knutr> !exit                    # quit
```

### `knutr build <target|--all>`

Build a Docker image, load it into the Kind cluster, and restart the deployment — all in one step.

```bash
knutr build core           # rebuild core bot
knutr build joke           # rebuild joke plugin
knutr build pingpong       # rebuild pingpong plugin
knutr build jargonbuster   # rebuild jargonbuster plugin
knutr build --all          # rebuild everything
```

### `knutr apply`

Apply all Kubernetes manifests via Kustomize.

```bash
knutr apply
```

### `knutr destroy`

Tear down deployments and services. PVCs (ollama model storage) are preserved.

```bash
knutr destroy
```

### `knutr status`

Show pod status across the cluster with colorized output.

### `knutr plugins list`

List all registered plugin pods and their status.

### `knutr plugins show <name>`

Show detailed info for a plugin including its manifest, slash commands, subcommands, and scan mode.

```bash
knutr plugins show joke
```

## Project Structure

```
knutr                          Developer CLI (bash)
init                           Source this to add knutr to PATH
src/
  Knutr.Abstractions/         Shared interfaces and contracts
  Knutr.Core/                 Command routing, orchestration, NL engine
  Knutr.Hosting/              ASP.NET host, DI wiring, Program.cs
  Knutr.Adapters.Slack/       Slack API adapter
  Knutr.Infrastructure/       LLM client, prompts
  Knutr.Sdk/                  Plugin service SDK (shared types)
  Knutr.Sdk.Hosting/          Plugin service host helpers, logging
  Knutr.Plugins.PingPong/     Ping/pong plugin service
  Knutr.Plugins.Joke/         Joke plugin service
  Knutr.Plugins.JargonBuster/ TLA scanner plugin service
tools/
  Knutr.Testbed/              CLI that simulates Slack for local testing
deploy/
  k8s/                        Kubernetes manifests (Kustomize)
```
