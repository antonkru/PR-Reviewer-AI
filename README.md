# PR-Reviewer-AI

> **Note: This is a Proof of Concept. This project is Azure cloud ready but has only been tested locally.**

An AI senior-developer "first pass" code review for pull requests, powered by an LLM via the Microsoft Agent Framework. Supports **Bitbucket Cloud** and **GitHub** in the same deployment.

When a PR is created or updated, the provider fires a webhook to this service. The service fetches the diff, sends it to OpenAI through a `ChatClientAgent` configured with a senior-reviewer system prompt, and posts the response back as a top-level comment on the PR. A hidden marker (`<!-- pr-reviewer-ai: sha=... -->`) is appended so re-deliveries for the same commit don't double-post.

## Out of scope for POC

- Inline comments (top-level Markdown summary only)
- Deep code analysis — the POC only looks at the PR diff; it does not fetch surrounding files, walk call graphs, or reason about repo-wide impact
- GitHub App authentication (PAT only — fine for POC; per-installation tokens are the production path)
- Persistent job queue / retry / DLQ (in-process Channel; restart loses pending jobs)
- Azure deployment

## Endpoints

| Method | Path                  | Purpose                                                                  |
|--------|-----------------------|--------------------------------------------------------------------------|
| `GET`  | `/`                   | Redirects to `/health`                                                   |
| `GET`  | `/health`             | Liveness probe — returns service name, status, and UTC timestamp         |
| `POST` | `/webhooks/bitbucket` | Bitbucket Cloud pull-request webhook receiver (signature-verified)       |
| `POST` | `/webhooks/github`    | GitHub pull-request webhook receiver (signature-verified)                |
| `POST` | `/reviews`            | Manual review trigger — enqueue a job by PR ref (Bearer-token auth)      |

See [Endpoint reference](#endpoint-reference) below for headers, payload schemas, and responses.

## Prerequisites

- .NET 10 SDK
- An OpenAI API key
- For Bitbucket: a Repository or Workspace Access Token (Bitbucket Cloud) with `pullrequest:write` scope. A Repository token is the narrowest choice; use a Workspace token if one service instance handles PRs across multiple repos.
- For GitHub: a Personal Access Token (fine-grained recommended) with **Pull requests: Read and write** and **Contents: Read** on the target repo(s) — the diff media type is gated on Contents, not Pull requests
- A way to expose `http://localhost:5279` to the provider — Visual Studio Dev Tunnels or ngrok

You only need to configure the provider(s) you actually use. Unconfigured webhooks will log a "secret not configured" warning and accept any signature until you set one.

## Configuration

Settings live under sections in `appsettings.json` (committed with empty placeholders):

```json
{
  "Api":       { "AccessToken": "" },
  "OpenAI":    { "ApiKey": "", "Model": "gpt-5-mini", "MaxDiffChars": 200000 },
  "Bitbucket": { "AccessToken": "", "WebhookSecret": "", "BaseAddress": "https://api.bitbucket.org/2.0/" },
  "GitHub":    { "AccessToken": "", "WebhookSecret": "", "UserAgent": "PR-Reviewer-AI", "BaseAddress": "https://api.github.com/" }
}
```

`Api:AccessToken` guards the manual `/reviews` endpoint (see [POST /reviews](#post-reviews)). `OpenAI:Model` accepts any chat-completions model the configured API key has access to.

Set real values via user-secrets, scoped to the Api project:

```powershell
dotnet user-secrets --project src/PrReviewer.Api init
dotnet user-secrets --project src/PrReviewer.Api set "OpenAI:ApiKey"            "sk-..."

# /reviews endpoint (required outside Development)
dotnet user-secrets --project src/PrReviewer.Api set "Api:AccessToken"          "<random hex>"

# Bitbucket (optional)
dotnet user-secrets --project src/PrReviewer.Api set "Bitbucket:AccessToken"    "ATCTT..."
dotnet user-secrets --project src/PrReviewer.Api set "Bitbucket:WebhookSecret"  "<random hex>"

# GitHub (optional)
dotnet user-secrets --project src/PrReviewer.Api set "GitHub:AccessToken"       "github_pat_..."
dotnet user-secrets --project src/PrReviewer.Api set "GitHub:WebhookSecret"     "<random hex>"
```

`GitHub:BaseAddress` defaults to `https://api.github.com/`; override it only for GitHub Enterprise.

If `Bitbucket:WebhookSecret` or `GitHub:WebhookSecret` is left empty, signature verification for that provider is **skipped** and a warning is logged the first time a webhook from that provider arrives. Acceptable while smoke-testing on a private dev tunnel; do not ship without it.

## Run locally

```powershell
dotnet build PR-Reviewer-AI.slnx
dotnet run --project src/PrReviewer.Api
```

The service listens on `http://localhost:5279` (see `src/PrReviewer.Api/Properties/launchSettings.json`).

`GET /` redirects (`302`) to `/health`. `GET /health` returns `{ "service": "PR-Reviewer-AI", "status": "ok", "timestamp": "..." }`.

## Expose to your source control provider via a dev tunnel

Visual Studio Dev Tunnels (ships with Visual Studio; available standalone as the `devtunnel` CLI):

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

## Endpoint reference

### `GET /`

Returns a `302` redirect to `/health`. Convenient as a default landing path for uptime monitors that follow redirects.

### `GET /health`

Liveness probe.

**Response `200 OK`**

```json
{
  "service":   "PR-Reviewer-AI",
  "status":    "ok",
  "timestamp": "2026-05-19T12:34:56.789+00:00"
}
```

### `POST /webhooks/bitbucket`

Receives pull-request events from Bitbucket Cloud and enqueues a review job.

**Headers**

| Header              | Required | Notes |
|---------------------|----------|-------|
| `X-Event-Key`       | yes      | Only `pullrequest:created` and `pullrequest:updated` are processed; any other key returns `204` and is ignored |
| `X-Hub-Signature`   | yes      | `sha256=<hex>` — HMAC-SHA256 of the raw request body keyed with `Bitbucket:WebhookSecret`. Verification is **skipped** (with a one-time warning on the first webhook) when the secret is empty |
| `X-Request-UUID`    | no       | Bitbucket's delivery id — logged, not validated |

**Body** — the standard Bitbucket pull-request payload. Required fields: `repository.workspace.slug`, `repository.name`, `pullrequest.id`, `pullrequest.source.commit.hash`.

**Responses**

| Status                       | When                                                              |
|------------------------------|-------------------------------------------------------------------|
| `204 No Content`             | Event handled and job enqueued, **or** event intentionally ignored |
| `400 Bad Request` (problem+json) | Malformed JSON or empty body                                   |
| `400 Bad Request` (validation problem) | Payload missing required fields                          |
| `401 Unauthorized`           | Invalid signature                                                 |

### `POST /webhooks/github`

Receives pull-request events from GitHub and enqueues a review job.

**Headers**

| Header                  | Required | Notes |
|-------------------------|----------|-------|
| `X-GitHub-Event`        | yes      | Only `pull_request` is processed; any other event returns `204` |
| `X-Hub-Signature-256`   | yes      | `sha256=<hex>` — HMAC-SHA256 of the raw body keyed with `GitHub:WebhookSecret`. Verification is **skipped** (with a one-time warning on the first webhook) when the secret is empty |
| `X-GitHub-Delivery`     | no       | GitHub's delivery id — logged, not validated |

**Body** — the standard GitHub `pull_request` payload. `action` must be `opened`, `synchronize`, or `reopened` (other actions return `204` and are ignored). Required fields: `repository.owner.login`, `repository.name`, `pull_request.number`, `pull_request.head.sha`.

**Responses**

| Status                       | When                                                              |
|------------------------------|-------------------------------------------------------------------|
| `204 No Content`             | Event handled and job enqueued, **or** event/action ignored        |
| `400 Bad Request`            | Malformed JSON                                                    |
| `400 Bad Request` (validation problem) | Payload missing required fields                          |
| `401 Unauthorized`           | Invalid signature                                                 |

### `POST /reviews`

Manually enqueue a review job. Useful for backfills, scripted tests, and re-running a review for a specific commit without going through a provider webhook.

**Authentication** — `Authorization: Bearer <Api:AccessToken>`, compared in constant time. In **Development** a missing `Api:AccessToken` allows unauthenticated requests (with a one-time warning); outside Development the host **fails at startup** if it isn't set. Configure it via user-secrets:

```powershell
dotnet user-secrets --project src/PrReviewer.Api set "Api:AccessToken" "<random hex>"
```

**Headers**

| Header          | Required | Notes |
|-----------------|----------|-------|
| `Content-Type`  | yes      | `application/json` |
| `Authorization` | yes\*    | `Bearer <token>` — \*optional in Development if `Api:AccessToken` is unset |

**Body**

```json
{
  "provider": "Bitbucket",
  "owner":    "my-workspace",
  "repo":     "my-repo",
  "prId":     42,
  "headSha":  "abc1234...",
  "title":    "Optional PR title for log lines"
}
```

| Field      | Type   | Required | Notes |
|------------|--------|----------|-------|
| `provider` | string | yes      | `Bitbucket` or `GitHub` (case-insensitive) |
| `owner`    | string | yes      | Workspace slug (Bitbucket) or owner login (GitHub) |
| `repo`     | string | yes      | Repository slug/name |
| `prId`     | int    | yes      | Must be greater than `0` |
| `headSha`  | string | no       | Omit to force a re-review of the current head (bypasses the dedup marker) |
| `title`    | string | no       | Echoed into log lines for readability |

**Responses**

| Status                         | When                                                              |
|--------------------------------|-------------------------------------------------------------------|
| `204 No Content`               | Job enqueued                                                      |
| `400 Bad Request` (problem+json) | Malformed JSON or empty body                                   |
| `400 Bad Request` (validation problem) | Missing/invalid fields                                   |
| `415 Unsupported Media Type`   | Wrong `Content-Type`                                              |
| `401 Unauthorized`             | Missing or invalid bearer token                                   |

**Example**

```powershell
curl -X POST http://localhost:5279/reviews `
  -H "Authorization: Bearer $env:PR_REVIEWER_TOKEN" `
  -H "Content-Type: application/json" `
  -d '{ "provider": "GitHub", "owner": "antonkru", "repo": "PR-Reviewer-AI", "prId": 7 }'
```

Omitting `headSha` (as above) forces a re-review of the current head; the posted comment will not include a dedup marker, so subsequent webhooks for that SHA will still be reviewed.

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

