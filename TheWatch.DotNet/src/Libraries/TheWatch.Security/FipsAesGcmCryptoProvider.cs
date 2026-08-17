using System.Security.Cryptography;

namespace TheWatch.Security;

/// <summary>
/// Production-grade FIPS 140-3 compliant implementation utilizing hardware-accelerated AES-256-GCM.
/// </summary>
public sealed class FipsAesGcmCryptoProvider : IFipsCryptoProvider, IDisposable
{
    private readonly byte[] _key;
    private readonly AesGcm _aesGcm;

    public FipsAesGcmCryptoProvider(byte[]? key256Bits = null)
    {
        if (key256Bits == null || key256Bits.Length != 32)
        {
            _key = new byte[32];
            RandomNumberGenerator.Fill(_key);
        }
        else
        {
            _key = (byte[])key256Bits.Clone();
        }

        _aesGcm = new AesGcm(_key, 16); // 16 bytes tag (128-bit MAC)
    }

    public EncryptedPayload Encrypt(byte[] plaintext, byte[]? associatedData = null)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var nonce = new byte[12]; // 96-bit standard GCM nonce
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        _aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return new EncryptedPayload(ciphertext, nonce, tag);
    }

    public byte[] Decrypt(byte[] ciphertext, byte[] nonce, byte[] tag, byte[]? associatedData = null)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(tag);

        var plaintext = new byte[ciphertext.Length];
        _aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

        return plaintext;
    }

    public string ComputeSha256Hash(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var hashBytes = SHA256.HashData(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public bool VerifySha256Hash(byte[] data, string expectedHashHex)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(expectedHashHex);

        var computed = ComputeSha256Hash(data);
        return string.Equals(computed, expectedHashHex, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _aesGcm.Dispose();
        CryptographicOperations.ZeroMemory(_key);
    }
}
