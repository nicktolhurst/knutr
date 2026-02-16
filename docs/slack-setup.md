# Slack Setup Guide

Connect Knutr to a real Slack workspace. This guide assumes you already have the Kind cluster running and services deployed (see [deploy/k8s/README.md](../deploy/k8s/README.md)).

## 1. Expose your local cluster

Slack sends webhooks over HTTPS, so your local `localhost:7071` needs a public URL. Use [ngrok](https://ngrok.com) or any tunnel of your choice.

```bash
ngrok http 7071
```

Note the forwarding URL (e.g. `https://abc-123-xyz.ngrok-free.app`). Keep this running.

> **Tip:** On the free tier, ngrok generates a new URL each session. Use a [custom domain](https://ngrok.com/docs/guides/how-to-set-up-a-custom-domain/) or [cloudflared tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/) for a stable URL.

## 2. Create the Slack App

1. Go to [api.slack.com/apps](https://api.slack.com/apps)
2. Click **Create New App** > **From a manifest**
3. Select your workspace
4. Switch the format to **YAML**
5. Open [`manifest.yml`](../manifest.yml) from this repo, replace all three URLs with your ngrok domain, and paste it in:

```
https://YOUR-NGROK-URL/slack/events
https://YOUR-NGROK-URL/slack/commands
https://YOUR-NGROK-URL/slack/interactivity
```

6. Click **Create**

### What the manifest configures

| Setting | Value |
|---|---|
| Bot display name | Knutr |
| Slash commands | `/ping`, `/joke`, `/knutr` |
| Event subscriptions | `message.channels`, `message.groups`, `message.im`, `reaction_added` |
| Interactivity | Enabled (for button actions) |
| Socket mode | Disabled (uses HTTP webhooks) |

### OAuth scopes

The manifest requests these bot token scopes:

| Scope | Purpose |
|---|---|
| `channels:history` | Read messages in public channels |
| `channels:read` | View public channel info |
| `chat:write` | Send messages |
| `commands` | Register slash commands |
| `groups:history` | Read messages in private channels |
| `groups:read` | View private channel info |
| `im:history` | Read direct messages |
| `im:write` | Send direct messages |
| `reactions:read` | View emoji reactions |
| `reactions:write` | Add emoji reactions |
| `users:read` | View user info |

## 3. Install to your workspace

After creating the app, click **Install to Workspace** and authorize it.

## 4. Collect your credentials

You need three values from the Slack app settings:

| Credential | Where to find it |
|---|---|
| **Bot Token** (`xoxb-...`) | OAuth & Permissions > Bot User OAuth Token |
| **Signing Secret** | Basic Information > App Credentials > Signing Secret |
| **Bot User ID** (`U...`) | Run the command below |

To get the Bot User ID:

```bash
curl -s -H "Authorization: Bearer xoxb-YOUR-TOKEN" \
  https://slack.com/api/auth.test | jq -r .user_id
```

## 5. Configure secrets

Copy the example secret file and fill in your values:

```bash
cp deploy/k8s/base/core-secret.example.yaml deploy/k8s/base/core-secret.yaml
```

Edit `deploy/k8s/base/core-secret.yaml`:

```yaml
stringData:
  Slack__BotToken: "xoxb-your-actual-token"
  Slack__SigningSecret: "your-actual-signing-secret"
  Slack__BotUserId: "U0YOUR-BOT-ID"
  LLM__ApiKey: "REPLACE-ME"  # not needed for Ollama, leave as-is
```

> **Note:** `core-secret.yaml` is gitignored. Never commit real credentials.

## 6. Deploy

```bash
knutr apply
```

Or if this is a fresh deploy:

```bash
knutr build --all
knutr apply
```

Verify everything is running:

```bash
knutr status
curl http://localhost:7071/health
```

Expected: `{"status":"ok"}`

## 7. Test the connection

1. Invite the bot to a channel: `/invite @Knutr`
2. Try a slash command: `/ping`
3. Mention the bot: `@Knutr hello`
4. Watch the logs: `knutr watch`

## Troubleshooting

### Slack shows "dispatch_failed" on slash commands

- Check that ngrok is running and the URL matches what's in your Slack app config
- Verify the health endpoint responds: `curl http://localhost:7071/health`
- Check the core pod is running: `knutr status`

### Bot doesn't respond to messages

- Make sure the bot is invited to the channel
- Check Event Subscriptions is verified (green checkmark) in Slack app settings
- Look at the logs for ingress events: `knutr watch`

### "Invalid signing secret" errors

- Ensure `Slack__SigningSecret` in your secret matches the value from Basic Information > App Credentials
- Signature validation is disabled in development by default (`Slack__EnableSignatureValidation: false` in appsettings.Development.json). For production, set it to `true`.

### Plugin commands not working (e.g. `/knutr sentinel status`)

- Plugin services need to be discovered at startup. Check the logs for `Discovered plugin service` messages
- Verify plugin pods are running: `knutr plugins list`
- If plugins started after core, restart core: `kubectl -n knutr rollout restart deployment knutr-core`

### Updating the ngrok URL

If your ngrok URL changes, update all three URLs in your Slack app settings:

1. **Event Subscriptions** > Request URL
2. **Slash Commands** > each command's Request URL
3. **Interactivity & Shortcuts** > Request URL

Slack will re-verify the Event Subscriptions URL automatically.
