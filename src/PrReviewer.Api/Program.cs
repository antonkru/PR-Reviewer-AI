using FluentValidation;
using PrReviewer.Agents;
using PrReviewer.Api.Bitbucket;
using PrReviewer.Api.Endpoints;
using PrReviewer.Api.GitHub;
using PrReviewer.Api.Infrastructure;
using PrReviewer.Domain.Abstractions;

namespace PrReviewer.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        if (!builder.Environment.IsDevelopment() 
            && string.IsNullOrEmpty(builder.Configuration[$"{ApiOptions.SectionName}:{nameof(ApiOptions.AccessToken)}"]))
        {
            throw new InvalidOperationException(
                $"{ApiOptions.SectionName}:{nameof(ApiOptions.AccessToken)} must be configured outside the Development environment.");
        }

        builder.Services.AddOptions<ApiOptions>()
            .Bind(builder.Configuration.GetSection(ApiOptions.SectionName));

        builder.Services.AddSingleton<BitbucketWebhookSignatureValidator>();
        builder.Services.AddSingleton<GitHubWebhookSignatureValidator>();
        builder.Services.AddSingleton<IReviewQueue, ChannelReviewQueue>();

        builder.Services.AddSingleton<IValidator<WebhookPayload>, BitbucketWebhookPayloadValidator>();
        builder.Services.AddSingleton<IValidator<GitHubWebhookPayload>, GitHubWebhookPayloadValidator>();
        builder.Services.AddSingleton<IValidator<ReviewRequest>, ReviewRequestValidator>();

        builder.Services.AddSourceControlClients(builder.Configuration);
        builder.Services.AddReviewerAgents(builder.Configuration);

        var app = builder.Build();

        app.MapBitbucketWebhook();
        app.MapGitHubWebhook();
        app.MapReviewRequest();

        app.MapGet("/", () => Results.Redirect("/health"));

        app.MapGet("/health", () => HealthCheckAsync());

        app.Run();
    }

    private static IResult HealthCheckAsync()
    {
        return Results.Ok(new
        {
            service = "PR-Reviewer-AI",
            status = "ok",
            timestamp = DateTimeOffset.UtcNow,
        });
    }
}
