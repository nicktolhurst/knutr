# Knutr (fresh start)

A minimal, plugin-first Slack bot host with clean logging, rich metrics/traces, and a tiny sample plugin.

## Quickstart

1. Start the dev stack (Prometheus, Tempo, Grafana, Ollama LLM, ngrok):
   ```bash
   cd dev
   cp .env.example .env   # add NGROK_AUTHTOKEN if you have one
   docker compose up -d
   ```

2. In another terminal, run the host:
   ```bash
   dotnet build src/Knutr.Hosting
   dotnet run --project src/Knutr.Hosting
   ```

3. Copy your ngrok HTTPS URL and set it as the Slack app request URL(s):
   - Event Subscriptions: `{NGROK_HTTPS}/slack/events`
   - Slash Commands (for `/ping`): `{NGROK_HTTPS}/slack/commands`

4. Configure `appsettings.Development.json`:
   - Set `Slack:BotToken` to your bot token (starts with `xoxb-...`)
   - Set `Slack:SigningSecret` (if you enable signature validation)

5. Try it in Slack:
   - Type `/ping` → the bot replies “pong” via `response_url`
   - Post `ping` in a channel or DM → the bot replies “pong” in thread

## Metrics & Traces
- Prometheus scrape: `http://localhost:9090`
- Grafana: `http://localhost:3000` (admin/admin)
- Tempo: `http://localhost:3200` (used by Grafana)
- Bot metrics endpoint: `http://localhost:7071/metrics`

See `docs/architecture.md` for the architecture and design notes.
