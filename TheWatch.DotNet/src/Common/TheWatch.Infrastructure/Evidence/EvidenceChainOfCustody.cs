using System;
using System.Security.Cryptography;
using System.Text;

namespace TheWatch.Infrastructure.EvidenceForensics;

/// <summary>
/// Manages cryptographic chain-of-custody records for bodycam video, sensor data, and audit events.
/// </summary>
/// <remarks>
/// Implements ISO/IEC 27037 standards for digital evidence handling with SHA-256 block hashing.
/// </remarks>
public class EvidenceChainOfCustody
{
    /// <summary>
    /// Generates a tamper-evident SHA-256 cryptographic fingerprint for raw binary or text data.
    /// </summary>
    /// <param name="rawContent">The raw content bytes.</param>
    /// <returns>Hex-encoded SHA-256 checksum string.</returns>
    public static string ComputeEvidenceFingerprint(byte[] rawContent)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(rawContent);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Creates a chained tamper-proof evidence block linked to the previous block hash.
    /// </summary>
    /// <param name="evidenceId">Unique identifier of the evidence item.</param>
    /// <param name="collectorId">ID of the officer, drone, or sensor capturing evidence.</param>
    /// <param name="payloadHash">SHA-256 hash of the evidence payload.</param>
    /// <param name="previousBlockHash">Cryptographic hash of the preceding evidence record in the chain.</param>
    /// <returns>A new sealed evidence block.</returns>
    public static EvidenceBlock SealEvidenceBlock(string evidenceId, string collectorId, string payloadHash, string previousBlockHash)
    {
        var timestamp = DateTime.UtcNow;
        var blockData = $"{evidenceId}:{collectorId}:{payloadHash}:{previousBlockHash}:{timestamp:O}";
        var blockHash = ComputeEvidenceFingerprint(Encoding.UTF8.GetBytes(blockData));

        return new EvidenceBlock(evidenceId, collectorId, payloadHash, previousBlockHash, blockHash, timestamp);
    }
}

/// <summary>
/// Represents an immutable sealed evidence record in the chain-of-custody ledger.
/// </summary>
/// <param name="EvidenceId">Unique evidence identifier.</param>
/// <param name="CollectorId">Identifier of collecting party/sensor.</param>
/// <param name="PayloadHash">Cryptographic hash of payload content.</param>
/// <param name="PreviousBlockHash">Parent block hash.</param>
/// <param name="BlockHash">Unique hash sealing this block.</param>
/// <param name="Timestamp">UTC creation timestamp.</param>
public record EvidenceBlock(string EvidenceId, string CollectorId, string PayloadHash, string PreviousBlockHash, string BlockHash, DateTime Timestamp);
