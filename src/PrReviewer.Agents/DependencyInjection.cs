using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrReviewer.Agents.Configuration;
using PrReviewer.Domain.Abstractions;

namespace PrReviewer.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddReviewerAgents(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OpenAIOptions>()
            .Bind(configuration.GetSection(OpenAIOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IReviewerAgent, ReviewerAgent>();
        services.AddHostedService<ReviewBackgroundService>();

        return services;
    }
}
