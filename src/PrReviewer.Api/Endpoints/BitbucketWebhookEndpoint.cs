using System.Text.Json;
using PrReviewer.Api.Bitbucket;
using PrReviewer.Domain.Abstractions;
using PrReviewer.Domain.Models;

namespace PrReviewer.Api.Endpoints;

public static class BitbucketWebhookEndpoint
{
    private static readonly HashSet<string> HandledEventKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "pullrequest:created",
        "pullrequest:updated",
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapBitbucketWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/bitbucket", HandleAsync);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        BitbucketWebhookSignatureValidator validator,
        IReviewQueue queue,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("BitbucketWebhook");

        var eventKey = context.Request.Headers["X-Event-Key"].ToString();
        var deliveryId = context.Request.Headers["X-Request-UUID"].ToString();

        if (!HandledEventKeys.Contains(eventKey))
        {
            logger.LogInformation(
                "Webhook received DeliveryId={DeliveryId} EventKey={EventKey} Outcome=Ignored",
                deliveryId, eventKey);
            return Results.NoContent();
        }

        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms, ct);
        var rawBody = ms.ToArray();

        var signature = context.Request.Headers["X-Hub-Signature"].ToString();
        if (!validator.Validate(rawBody, signature))
        {
            logger.LogWarning(
                "Webhook rejected DeliveryId={DeliveryId} EventKey={EventKey} Outcome=BadSignature",
                deliveryId, eventKey);
            return Results.Unauthorized();
        }

        WebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookPayload>(rawBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Webhook rejected DeliveryId={DeliveryId} EventKey={EventKey} Outcome=MalformedJson",
                deliveryId, eventKey);
            return Results.BadRequest();
        }

        var owner = payload?.Repository?.Workspace?.Slug;
        var repo = payload?.Repository?.Name;
        var prId = payload?.PullRequest?.Id ?? 0;
        var headSha = payload?.PullRequest?.Source?.Commit?.Hash;

        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo) || prId <= 0)
        {
            logger.LogWarning(
                "Webhook rejected Provider=Bitbucket DeliveryId={DeliveryId} EventKey={EventKey} Outcome=MissingRefs " +
                "(owner={Owner} repo={Repo} prId={PrId})",
                deliveryId, eventKey, owner, repo, prId);
            return Results.BadRequest();
        }

        if (string.IsNullOrEmpty(headSha))
        {
            logger.LogWarning(
                "Webhook rejected Provider=Bitbucket DeliveryId={DeliveryId} EventKey={EventKey} Outcome=MissingSha " +
                "for {Owner}/{Repo}#{PrId} — cannot dedup",
                deliveryId, eventKey, owner, repo, prId);
            return Results.BadRequest();
        }

        var prRef = new PullRequestRef(owner, repo, prId, payload?.PullRequest?.Title, Provider.Bitbucket);
        await queue.WriteAsync(new ReviewJob(prRef, headSha, DateTimeOffset.UtcNow), ct);

        logger.LogInformation(
            "Webhook enqueued Provider=Bitbucket DeliveryId={DeliveryId} EventKey={EventKey} Outcome=Enqueued " +
            "{Owner}/{Repo}#{PrId} sha={Sha} ({Title})",
            deliveryId, eventKey, owner, repo, prId, headSha, prRef.Title);

        return Results.NoContent();
    }
}
