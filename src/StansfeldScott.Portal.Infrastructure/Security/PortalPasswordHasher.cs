using System.Security.Cryptography;
using System.Text;

namespace StansfeldScott.Portal.Infrastructure.Security;

/// <summary>Creates salted, one-way password hashes for the CRM Contact field.</summary>
public static class PortalPasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MinimumAcceptedIterations = 100_000;
    private const int MaximumAcceptedIterations = 2_000_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return string.Join('$', Prefix, Iterations.ToString(),
            Convert.ToBase64String(salt), Convert.ToBase64String(derivedKey));
    }

    /// <summary>
    /// Verifies current hashes. Legacy plaintext values are accepted once and
    /// returned as a replacement hash so a successful login upgrades the CRM row.
    /// </summary>
    public static bool VerifyAndUpgrade(string storedValue, string suppliedPassword, out string? upgradedHash)
    {
        upgradedHash = null;
        if (storedValue.StartsWith(Prefix + "$", StringComparison.Ordinal))
        {
            var parts = storedValue.Split('$');
            if (parts.Length != 4
                || !int.TryParse(parts[1], out var iterations)
                || iterations < MinimumAcceptedIterations
                || iterations > MaximumAcceptedIterations)
            {
                return false;
            }

            try
            {
                var salt = Convert.FromBase64String(parts[2]);
                var expectedHash = Convert.FromBase64String(parts[3]);
                if (salt.Length < SaltSize || expectedHash.Length != HashSize)
                    return false;

                var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    suppliedPassword, salt, iterations, HashAlgorithmName.SHA256, HashSize);
                var matches = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
                if (matches && iterations < Iterations)
                    upgradedHash = Hash(suppliedPassword);
                return matches;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // Temporary compatibility for credentials stored before hashing was enabled.
        var storedBytes = Encoding.UTF8.GetBytes(storedValue);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedPassword);
        if (storedBytes.Length != suppliedBytes.Length
            || !CryptographicOperations.FixedTimeEquals(storedBytes, suppliedBytes))
        {
            return false;
        }

        upgradedHash = Hash(suppliedPassword);
        return true;
    }
}
