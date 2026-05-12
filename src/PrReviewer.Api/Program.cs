using System.Net.Http.Headers;
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

        builder.Services.AddOptions<BitbucketOptions>()
            .Bind(builder.Configuration.GetSection(BitbucketOptions.SectionName));

        builder.Services.AddOptions<GitHubOptions>()
            .Bind(builder.Configuration.GetSection(GitHubOptions.SectionName));

        builder.Services.AddSingleton<BitbucketWebhookSignatureValidator>();
        builder.Services.AddSingleton<GitHubWebhookSignatureValidator>();
        builder.Services.AddSingleton<IReviewQueue, ChannelReviewQueue>();
        builder.Services.AddSingleton<ISourceControlClientFactory, SourceControlClientFactory>();

        builder.Services.AddHttpClient<BitbucketClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BitbucketOptions>>().Value;
            http.BaseAddress = new Uri("https://api.bitbucket.org/2.0/");
            if (!string.IsNullOrEmpty(opts.AccessToken))
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", opts.AccessToken);
            }
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        builder.Services.AddHttpClient<GitHubClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GitHubOptions>>().Value;
            http.BaseAddress = new Uri(string.IsNullOrEmpty(opts.BaseAddress) ? "https://api.github.com/" : opts.BaseAddress);
            if (!string.IsNullOrEmpty(opts.AccessToken))
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", opts.AccessToken);
            }
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            var userAgent = string.IsNullOrWhiteSpace(opts.UserAgent) ? "PR-Reviewer-AI" : opts.UserAgent;
            http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        });

        builder.Services.AddReviewerAgents(builder.Configuration);

        var app = builder.Build();

        app.MapBitbucketWebhook();
        app.MapGitHubWebhook();

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
