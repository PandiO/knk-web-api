namespace knkwebapi_v2.Services.Lootbox;

/// <summary>
/// The randomness behind every lootbox roll (docs/specs/lootboxes/DESIGN.md §3.1), injected so tests can script
/// it. Production uses <see cref="CryptoLootRandom"/>.
/// </summary>
public interface ILootRandom
{
    /// <summary>A uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    int NextInt(int minInclusive, int maxExclusive);

    /// <summary>A uniform double in [0, 1).</summary>
    double NextDouble();
}
