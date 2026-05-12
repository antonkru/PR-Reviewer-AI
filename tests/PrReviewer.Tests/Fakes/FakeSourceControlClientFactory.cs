using PrReviewer.Domain.Abstractions;
using PrReviewer.Domain.Models;

namespace PrReviewer.Tests.Fakes;

public sealed class FakeSourceControlClientFactory : ISourceControlClientFactory
{
    public Dictionary<Provider, ISourceControlClient> Clients { get; } = new();

    public static FakeSourceControlClientFactory ForAll(ISourceControlClient client)
    {
        var f = new FakeSourceControlClientFactory();
        f.Clients[Provider.Bitbucket] = client;
        f.Clients[Provider.GitHub] = client;
        return f;
    }

    public ISourceControlClient For(Provider provider) => Clients[provider];
}
