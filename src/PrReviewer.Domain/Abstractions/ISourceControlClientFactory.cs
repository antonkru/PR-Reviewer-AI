using PrReviewer.Domain.Models;

namespace PrReviewer.Domain.Abstractions;

public interface ISourceControlClientFactory
{
    ISourceControlClient For(Provider provider);
}
