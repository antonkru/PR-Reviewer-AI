using System.Security.Cryptography;
using System.Text;

namespace PrReviewer.Api.Webhooks;

public static class HmacSha256SignatureVerifier
{
    private const string Prefix = "sha256=";

    public static bool Verify(byte[] rawBody, string? signatureHeader, string? secret)
    {
        if (string.IsNullOrEmpty(secret))
            return true;

        if (string.IsNullOrEmpty(signatureHeader))
            return false;

        var hex = signatureHeader.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? signatureHeader[Prefix.Length..]
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

        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var computed = HMACSHA256.HashData(keyBytes, rawBody);

        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }
}
