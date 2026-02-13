# Knutr on Kubernetes - Local Setup & Testing

This guide walks you through deploying Knutr to a local Kubernetes cluster and testing it without a real Slack workspace.

## Prerequisites

Install these tools:

| Tool | Purpose | Install |
|------|---------|---------|
| **Docker** | Container runtime | [docker.com](https://docs.docker.com/get-docker/) |
| **kind** | Local K8s cluster | `brew install kind` or `choco install kind` |
| **kubectl** | K8s CLI | `brew install kubectl` or `choco install kubernetes-cli` |
| **.NET 9 SDK** | Build the app | [dot.net](https://dotnet.microsoft.com/download/dotnet/9.0) |

> **Alternative to kind:** You can use k3d, minikube, or Docker Desktop's built-in Kubernetes. The steps below use kind but adapt easily. kind is recommended because it's lightweight, fast, and works well for loading local Docker images.

## 1. Create a Local Cluster

```bash
# Create a kind cluster with port mapping so you can reach the bot from your host
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

Verify:

```bash
kubectl cluster-info --context kind-knutr
kubectl get nodes
```

## 2. Build the Docker Image

From the repo root:

```bash
docker build -t knutr-core:latest .
```

Load it into the kind cluster (kind doesn't use your local Docker registry):

```bash
kind load docker-image knutr-core:latest --name knutr
```

## 3. Deploy to Kubernetes

### 3a. Apply the base manifests

```bash
kubectl apply -k deploy/k8s/base
```

This creates:
- `knutr` namespace
- `knutr-core` deployment, service, serviceaccount
- ConfigMap with default settings
- Secret with placeholder values

### 3b. Expose the service to your host

The kind cluster has a port mapping on 30071. Create a NodePort service to bridge it:

```bash
kubectl apply -f - <<EOF
apiVersion: v1
kind: Service
metadata:
  name: knutr-core-nodeport
  namespace: knutr
spec:
  type: NodePort
  selector:
    app.kubernetes.io/name: knutr-core
  ports:
    - port: 80
      targetPort: 7071
      nodePort: 30071
EOF
```

Now `http://localhost:7071` on your machine reaches the knutr pod.

### 3c. Verify the pod is running

```bash
# Watch the pod come up
kubectl -n knutr get pods -w

# Check logs
kubectl -n knutr logs -l app.kubernetes.io/name=knutr-core -f

# Test health endpoint
curl http://localhost:7071/health
```

Expected output: `{"status":"ok"}`

## 4. Test with the Testbed CLI

The testbed is a CLI tool that simulates Slack - it sends slash commands and messages to the bot and captures response callbacks. No Slack workspace needed.

### 4a. Build and run

```bash
# From the repo root
dotnet run --project tools/Knutr.Testbed
```

You'll see:

```
  knutr-testbed

  Target:       http://localhost:7071
  Callback:     http://localhost:9876
  User:         U_TESTUSER
  Channel:      C_TESTCHANNEL
  ──────────────────────────────────────────────────────

knutr>
```

### 4b. Send a slash command

```
knutr> /ping
  ► POST /slack/commands  command=/ping  text=""
  ← 200 OK
  (Watching for response_url callback... replies appear below)

  ◄ CALLBACK [POST /response]
    text: pong
    response_type: in_channel
```

### 4c. Send a /knutr subcommand

```
knutr> /knutr claimed
  ► POST /slack/commands  command=/knutr  text="claimed"
  ← 200 OK

  ◄ CALLBACK [POST /response]
    text: No environments are currently claimed.
```

### 4d. Send a message event

```
knutr> ping
  ► POST /slack/events  text="ping"
  ← 200 OK
```

> **Note:** Message responses go via `chat.postMessage` (not response_url), so they'll show in the knutr logs rather than the callback listener. With no Slack BotToken configured, the bot logs the reply to stdout instead.

### 4e. Check a plugin service manifest

```
knutr> !manifest localhost:5100
  ► GET http://localhost:5100/manifest
  ← 200 OK
  ◄ { "name": "ChannelExport", "subcommands": [...] }
```

### 4f. Health check

```
knutr> !health
  ► GET http://localhost:7071/health
  ← 200 OK
  ◄ {"status":"ok"}
```

### CLI options

```bash
# Point to a different URL (e.g. port-forwarded pod)
dotnet run --project tools/Knutr.Testbed -- --url http://localhost:8080

# Change the callback listener port
dotnet run --project tools/Knutr.Testbed -- --callback-port 9999
```

## 5. Deploying a Plugin Service

Plugin services are separate pods that the core discovers at startup.

### 5a. Build a plugin service

A plugin service is a tiny ASP.NET app. Here's the minimal pattern:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKnutrPluginService<MyHandler>();
var app = builder.Build();
app.MapKnutrPluginEndpoints(); // maps /health, /manifest, /execute
app.Run("http://0.0.0.0:8080");
```

Where `MyHandler` implements `IPluginHandler`:

```csharp
public class MyHandler : IPluginHandler
{
    public PluginManifest GetManifest() => new()
    {
        Name = "PostMortem",
        Version = "1.0.0",
        Subcommands = [new() { Name = "post-mortem", Description = "Run a post-mortem" }]
    };

    public async Task<PluginExecuteResponse> ExecuteAsync(
        PluginExecuteRequest request, CancellationToken ct)
    {
        // Your logic here
        return PluginExecuteResponse.Ok("Here's your post-mortem...");
    }
}
```

### 5b. Deploy to K8s

Copy and customise the template:

```bash
# Copy the template
cp deploy/k8s/base/plugin-template.yaml deploy/k8s/base/plugin-postmortem.yaml

# Replace PLUGIN_NAME with your plugin name
sed -i 's/PLUGIN_NAME/postmortem/g' deploy/k8s/base/plugin-postmortem.yaml

# Build and load the image
docker build -f Dockerfile.plugin \
  --build-arg PLUGIN_PROJECT=MyPlugin/MyPlugin.csproj \
  -t knutr-plugin-postmortem:latest .
kind load docker-image knutr-plugin-postmortem:latest --name knutr

# Deploy
kubectl apply -f deploy/k8s/base/plugin-postmortem.yaml
```

### 5c. Tell the core about the plugin

Update the ConfigMap to include the new service name:

```bash
kubectl -n knutr edit configmap knutr-core-config
```

In `appsettings.json`, add to the `PluginServices.Services` array:

```json
{
  "PluginServices": {
    "Services": ["postmortem"],
    "Namespace": "knutr"
  }
}
```

Then restart the core to pick up the change:

```bash
kubectl -n knutr rollout restart deployment knutr-core
```

The core will discover the plugin at startup via `http://knutr-plugin-postmortem.knutr.svc.cluster.local/manifest`.

### 5d. Test the plugin

```
knutr> /knutr post-mortem this-channel
  ► POST /slack/commands  command=/knutr  text="post-mortem this-channel"
  ← 200 OK

  ◄ CALLBACK [POST /response]
    text: Here's your post-mortem...
```

## 6. Service Chaining

Plugin services can call other plugin services using `IPluginServiceClient`:

```csharp
public class PostMortemHandler(IPluginServiceClient services) : IPluginHandler
{
    public async Task<PluginExecuteResponse> ExecuteAsync(
        PluginExecuteRequest request, CancellationToken ct)
    {
        // First, call channel-export to get channel data
        var exportResult = await services.CallAsync("channel-export", new PluginExecuteRequest
        {
            Command = "channel-export",
            Subcommand = "export",
            ChannelId = request.ChannelId,
            UserId = request.UserId,
        }, ct);

        if (!exportResult.Success)
            return PluginExecuteResponse.Fail($"Export failed: {exportResult.Error}");

        // Then process the data
        var analysis = $"Post-mortem for channel based on export:\n{exportResult.Text}";
        return PluginExecuteResponse.Ok(analysis, markdown: true);
    }

    // ... GetManifest() ...
}
```

The `IPluginServiceClient` resolves `"channel-export"` to `http://knutr-plugin-channel-export.knutr.svc.cluster.local` using K8s DNS. For local development outside K8s, you can override with config:

```json
{
  "PluginServices": {
    "Endpoints": {
      "channel-export": "http://localhost:5100",
      "postmortem": "http://localhost:5200"
    }
  }
}
```

## 7. Local Development (Without K8s)

You don't need K8s for day-to-day development. Run everything locally:

```bash
# Terminal 1: Run the core bot
dotnet run --project src/Knutr.Hosting

# Terminal 2: Run the testbed
dotnet run --project tools/Knutr.Testbed

# Terminal 3 (optional): Run a plugin service
dotnet run --project src/MyPluginService -- --urls http://localhost:5100
```

Configure the core to find local plugin services via `appsettings.Development.json`:

```json
{
  "PluginServices": {
    "Services": ["channel-export", "postmortem"],
    "Endpoints": {
      "channel-export": "http://localhost:5100",
      "postmortem": "http://localhost:5200"
    }
  }
}
```

## 8. Useful Commands

```bash
# View all knutr resources
kubectl -n knutr get all

# Stream core logs
kubectl -n knutr logs -l app.kubernetes.io/name=knutr-core -f

# Stream a plugin service's logs
kubectl -n knutr logs -l app.kubernetes.io/name=knutr-plugin-postmortem -f

# Port-forward to a specific pod (bypass NodePort)
kubectl -n knutr port-forward svc/knutr-core 7071:80

# Restart after config change
kubectl -n knutr rollout restart deployment knutr-core

# Check plugin service discovery
kubectl -n knutr logs -l app.kubernetes.io/name=knutr-core | grep -i "plugin service"

# Delete everything
kubectl delete namespace knutr
kind delete cluster --name knutr
```

## 9. Connecting Real Slack (Production)

When you're ready to connect a real Slack workspace:

1. Create a Slack app using the `manifest.yml` at the repo root
2. Update the K8s secret with real credentials:

```bash
kubectl -n knutr create secret generic knutr-core-secrets \
  --from-literal=Slack__BotToken=xoxb-your-token \
  --from-literal=Slack__SigningSecret=your-signing-secret \
  --from-literal=Slack__BotUserId=U_YOUR_BOT_USER_ID \
  --from-literal=LLM__ApiKey=sk-your-key \
  --dry-run=client -o yaml | kubectl apply -f -
```

3. Set up an Ingress or LoadBalancer to expose the core bot to Slack's webhook URLs
4. Update the Slack app's event/command/interactivity URLs to point to your Ingress

## Architecture Recap

```
                    ┌─────────────────────────────────────────────────┐
   Slack / CLI      │              knutr namespace                     │
   Testbed          │                                                  │
       │            │  ┌─────────────────┐                            │
       │──webhook──►│  │   knutr-core    │     HTTP                   │
       │            │  │                 ├────► knutr-plugin-export   │
       │◄─callback──│  │  Slack adapter  │     HTTP                   │
                    │  │  Orchestrator   ├────► knutr-plugin-mortem   │
                    │  │  NL engine      │     HTTP                   │
                    │  │  Observability  ├────► knutr-plugin-...      │
                    │  └─────────────────┘                            │
                    └─────────────────────────────────────────────────┘
```

- **Core bot** always runs, handles Slack ingress/egress, routing, NL fallback
- **Plugin services** are separate pods, discovered at startup via `/manifest`
- **Services communicate** over HTTP using K8s internal DNS
- **Testbed CLI** replaces Slack for local testing
