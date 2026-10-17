using System.Buffers;

namespace MessageHeightTwitch.Measurement.Shared;

/// <summary>
/// Grows rented buffers and returns the previous arrays to their shared pools.
/// </summary>
internal static class PooledBuffers
{
    internal static void Ensure<T>(ref T[] Buffer, int Length)
    {
        if (Buffer.Length >= Length)
            return;
        var next = ArrayPool<T>.Shared.Rent(Length);
        ArrayPool<T>.Shared.Return(Buffer);
        Buffer = next;
    }
}
