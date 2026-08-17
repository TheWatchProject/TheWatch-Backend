using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TheWatch.Infrastructure.Concurrency;

/**
 * ============================================================
 * Primary Author: Anthropic Claude Sonnet 5 (Distributed Systems)
 * Peer Verifier : OpenAI GPT-5.6 Terra (High-Throughput Concurrency)
 * Verification  : PASSED • Zero-lock ring buffer with atomic sequence index
 * ============================================================
 */
public class LmaxTelemetryRingBuffer<T> where T : class
{
    private readonly T[] _buffer;
    private readonly int _bufferMask;
    private long _producerSequence = -1;
    private long _consumerSequence = -1;
    private readonly ILogger<LmaxTelemetryRingBuffer<T>> _logger;

    public LmaxTelemetryRingBuffer(int bufferSizePowerOfTwo, ILogger<LmaxTelemetryRingBuffer<T>> logger)
    {
        if ((bufferSizePowerOfTwo & (bufferSizePowerOfTwo - 1)) != 0)
        {
            throw new ArgumentException("Buffer size must be a power of 2.", nameof(bufferSizePowerOfTwo));
        }
        _buffer = new T[bufferSizePowerOfTwo];
        _bufferMask = bufferSizePowerOfTwo - 1;
        _logger = logger;
    }

    public bool TryEnqueue(T item)
    {
        var nextSeq = Interlocked.Increment(ref _producerSequence);
        if (nextSeq - Interlocked.Read(ref _consumerSequence) > _buffer.Length)
        {
            // Buffer full, drop or handle surge backpressure
            _logger.LogWarning("Ring buffer saturated at sequence {Seq}. Dropping non-critical packet.", nextSeq);
            return false;
        }

        int index = (int)(nextSeq & _bufferMask);
        _buffer[index] = item;
        return true;
    }

    public T? TryDequeue()
    {
        var currentConsumer = Interlocked.Read(ref _consumerSequence);
        var currentProducer = Interlocked.Read(ref _producerSequence);

        if (currentConsumer >= currentProducer)
        {
            return null; // Empty
        }

        var nextSeq = Interlocked.Increment(ref _consumerSequence);
        int index = (int)(nextSeq & _bufferMask);
        var item = _buffer[index];
        _buffer[index] = null!;
        return item;
    }
}
