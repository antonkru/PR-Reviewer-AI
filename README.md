# PR-Reviewer-AI

A senior-developer "first pass" code review for Bitbucket Cloud pull requests, powered by an LLM via the Microsoft Agent Framework.

When a PR is created or updated, Bitbucket fires a webhook to this service. The service fetches the diff, sends it to OpenAI through a `ChatClientAgent` configured with a senior-reviewer system prompt, and posts the response back as a top-level comment on the PR.

## Solution layout

```
PR-Reviewer-AI.slnx
src/
  PrReviewer.Domain/   pure types + interfaces (no infra deps)
  PrReviewer.Agents/   Microsoft Agent Framework integration + background worker
                       Prompts/SeniorDeveloperPrompt.md is the system prompt (embedded resource)
  PrReviewer.Api/      ASP.NET Core minimal API host
                       Bitbucket/  webhook receiver + REST client
                       Endpoints/  /webhooks/bitbucket
```

References: `Api -> Agents -> Domain`. Bitbucket-specific code lives only in `Api`; `Agents` is HTTP-agnostic.

## Prerequisites

- .NET 10 SDK
- An OpenAI API key
- A Bitbucket Cloud Repository (or Workspace) Access Token with `pullrequest:write` scope
- A way to expose `http://localhost:5279` to Bitbucket — Visual Studio Dev Tunnels or ngrok

## Configuration

Settings live under two sections in `appsettings.json` (committed with empty placeholders):

```json
{
  "OpenAI":    { "ApiKey": "", "Model": "gpt-4o-mini", "MaxDiffChars": 200000 },
  "Bitbucket": { "AccessToken": "", "WebhookSecret": "" }
}
```

Set real values via user-secrets, scoped to the Api project:

```powershell
dotnet user-secrets --project src/PrReviewer.Api init
dotnet user-secrets --project src/PrReviewer.Api set "OpenAI:ApiKey"            "sk-..."
dotnet user-secrets --project src/PrReviewer.Api set "Bitbucket:AccessToken"    "ATCTT..."
dotnet user-secrets --project src/PrReviewer.Api set "Bitbucket:WebhookSecret"  "<random hex>"
```

If `Bitbucket:WebhookSecret` is left empty, signature verification is **skipped** with a startup warning. Acceptable while smoke-testing on a private dev tunnel; do not ship without it.

## Run locally

```powershell
dotnet build PR-Reviewer-AI.slnx
dotnet run --project src/PrReviewer.Api
```

The service listens on `http://localhost:5279` (see `src/PrReviewer.Api/Properties/launchSettings.json`).

`GET /` returns `{ "service": "PR-Reviewer-AI", "status": "ok" }` for a quick liveness check.

## Expose to Bitbucket via a dev tunnel

Visual Studio Dev Tunnels (bundled with the .NET SDK):

```powershell
devtunnel user login
devtunnel host -p 5279 --allow-anonymous
```

Or ngrok:

```powershell
ngrok http 5279
```

Both will print a public HTTPS URL — use it as the base for the webhook below.

## Configure the Bitbucket webhook

In your Bitbucket Cloud repository:

1. **Repository settings** -> **Webhooks** -> **Add webhook**
2. **URL**: `<your-tunnel-url>/webhooks/bitbucket` e.g. ` https://travel-dotted-smilingly.ngrok-free.dev/webhooks/bitbucket`
3. **Secret**: same value you stored in `Bitbucket:WebhookSecret` (Bitbucket sends `X-Hub-Signature: sha256=<hex>`)
4. **Triggers**: enable
   - Pull request -> Created
   - Pull request -> Updated

Save, then open or update a PR. Within ~30 seconds an AI review comment should appear.

## Behavior reference

| Situation | HTTP response | Side effect |
|---|---|---|
| Event is `pullrequest:created` or `pullrequest:updated`, signature valid | `204` | Job enqueued; review comment posted ~10-60s later |
| Event is anything else (e.g. push, issue) | `204` | Ignored |
| Signature invalid | `401` | Nothing |
| Body malformed JSON or missing workspace/repo/prId | `400` | Nothing |
| OpenAI / Bitbucket call fails inside the worker | (already 204'd) | Error logged, background loop continues |

## Editing the system prompt

`src/PrReviewer.Agents/Prompts/SeniorDeveloperPrompt.md` is the agent's instructions. Edit it, rebuild, restart — no C# changes needed. The file is shipped as an `<EmbeddedResource>` so the running service always has a self-contained copy.

## Out of scope (POC)

- Inline comments (top-level Markdown summary only)
- Persistence / dedup (webhook retries can double-post)
- Provider abstraction (Bitbucket Cloud only)
- Tests (manual verification via a real PR)
- Azure deployment
