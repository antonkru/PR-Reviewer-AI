# PR-Reviewer-AI

```
Status: This is a Proof of Concept. This project is Azure cloud ready but has only been tested locally.
```

A senior-developer "first pass" code review for pull requests, powered by an LLM via the Microsoft Agent Framework. Supports **Bitbucket Cloud** and **GitHub** in the same deployment.

When a PR is created or updated, the provider fires a webhook to this service. The service fetches the diff, sends it to OpenAI through a `ChatClientAgent` configured with a senior-reviewer system prompt, and posts the response back as a top-level comment on the PR. A hidden marker (`<!-- pr-reviewer-ai: sha=... -->`) is appended so re-deliveries for the same commit don't double-post.

## Solution layout

```
PR-Reviewer-AI.slnx
src/
  PrReviewer.Domain/      pure types + interfaces (no infra deps)
                          Models/ PullRequestRef, ReviewJob, Provider, ...
                          Abstractions/ ISourceControlClient, ISourceControlClientFactory, ...
  PrReviewer.Agents/      Microsoft Agent Framework integration + background worker
                          Prompts/SeniorDeveloperPrompt.md is the system prompt (embedded resource)
  PrReviewer.Api/         ASP.NET Core minimal API host
                          Bitbucket/      Bitbucket webhook DTOs + REST client + signature validator
                          GitHub/         GitHub webhook DTOs + REST client + signature validator
                          Webhooks/       shared HMAC-SHA256 verifier
                          Endpoints/      /webhooks/bitbucket, /webhooks/github
                          Infrastructure/ ChannelReviewQueue, SourceControlClientFactory
tests/
  PrReviewer.Tests/       xUnit + WireMock coverage for clients, validators, and the review pipeline
```

References: `Api -> Agents -> Domain`. Provider-specific code lives only in `Api`; `Agents` and `Domain` are provider-agnostic. The background worker dispatches per-job through `ISourceControlClientFactory.For(job.Pr.Provider)`.

## Prerequisites

- .NET 10 SDK
- An OpenAI API key
- For Bitbucket: a Bitbucket Cloud Repository (or Workspace) Access Token with `pullrequest:write` scope
- For GitHub: a Personal Access Token (fine-grained recommended) with **Pull requests: Read and write** and **Contents: Read** on the target repo(s) — the diff media type is gated on Contents, not Pull requests
- A way to expose `http://localhost:5279` to the provider — Visual Studio Dev Tunnels or ngrok

You only need to configure the provider(s) you actually use. Unconfigured webhooks will log a "secret not configured" warning and accept any signature until you set one.

## Configuration

Settings live under sections in `appsettings.json` (committed with empty placeholders):

```json
{
  "OpenAI":    { "ApiKey": "", "Model": "gpt-4o-mini", "MaxDiffChars": 200000 },
  "Bitbucket": { "AccessToken": "", "WebhookSecret": "" },
  "GitHub":    { "AccessToken": "", "WebhookSecret": "", "UserAgent": "PR-Reviewer-AI", "BaseAddress": "https://api.github.com/" }
}
```

Set real values via user-secrets, scoped to the Api project:

```powershell
dotnet user-secrets --project src/PrReviewer.Api init
dotnet user-secrets --project src/PrReviewer.Api set "OpenAI:ApiKey"            "sk-..."

# Bitbucket (optional)
dotnet user-secrets --project src/PrReviewer.Api set "Bitbucket:AccessToken"    "ATCTT..."
dotnet user-secrets --project src/PrReviewer.Api set "Bitbucket:WebhookSecret"  "<random hex>"

# GitHub (optional)
dotnet user-secrets --project src/PrReviewer.Api set "GitHub:AccessToken"       "github_pat_..."
dotnet user-secrets --project src/PrReviewer.Api set "GitHub:WebhookSecret"     "<random hex>"
```

`GitHub:BaseAddress` defaults to `https://api.github.com/`; override it only for GitHub Enterprise.

If `Bitbucket:WebhookSecret` or `GitHub:WebhookSecret` is left empty, signature verification for that provider is **skipped** with a startup warning. Acceptable while smoke-testing on a private dev tunnel; do not ship without it.

## Run locally

```powershell
dotnet build PR-Reviewer-AI.slnx
dotnet run --project src/PrReviewer.Api
```

The service listens on `http://localhost:5279` (see `src/PrReviewer.Api/Properties/launchSettings.json`).

`GET /` returns `{ "service": "PR-Reviewer-AI", "status": "ok" }` for a quick liveness check. `GET /health` includes a timestamp.

## Expose to your provider via a dev tunnel

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
2. **URL**: `<your-tunnel-url>/webhooks/bitbucket`
3. **Secret**: same value you stored in `Bitbucket:WebhookSecret` (Bitbucket sends `X-Hub-Signature: sha256=<hex>`)
4. **Triggers**: enable
   - Pull request -> Created
   - Pull request -> Updated

## Configure the GitHub webhook

In your GitHub repository:

1. **Settings** -> **Webhooks** -> **Add webhook**
2. **Payload URL**: `<your-tunnel-url>/webhooks/github`
3. **Content type**: `application/json`
4. **Secret**: same value you stored in `GitHub:WebhookSecret` (GitHub sends `X-Hub-Signature-256: sha256=<hex>`)
5. **Which events?** -> *Let me select individual events* -> tick **Pull requests** only

The service handles the `pull_request` event with action `opened`, `synchronize`, or `reopened`. All other events/actions are acknowledged with `204` and ignored.

Save, then open or update a PR. Within ~30 seconds an AI review comment should appear.

## Behavior reference

| Situation | HTTP response | Side effect |
|---|---|---|
| Handled event + action, signature valid | `204` | Job enqueued; review comment posted ~10-60s later |
| Anything else (push, issue, `closed` action, ...) | `204` | Ignored |
| Signature invalid | `401` | Nothing |
| Body malformed JSON or missing owner/repo/prId/sha | `400` | Nothing |
| Same head SHA already has a review marker on the PR | `204` (enqueued) | Background worker sees the marker and skips — no duplicate comment |
| OpenAI / provider API call fails inside the worker | (already 204'd) | Error logged, background loop continues |

## Editing the system prompt

`src/PrReviewer.Agents/Prompts/SeniorDeveloperPrompt.md` is the agent's instructions. Edit it, rebuild, restart — no C# changes needed. The file is shipped as an `<EmbeddedResource>` so the running service always has a self-contained copy.

## Tests

```powershell
dotnet test tests/PrReviewer.Tests/PrReviewer.Tests.csproj
```

Covers: HMAC signature validation per provider, BitbucketClient + GitHubClient REST behavior via WireMock, comment-marker dedup, and per-job provider dispatch in the background worker.

## Out of scope (POC)

- Inline comments (top-level Markdown summary only)
- GitHub App authentication (PAT only — fine for POC; per-installation tokens are the production path)
- Persistent job queue / retry / DLQ (in-process Channel; restart loses pending jobs)
- Azure deployment
