using System.Text.Json;
using PrReviewer.Api.GitHub;
using PrReviewer.Domain.Abstractions;
using PrReviewer.Domain.Models;

namespace PrReviewer.Api.Endpoints;

public static class GitHubWebhookEndpoint
{
    private const string HandledEvent = "pull_request";

    private static readonly HashSet<string> HandledActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "opened",
        "synchronize",
        "reopened",
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapGitHubWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/github", HandleAsync);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        GitHubWebhookSignatureValidator validator,
        IReviewQueue queue,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("GitHubWebhook");

        var eventName = context.Request.Headers["X-GitHub-Event"].ToString();
        var deliveryId = context.Request.Headers["X-GitHub-Delivery"].ToString();

        if (!string.Equals(eventName, HandledEvent, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Webhook received Provider=GitHub DeliveryId={DeliveryId} Event={Event} Outcome=Ignored",
                deliveryId, eventName);
            return Results.NoContent();
        }

        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms, ct);
        var rawBody = ms.ToArray();

        var signature = context.Request.Headers["X-Hub-Signature-256"].ToString();
        if (!validator.Validate(rawBody, signature))
        {
            logger.LogWarning(
                "Webhook rejected Provider=GitHub DeliveryId={DeliveryId} Event={Event} Outcome=BadSignature",
                deliveryId, eventName);
            return Results.Unauthorized();
        }

        GitHubWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GitHubWebhookPayload>(rawBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Webhook rejected Provider=GitHub DeliveryId={DeliveryId} Event={Event} Outcome=MalformedJson",
                deliveryId, eventName);
            return Results.BadRequest();
        }

        var action = payload?.Action;
        if (action is null || !HandledActions.Contains(action))
        {
            logger.LogInformation(
                "Webhook received Provider=GitHub DeliveryId={DeliveryId} Event={Event} Action={Action} Outcome=Ignored",
                deliveryId, eventName, action);
            return Results.NoContent();
        }

        var owner = payload?.Repository?.Owner?.Login;
        var repo = payload?.Repository?.Name;
        var prId = payload?.PullRequest?.Number ?? 0;
        var headSha = payload?.PullRequest?.Head?.Sha;

        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo) || prId <= 0)
        {
            logger.LogWarning(
                "Webhook rejected Provider=GitHub DeliveryId={DeliveryId} Event={Event} Outcome=MissingRefs " +
                "(owner={Owner} repo={Repo} prId={PrId})",
                deliveryId, eventName, owner, repo, prId);
            return Results.BadRequest();
        }

        if (string.IsNullOrEmpty(headSha))
        {
            logger.LogWarning(
                "Webhook rejected Provider=GitHub DeliveryId={DeliveryId} Event={Event} Outcome=MissingSha " +
                "for {Owner}/{Repo}#{PrId} — cannot dedup",
                deliveryId, eventName, owner, repo, prId);
            return Results.BadRequest();
        }

        var prRef = new PullRequestRef(owner, repo, prId, payload?.PullRequest?.Title, Provider.GitHub);
        await queue.WriteAsync(new ReviewJob(prRef, headSha, DateTimeOffset.UtcNow), ct);

        logger.LogInformation(
            "Webhook enqueued Provider=GitHub DeliveryId={DeliveryId} Event={Event} Action={Action} Outcome=Enqueued " +
            "{Owner}/{Repo}#{PrId} sha={Sha} ({Title})",
            deliveryId, eventName, action, owner, repo, prId, headSha, prRef.Title);

        return Results.NoContent();
    }
}
