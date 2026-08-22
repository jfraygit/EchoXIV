using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EchoMix.Shared;

/// Certificate pinning shared by both the broadcast host connection and the listen client.
public static class RelayTls
{
    public static bool ValidatePinnedCertificate(X509Certificate? certificate)
    {
        if (certificate == null || string.IsNullOrEmpty(RelayConfig.PinnedCertThumbprint))
            return false;

        var actual = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));
        return string.Equals(actual, RelayConfig.PinnedCertThumbprint, StringComparison.OrdinalIgnoreCase);
    }
}
