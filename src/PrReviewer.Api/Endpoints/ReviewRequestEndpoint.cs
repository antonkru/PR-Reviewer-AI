using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
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
        IValidator<ReviewRequest> validator,
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
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Malformed JSON");
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Review request rejected Outcome=UnsupportedContentType");
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Unsupported content type");
        }

        if (request is null)
        {
            logger.LogWarning("Review request rejected Outcome=EmptyBody");
            return Results.NoContent();
        }

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            logger.LogWarning(
                "Review request rejected Outcome=InvalidPayload Errors={Errors}",
                string.Join("; ", validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var provider = Enum.Parse<Provider>(request.Provider!, ignoreCase: true);
        var headSha = string.IsNullOrEmpty(request.HeadSha) ? null : request.HeadSha;
        var prRef = new PullRequestRef(request.Owner!, request.Repo!, request.PrId, request.Title, provider);
        await queue.WriteAsync(new ReviewJob(prRef, headSha, DateTimeOffset.UtcNow), ct);

        logger.LogInformation(
            "Review request enqueued Provider={Provider} Outcome=Enqueued {Owner}/{Repo}#{PrId} sha={Sha} ({Title})",
            provider, request.Owner, request.Repo, request.PrId, headSha ?? "(force)", string.IsNullOrEmpty(request.Title) ? "(no title)" : request.Title);

        return Results.NoContent();
    }

    private static bool IsAuthorized(HttpContext context, string configuredToken, ILogger logger)
    {
        var env = context.RequestServices.GetRequiredService<IHostEnvironment>();
        if (string.IsNullOrEmpty(configuredToken))
        {
            if (!env.IsDevelopment())
                return false;

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
                    "Api:AccessToken is not configured — /reviews requests are unauthenticated. " +
                    "This is allowed only in Development; non-Development hosts will fail at startup.");
            }
        }
    }

}
