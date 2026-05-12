using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrReviewer.Api.SourceControlClients.GitHub;

namespace PrReviewer.Tests.GitHub;

public sealed class GitHubWebhookSignatureValidatorTests
{
    private const string Secret = "shhh-its-a-secret";

    [Fact]
    public void Allows_request_when_secret_not_configured()
    {
        var validator = CreateValidator(secret: "");

        var ok = validator.Validate(Encoding.UTF8.GetBytes("body"), signatureHeader: null);

        Assert.True(ok);
    }

    [Fact]
    public void Accepts_signature_with_sha256_prefix()
    {
        var body = Encoding.UTF8.GetBytes("hello github");
        var signature = "sha256=" + ComputeHex(Secret, body);
        var validator = CreateValidator(Secret);

        Assert.True(validator.Validate(body, signature));
    }

    [Fact]
    public void Rejects_when_signature_does_not_match()
    {
        var body = Encoding.UTF8.GetBytes("payload");
        var wrong = "sha256=" + ComputeHex("different-secret", body);
        var validator = CreateValidator(Secret);

        Assert.False(validator.Validate(body, wrong));
    }

    [Fact]
    public void Rejects_when_signature_header_is_missing()
    {
        var validator = CreateValidator(Secret);
        Assert.False(validator.Validate(Encoding.UTF8.GetBytes("payload"), signatureHeader: null));
        Assert.False(validator.Validate(Encoding.UTF8.GetBytes("payload"), signatureHeader: ""));
    }

    [Fact]
    public void Rejects_when_signature_is_not_hex()
    {
        var validator = CreateValidator(Secret);
        Assert.False(validator.Validate(Encoding.UTF8.GetBytes("payload"), "sha256=not-hex!"));
    }

    [Fact]
    public void Rejects_when_body_is_tampered()
    {
        var body = Encoding.UTF8.GetBytes("original");
        var signature = "sha256=" + ComputeHex(Secret, body);
        var validator = CreateValidator(Secret);

        var tampered = Encoding.UTF8.GetBytes("tampered");
        Assert.False(validator.Validate(tampered, signature));
    }

    private static GitHubWebhookSignatureValidator CreateValidator(string secret)
    {
        var options = Options.Create(new GitHubOptions { WebhookSecret = secret });
        return new GitHubWebhookSignatureValidator(options, NullLogger<GitHubWebhookSignatureValidator>.Instance);
    }

    private static string ComputeHex(string secret, byte[] body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return Convert.ToHexString(hash);
    }
}
