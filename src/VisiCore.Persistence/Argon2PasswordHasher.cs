using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace VisiCore.Persistence;

public sealed class Argon2PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 3;
    private const int MemorySize = 65_536;
    private const int Parallelism = 2;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var digest = Derive(password, salt);
        return $"argon2id$v=1$m={MemorySize},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(digest)}";
    }

    public bool Verify(string password, string encodedHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var parts = encodedHash.Split('$', StringSplitOptions.None);
        if (parts.Length != 5 || parts[0] != "argon2id" || parts[1] != "v=1")
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = Derive(password, salt);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt)
    {
        var algorithm = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            Iterations = Iterations,
            MemorySize = MemorySize,
            DegreeOfParallelism = Parallelism
        };
        return algorithm.GetBytes(HashSize);
    }
}
