using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PrReviewer.Api.Infrastructure;
using PrReviewer.Domain.Abstractions;
using PrReviewer.Domain.Models;

namespace PrReviewer.Api.Endpoints;

public static class ReviewRequestEndpoint
{
    private const string BearerPrefix = "Bearer ";

    public static IEndpointRouteBuilder MapReviewRequest(this IEndpointRouteBuilder app)
    {
        app.MapPost("/reviews", HandleAsync);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IOptions<ApiOptions> apiOptions,
        IReviewQueue queue,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ReviewRequest");

        if (!IsAuthorized(context, apiOptions.Value.AccessToken, logger))
        {
            logger.LogWarning("Review request rejected Outcome=Unauthorized");
            return Results.Unauthorized();
        }

        ReviewRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ReviewRequest>(ct);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Review request rejected Outcome=MalformedJson");
            return Results.BadRequest();
        }

        if (request is null)
        {
            logger.LogWarning("Review request rejected Outcome=EmptyBody");
            return Results.BadRequest();
        }

        if (!Enum.IsDefined(request.Provider))
        {
            logger.LogWarning(
                "Review request rejected Outcome=UnknownProvider Provider={Provider}",
                request.Provider);
            return Results.BadRequest();
        }

        if (string.IsNullOrEmpty(request.Owner) || string.IsNullOrEmpty(request.Repo) || request.PrId <= 0)
        {
            logger.LogWarning(
                "Review request rejected Outcome=MissingRefs (provider={Provider} owner={Owner} repo={Repo} prId={PrId})",
                request.Provider, request.Owner, request.Repo, request.PrId);
            return Results.BadRequest();
        }

        var headSha = string.IsNullOrEmpty(request.HeadSha) ? null : request.HeadSha;
        var prRef = new PullRequestRef(request.Owner, request.Repo, request.PrId, request.Title, request.Provider);
        await queue.WriteAsync(new ReviewJob(prRef, headSha, DateTimeOffset.UtcNow), ct);

        logger.LogInformation(
            "Review request enqueued Provider={Provider} Outcome=Enqueued {Owner}/{Repo}#{PrId} sha={Sha} ({Title})",
            request.Provider, request.Owner, request.Repo, request.PrId, headSha ?? "(force)", request.Title);

        return Results.NoContent();
    }

    private static bool IsAuthorized(HttpContext context, string configuredToken, ILogger logger)
    {
        if (string.IsNullOrEmpty(configuredToken))
        {
            MissingTokenWarning.LogOnce(logger);
            return true;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
            return false;

        var presented = header[BearerPrefix.Length..];
        if (string.IsNullOrEmpty(presented))
            return false;

        var expectedBytes = Encoding.UTF8.GetBytes(configuredToken);
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, presentedBytes);
    }

    private static class MissingTokenWarning
    {
        private static int _logged;

        public static void LogOnce(ILogger logger)
        {
            if (Interlocked.Exchange(ref _logged, 1) == 0)
            {
                logger.LogWarning(
                    "Api:AccessToken is not configured — /reviews requests will not be authenticated. " +
                    "Set the token via user-secrets or environment for production.");
            }
        }
    }

    private sealed record ReviewRequest(
        Provider Provider,
        string Owner,
        string Repo,
        int PrId,
        string? HeadSha,
        string? Title);
}
