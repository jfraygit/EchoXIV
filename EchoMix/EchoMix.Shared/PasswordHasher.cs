using System;
using System.Security.Cryptography;
using System.Text;

namespace EchoMix.Shared;

/// Turns a room password into a fixed-length hash before it ever leaves the process that collected it, so the
/// relay only ever sees/compares hashes, never the plaintext.
public static class PasswordHasher
{
    public static string Hash(string? password)
    {
        var bytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// Compares two hashes in constant time - a plain string/== comparison short-circuits on the first
    /// mismatched character, which in principle lets a remote attacker recover a room's password hash one
    /// byte at a time by timing repeated join attempts.
    public static bool Verify(string hash, string candidateHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hash), Encoding.UTF8.GetBytes(candidateHash));
}
