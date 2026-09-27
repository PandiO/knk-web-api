using System.Security.Cryptography;

namespace knkwebapi_v2.Services.Lootbox;

/// <summary>
/// <see cref="ILootRandom"/> over <see cref="RandomNumberGenerator"/>: unpredictable, thread-safe and unbiased, so a
/// player can't infer or steer the next roll.
/// </summary>
public sealed class CryptoLootRandom : ILootRandom
{
    public int NextInt(int minInclusive, int maxExclusive) =>
        RandomNumberGenerator.GetInt32(minInclusive, maxExclusive);

    public double NextDouble()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        // 53 random bits -> [0, 1) with full double precision.
        return (BitConverter.ToUInt64(bytes) >> 11) * (1.0 / (1UL << 53));
    }
}
