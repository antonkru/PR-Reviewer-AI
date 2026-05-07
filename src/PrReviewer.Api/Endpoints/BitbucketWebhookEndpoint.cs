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
        WebhookSignatureValidator validator,
        IReviewQueue queue,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("BitbucketWebhook");

        var eventKey = context.Request.Headers["X-Event-Key"].ToString();
        if (!HandledEventKeys.Contains(eventKey))
        {
            logger.LogDebug("Ignoring Bitbucket event {EventKey}", eventKey);
            return Results.NoContent();
        }

        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms, ct);
        var rawBody = ms.ToArray();

        var signature = context.Request.Headers["X-Hub-Signature"].ToString();
        if (!validator.Validate(rawBody, signature))
        {
            logger.LogWarning("Rejected Bitbucket webhook for {EventKey}: invalid signature", eventKey);
            return Results.Unauthorized();
        }

        WebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookPayload>(rawBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Rejected Bitbucket webhook for {EventKey}: malformed JSON", eventKey);
            return Results.BadRequest();
        }

        var workspace = payload?.Repository?.Workspace?.Slug;
        var repo = payload?.Repository?.Name;
        var prId = payload?.PullRequest?.Id ?? 0;

        if (string.IsNullOrEmpty(workspace) || string.IsNullOrEmpty(repo) || prId <= 0)
        {
            logger.LogWarning(
                "Rejected Bitbucket webhook for {EventKey}: missing workspace/repo/prId (workspace={Workspace} repo={Repo} prId={PrId})",
                eventKey, workspace, repo, prId);
            return Results.BadRequest();
        }

        var prRef = new PullRequestRef(workspace, repo, prId, payload?.PullRequest?.Title);
        await queue.WriteAsync(new ReviewJob(prRef, DateTimeOffset.UtcNow), ct);

        logger.LogInformation(
            "Enqueued review for {EventKey} {Workspace}/{Repo}#{PrId} ({Title})",
            eventKey, workspace, repo, prId, prRef.Title);

        return Results.NoContent();
    }
}
