using System.Security.Cryptography;
using System.Text;

namespace Shink.Services;

public sealed class WordPressPasswordVerifier
{
    private const string PortableHashAlphabet = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public bool Verify(string? password, string? hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        if (password.Length > 4096)
        {
            return false;
        }

        if (hash.Length <= 32)
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(hash),
                Encoding.UTF8.GetBytes(Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant()));
        }

        if (hash.StartsWith("$wp", StringComparison.Ordinal))
        {
            return VerifyWordPressBcrypt(password, hash);
        }

        if (hash.StartsWith("$P$", StringComparison.Ordinal) || hash.StartsWith("$H$", StringComparison.Ordinal))
        {
            return VerifyPortablePhpPasswordHash(password, hash);
        }

        return VerifyBcrypt(password, hash);
    }

    public string DetectFormat(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return "unknown";
        }

        if (hash.StartsWith("$wp", StringComparison.Ordinal))
        {
            return "wp_bcrypt";
        }

        if (hash.StartsWith("$P$", StringComparison.Ordinal) || hash.StartsWith("$H$", StringComparison.Ordinal))
        {
            return "phpass";
        }

        if (hash.StartsWith("$2y$", StringComparison.Ordinal) ||
            hash.StartsWith("$2b$", StringComparison.Ordinal) ||
            hash.StartsWith("$2a$", StringComparison.Ordinal))
        {
            return "bcrypt";
        }

        if (hash.Length <= 32)
        {
            return "md5";
        }

        return "unknown";
    }

    private static bool VerifyWordPressBcrypt(string password, string hash)
    {
        if (hash.Length <= 3)
        {
            return false;
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password.Trim());
        var passwordToVerify = Convert.ToBase64String(HMACSHA384.HashData(Encoding.UTF8.GetBytes("wp-sha384"), passwordBytes));
        return VerifyBcrypt(passwordToVerify, hash[3..]);
    }

    private static bool VerifyBcrypt(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, NormalizeBcryptHash(hash));
        }
        catch (Exception) when (hash.StartsWith("$2y$", StringComparison.Ordinal))
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, "$2a$" + hash[4..]);
            }
            catch
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeBcryptHash(string hash) =>
        hash.StartsWith("$2y$", StringComparison.Ordinal)
            ? "$2b$" + hash[4..]
            : hash;

    private static bool VerifyPortablePhpPasswordHash(string password, string storedHash)
    {
        if (storedHash.Length < 34)
        {
            return false;
        }

        var countLog2 = PortableHashAlphabet.IndexOf(storedHash[3]);
        if (countLog2 is < 7 or > 30)
        {
            return false;
        }

        var salt = storedHash.Substring(4, 8);
        if (salt.Length != 8)
        {
            return false;
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var saltBytes = Encoding.ASCII.GetBytes(salt);
        var iterationCount = 1 << countLog2;

        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(Combine(saltBytes, passwordBytes));
        for (var i = 0; i < iterationCount; i++)
        {
            hashBytes = md5.ComputeHash(Combine(hashBytes, passwordBytes));
        }

        var encodedHash = EncodePortableHash(hashBytes, 16);
        var computedHash = storedHash[..12] + encodedHash;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computedHash),
            Encoding.ASCII.GetBytes(storedHash[..34]));
    }

    private static byte[] Combine(byte[] first, byte[] second)
    {
        var combined = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, combined, 0, first.Length);
        Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);
        return combined;
    }

    private static string EncodePortableHash(byte[] input, int count)
    {
        var output = new StringBuilder();
        var index = 0;

        do
        {
            var value = (int)input[index++];
            output.Append(PortableHashAlphabet[value & 0x3f]);

            if (index < count)
            {
                value |= input[index] << 8;
            }

            output.Append(PortableHashAlphabet[(value >> 6) & 0x3f]);

            if (index++ >= count)
            {
                break;
            }

            if (index < count)
            {
                value |= input[index] << 16;
            }

            output.Append(PortableHashAlphabet[(value >> 12) & 0x3f]);

            if (index++ >= count)
            {
                break;
            }

            output.Append(PortableHashAlphabet[(value >> 18) & 0x3f]);
        }
        while (index < count);

        return output.ToString();
    }
}
