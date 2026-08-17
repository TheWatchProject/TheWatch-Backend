namespace TheWatch.Security;

/// <summary>
/// FIPS 140-3 compliant authenticated encryption with associated data (AEAD) provider abstraction.
/// </summary>
public interface IFipsCryptoProvider
{
    EncryptedPayload Encrypt(byte[] plaintext, byte[]? associatedData = null);
    byte[] Decrypt(byte[] ciphertext, byte[] nonce, byte[] tag, byte[]? associatedData = null);
    string ComputeSha256Hash(byte[] data);
    bool VerifySha256Hash(byte[] data, string expectedHashHex);
}

public sealed record EncryptedPayload(
    byte[] Ciphertext,
    byte[] Nonce,
    byte[] Tag
);
