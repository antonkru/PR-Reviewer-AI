using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrReviewer.Api.Webhooks;

namespace PrReviewer.Api.SourceControlClients.GitHub;

public sealed class GitHubWebhookSignatureValidator
{
    private readonly GitHubOptions _options;
    private readonly ILogger<GitHubWebhookSignatureValidator> _logger;
    private bool _missingSecretWarningLogged;

    public GitHubWebhookSignatureValidator(
        IOptions<GitHubOptions> options,
        ILogger<GitHubWebhookSignatureValidator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool Validate(byte[] rawBody, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(_options.WebhookSecret))
        {
            if (!_missingSecretWarningLogged)
            {
                _logger.LogWarning(
                    "GitHub:WebhookSecret is not configured — webhook signatures will not be verified. " +
                    "Set the secret via user-secrets or environment for production.");
                _missingSecretWarningLogged = true;
            }
            return true;
        }

        return HmacSha256SignatureVerifier.Verify(rawBody, signatureHeader, _options.WebhookSecret);
    }
}
