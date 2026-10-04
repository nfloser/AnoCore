using System.Security.Cryptography;
using AnoCore.Abstractions.Management;

namespace AnoCore.Runtime.Management;

public sealed record ManagementTokenCredential
{
    public ManagementTokenCredential(
        string tokenId,
        ManagementScope scopes,
        string saltBase64,
        string hashBase64,
        int iterations)
    {
        Principal = new ManagementPrincipal(tokenId, scopes);
        if (iterations is < 100_000 or > 2_000_000)
            throw new ArgumentOutOfRangeException(nameof(iterations));

        Salt = Decode(saltBase64, nameof(saltBase64), 16, 64);
        Hash = Decode(hashBase64, nameof(hashBase64), 32, 64);
        Iterations = iterations;
        SaltBase64 = Convert.ToBase64String(Salt);
        HashBase64 = Convert.ToBase64String(Hash);
    }

    public ManagementPrincipal Principal { get; }
    public string TokenId => Principal.TokenId;
    public ManagementScope Scopes => Principal.Scopes;
    public string SaltBase64 { get; }
    public string HashBase64 { get; }
    public int Iterations { get; }

    internal byte[] Salt { get; }
    internal byte[] Hash { get; }

    public override string ToString()
        => $"ManagementTokenCredential {{ TokenId = {TokenId}, Scopes = {Scopes}, Iterations = {Iterations} }}";

    private static byte[] Decode(
        string value,
        string parameterName,
        int minimum,
        int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Credential material is required.", parameterName);
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "Credential material must be valid base64.", parameterName, exception);
        }

        if (decoded.Length < minimum || decoded.Length > maximum)
            throw new ArgumentException(
                $"Credential material must decode to {minimum}-{maximum} bytes.",
                parameterName);
        return decoded;
    }
}

public static class ManagementTokenHasher
{
    public const int DefaultIterations = 210_000;
    private const int SaltBytes = 24;
    private const int HashBytes = 32;

    public static ManagementTokenCredential Create(
        string tokenId,
        string secret,
        ManagementScope scopes,
        int iterations = DefaultIterations)
    {
        ValidateSecret(secret);
        if (iterations is < 100_000 or > 2_000_000)
            throw new ArgumentOutOfRangeException(nameof(iterations));

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(secret, salt, iterations);
        return new ManagementTokenCredential(
            tokenId,
            scopes,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash),
            iterations);
    }

    public static bool Verify(
        ManagementTokenCredential credential,
        string secret)
    {
        ArgumentNullException.ThrowIfNull(credential);
        try
        {
            ValidateSecret(secret);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var candidate = Derive(secret, credential.Salt, credential.Iterations);
        return CryptographicOperations.FixedTimeEquals(candidate, credential.Hash);
    }

    private static byte[] Derive(
        string secret,
        byte[] salt,
        int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(
            secret,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashBytes);

    private static void ValidateSecret(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)
            || secret.Length is < 24 or > 512
            || secret.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Management token secrets must contain 24-512 printable characters.",
                nameof(secret));
        }
    }
}

public sealed class ManagementTokenAuthenticator
{
    private readonly IReadOnlyDictionary<string, ManagementTokenCredential> _credentials;

    public ManagementTokenAuthenticator(
        IEnumerable<ManagementTokenCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var values = credentials.ToArray();
        if (values.Length is < 1 or > 128)
            throw new ArgumentException(
                "Management authentication requires 1-128 credentials.",
                nameof(credentials));

        var dictionary = new Dictionary<string, ManagementTokenCredential>(
            StringComparer.Ordinal);
        foreach (var credential in values)
        {
            ArgumentNullException.ThrowIfNull(credential);
            if (!dictionary.TryAdd(credential.TokenId, credential))
                throw new ArgumentException(
                    $"Duplicate management token id '{credential.TokenId}'.",
                    nameof(credentials));
        }

        _credentials = dictionary;
    }

    public ManagementPrincipal? Authenticate(string tokenId, string secret)
    {
        if (string.IsNullOrWhiteSpace(tokenId)
            || !_credentials.TryGetValue(tokenId.Trim(), out var credential))
        {
            // Perform one bounded derivation for unknown ids too, reducing the usefulness
            // of token-id timing as an enumeration oracle.
            _ = ManagementTokenHasher.Verify(_credentials.Values.First(), secret);
            return null;
        }

        return ManagementTokenHasher.Verify(credential, secret)
            ? credential.Principal
            : null;
    }
}
