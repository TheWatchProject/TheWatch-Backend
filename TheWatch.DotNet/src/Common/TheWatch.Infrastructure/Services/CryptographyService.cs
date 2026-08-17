using System.Security.Cryptography;
using System.Text;
using TheWatch.Core.Interfaces;
using TheWatch.Security.Cryptography;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Implementation of cryptographic operations using built-in .NET libraries.
/// Uses BCrypt for password hashing and the generated AES-256-GCM primitive for encryption.
/// </summary>
public class CryptographyService : ICryptographyService, IDisposable
{
    private readonly AesGcmCipher _cipher;
    private const int BCryptWorkFactor = 12;
    private const int LegacyPbkdf2Iterations = 4;
    private const int LegacySaltSize = 16;
    private const int LegacyHashSize = 32;

    public CryptographyService(string encryptionKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(encryptionKeyBase64))
        {
            throw new ArgumentException("Encryption key must be provided", nameof(encryptionKeyBase64));
        }

        byte[] encryptionKey = Convert.FromBase64String(encryptionKeyBase64);

        if (encryptionKey.Length != AesGcmCipher.KeySize)
        {
            throw new ArgumentException("Encryption key must be 32 bytes (256 bits)", nameof(encryptionKeyBase64));
        }

        try
        {
            _cipher = new AesGcmCipher(encryptionKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
        }
    }

    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty", nameof(password));
        }

        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: BCryptWorkFactor);
    }

    public bool VerifyPassword(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        try
        {
            if (hash.StartsWith("$2", StringComparison.Ordinal))
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }

            // Read compatibility for hashes created by the previous implementation.
            byte[] combined = Convert.FromBase64String(hash);

            if (combined.Length != LegacySaltSize + LegacyHashSize)
            {
                return false;
            }

            byte[] salt = new byte[LegacySaltSize];
            byte[] storedHash = new byte[LegacyHashSize];
            Buffer.BlockCopy(combined, 0, salt, 0, LegacySaltSize);
            Buffer.BlockCopy(combined, LegacySaltSize, storedHash, 0, LegacyHashSize);

            byte[] computedHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                LegacyPbkdf2Iterations,
                HashAlgorithmName.SHA256,
                LegacyHashSize);

            return CryptographicOperations.FixedTimeEquals(storedHash, computedHash);
        }
        catch
        {
            return false;
        }
    }

    public string ComputeSha256Hash(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            throw new ArgumentException("Input cannot be empty", nameof(input));
        }

        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = SHA256.HashData(inputBytes);

        return Convert.ToBase64String(hashBytes);
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            throw new ArgumentException("Plaintext cannot be empty", nameof(plaintext));
        }

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return Convert.ToBase64String(_cipher.Encrypt(plaintextBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            throw new ArgumentException("Ciphertext cannot be empty", nameof(ciphertext));
        }

        try
        {
            byte[] frame = Convert.FromBase64String(ciphertext);
            byte[] plaintextBytes;
            try
            {
                plaintextBytes = _cipher.Decrypt(frame);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(frame);
            }
            try
            {
                return Encoding.UTF8.GetString(plaintextBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintextBytes);
            }
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Decryption failed - data may be corrupted or tampered with");
        }
    }

    /// <inheritdoc />
    public void Dispose() => _cipher.Dispose();

    public string GenerateVerificationCode()
    {
        // Generate 6-digit code
        int code = RandomNumberGenerator.GetInt32(100000, 1000000);
        return code.ToString("D6");
    }

    public string GenerateTotpSecret()
    {
        // Generate 160-bit (20 byte) secret for TOTP (RFC 6238)
        byte[] secret = RandomNumberGenerator.GetBytes(20);
        return secret.ToBase32String();
    }

    public bool VerifyTotpCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        if (code.Length != 6 || !int.TryParse(code, out _))
        {
            return false;
        }

        try
        {
            byte[] secretBytes = secret.FromBase32String();
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

            // Check current time window and ±1 window (30 seconds before/after) for clock skew
            for (int i = -1; i <= 1; i++)
            {
                string expectedCode = GenerateTotpCodeForTime(secretBytes, currentTime + i);
                if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(code),
                    Encoding.UTF8.GetBytes(expectedCode)))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private string GenerateTotpCodeForTime(byte[] secret, long timeStep)
    {
        byte[] timeBytes = BitConverter.GetBytes(timeStep);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(timeBytes);
        }

        using (var hmac = new HMACSHA1(secret))
        {
            byte[] hash = hmac.ComputeHash(timeBytes);
            int offset = hash[^1] & 0x0F;

            int binary = ((hash[offset] & 0x7F) << 24) |
                        ((hash[offset + 1] & 0xFF) << 16) |
                        ((hash[offset + 2] & 0xFF) << 8) |
                        (hash[offset + 3] & 0xFF);

            int otp = binary % 1000000;
            return otp.ToString("D6");
        }
    }
}

/// <summary>
/// Extension methods for Base32 encoding/decoding for TOTP secrets.
/// </summary>
internal static class Base32Extensions
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string ToBase32String(this byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder result = new StringBuilder((bytes.Length * 8 + 4) / 5);
        int buffer = bytes[0];
        int bufferSize = 8;
        int index = 1;

        while (bufferSize > 0 || index < bytes.Length)
        {
            if (bufferSize < 5)
            {
                if (index < bytes.Length)
                {
                    buffer = (buffer << 8) | bytes[index++];
                    bufferSize += 8;
                }
                else
                {
                    int padding = 5 - bufferSize;
                    buffer <<= padding;
                    bufferSize += padding;
                }
            }

            int value = (buffer >> (bufferSize - 5)) & 0x1F;
            bufferSize -= 5;
            result.Append(Base32Alphabet[value]);
        }

        return result.ToString();
    }

    public static byte[] FromBase32String(this string base32)
    {
        if (string.IsNullOrWhiteSpace(base32))
        {
            return Array.Empty<byte>();
        }

        base32 = base32.TrimEnd('=').ToUpperInvariant();
        List<byte> result = new List<byte>();
        int buffer = 0;
        int bufferSize = 0;

        foreach (char c in base32)
        {
            int value = Base32Alphabet.IndexOf(c);
            if (value < 0)
            {
                throw new ArgumentException($"Invalid Base32 character: {c}");
            }

            buffer = (buffer << 5) | value;
            bufferSize += 5;

            if (bufferSize >= 8)
            {
                result.Add((byte)(buffer >> (bufferSize - 8)));
                bufferSize -= 8;
            }
        }

        return result.ToArray();
    }
}
