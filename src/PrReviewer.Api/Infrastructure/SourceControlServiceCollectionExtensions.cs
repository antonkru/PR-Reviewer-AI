using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using PrReviewer.Api.SourceControlClients.Bitbucket;
using PrReviewer.Api.SourceControlClients.GitHub;
using PrReviewer.Domain.Abstractions;

namespace PrReviewer.Api.Infrastructure;

public static class SourceControlServiceCollectionExtensions
{
    public static IServiceCollection AddSourceControlClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BitbucketOptions>()
            .Bind(configuration.GetSection(BitbucketOptions.SectionName));

        services.AddOptions<GitHubOptions>()
            .Bind(configuration.GetSection(GitHubOptions.SectionName));

        services.AddSingleton<ISourceControlClientFactory, SourceControlClientFactory>();

        services.AddHttpClient<BitbucketClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<BitbucketOptions>>().Value;
            http.BaseAddress = new Uri(string.IsNullOrEmpty(opts.BaseAddress) ? "https://api.bitbucket.org/2.0/" : opts.BaseAddress);
            if (!string.IsNullOrEmpty(opts.AccessToken))
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", opts.AccessToken);
            }
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.AddHttpClient<GitHubClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<GitHubOptions>>().Value;
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

        return services;
    }
}
