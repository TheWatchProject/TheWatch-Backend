using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TheWatch.Infrastructure.Jobs;

public sealed record MerkleBatchSealResult(string BatchId, string RootHash, int ItemCount, DateTime SealedAtUtc);

/// <summary>
/// Background job that batches unsealed evidence items into an immutable Merkle tree root and seals the cryptographic audit proof.
/// </summary>
public sealed class MerkleBatchSealAndNotarizationJob
{
    public Task<MerkleBatchSealResult> SealBatchAsync(IEnumerable<string> itemHashes, CancellationToken cancellationToken = default)
    {
        var hashes = new List<string>(itemHashes);
        if (hashes.Count == 0)
        {
            return Task.FromResult(new MerkleBatchSealResult(
                BatchId: Guid.NewGuid().ToString("N"),
                RootHash: "0000000000000000000000000000000000000000000000000000000000000000",
                ItemCount: 0,
                SealedAtUtc: DateTime.UtcNow
            ));
        }

        while (hashes.Count > 1)
        {
            if (cancellationToken.IsCancellationRequested) break;

            if (hashes.Count % 2 != 0)
            {
                hashes.Add(hashes[^1]);
            }

            var nextLevel = new List<string>();
            for (int i = 0; i < hashes.Count; i += 2)
            {
                var combined = hashes[i] + hashes[i + 1];
                nextLevel.Add(ComputeSha256(combined));
            }
            hashes = nextLevel;
        }

        return Task.FromResult(new MerkleBatchSealResult(
            BatchId: Guid.NewGuid().ToString("N"),
            RootHash: hashes[0],
            ItemCount: hashes.Count,
            SealedAtUtc: DateTime.UtcNow
        ));
    }

    private static string ComputeSha256(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
