using PrReviewer.Api.Bitbucket;
using PrReviewer.Api.GitHub;
using PrReviewer.Domain.Abstractions;
using PrReviewer.Domain.Models;

namespace PrReviewer.Api.Infrastructure;

public sealed class SourceControlClientFactory : ISourceControlClientFactory
{
    private readonly IServiceProvider _services;

    public SourceControlClientFactory(IServiceProvider services)
    {
        _services = services;
    }

    public ISourceControlClient For(Provider provider) => provider switch
    {
        Provider.Bitbucket => _services.GetRequiredService<BitbucketClient>(),
        Provider.GitHub => _services.GetRequiredService<GitHubClient>(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider"),
    };
}
