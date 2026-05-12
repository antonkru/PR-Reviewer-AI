using System.Text.Json;
using FluentValidation;
using PrReviewer.Api.SourceControlClients.GitHub;
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
        GitHubWebhookSignatureValidator signatureValidator,
        IValidator<GitHubWebhookPayload> payloadValidator,
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
        if (!signatureValidator.Validate(rawBody, signature))
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

        var validation = await payloadValidator.ValidateAsync(payload!, ct);
        if (!validation.IsValid)
        {
            logger.LogWarning(
                "Webhook rejected Provider=GitHub DeliveryId={DeliveryId} Event={Event} Outcome=InvalidPayload Errors={Errors}",
                deliveryId, eventName,
                string.Join("; ", validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var owner = payload!.Repository!.Owner!.Login!;
        var repo = payload.Repository.Name!;
        var prId = payload.PullRequest!.Number;
        var headSha = payload.PullRequest.Head!.Sha!;

        var prRef = new PullRequestRef(owner, repo, prId, payload.PullRequest.Title, Provider.GitHub);
        await queue.WriteAsync(new ReviewJob(prRef, headSha, DateTimeOffset.UtcNow), ct);

        logger.LogInformation(
            "Webhook enqueued Provider=GitHub DeliveryId={DeliveryId} Event={Event} Action={Action} Outcome=Enqueued " +
            "{Owner}/{Repo}#{PrId} sha={Sha} ({Title})",
            deliveryId, eventName, action, owner, repo, prId, headSha, prRef.Title);

        return Results.NoContent();
    }
}
