using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PrReviewer.Api.Bitbucket;

public sealed class WebhookSignatureValidator
{
    private readonly BitbucketOptions _options;
    private readonly ILogger<WebhookSignatureValidator> _logger;
    private bool _missingSecretWarningLogged;

    public WebhookSignatureValidator(
        IOptions<BitbucketOptions> options,
        ILogger<WebhookSignatureValidator> logger)
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
                    "Bitbucket:WebhookSecret is not configured — webhook signatures will not be verified. " +
                    "Set the secret via user-secrets or environment for production.");
                _missingSecretWarningLogged = true;
            }
            return true;
        }

        if (string.IsNullOrEmpty(signatureHeader))
            return false;

        const string prefix = "sha256=";
        var hex = signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? signatureHeader[prefix.Length..]
            : signatureHeader;

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return false;
        }

        var keyBytes = Encoding.UTF8.GetBytes(_options.WebhookSecret);
        var computed = HMACSHA256.HashData(keyBytes, rawBody);

        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }
}
