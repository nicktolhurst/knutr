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

## Getting Started

See [deploy/k8s/README.md](deploy/k8s/README.md) for the full setup guide (fresh WSL install through to running cluster).

## Project Structure

```
src/
  Knutr.Abstractions/    Shared interfaces and contracts
  Knutr.Core/            Command routing, orchestration, NL engine
  Knutr.Hosting/         ASP.NET host, DI wiring, Program.cs
  Knutr.Adapters.Slack/  Slack API adapter
  Knutr.Infrastructure/  LLM client, prompts
  Knutr.Sdk/             Plugin service SDK (shared types)
  Knutr.Sdk.Hosting/     Plugin service host helpers
  Knutr.Plugins.PingPong/  Built-in ping/pong plugin
  Knutr.Plugins.Joke/      Example plugin service (separate pod)
tools/
  Knutr.Testbed/         CLI that simulates Slack for local testing
deploy/
  k8s/                   Kubernetes manifests (Kustomize)
```
