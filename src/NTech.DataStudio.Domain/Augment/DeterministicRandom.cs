namespace NTech.DataStudio.Domain.Augment;

/// <summary>
/// SplitMix64. System.Random does not promise the same numbers for the same seed
/// across runtime versions, and a version must regenerate the same dataset years later.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed)
    {
        _state = seed;
    }

    /// <summary>A seed derived from several numbers, so that nearby inputs give unrelated streams.</summary>
    public static DeterministicRandom From(params long[] parts)
    {
        ulong state = 0x9E3779B97F4A7C15UL;
        foreach (var part in parts)
        {
            state ^= unchecked((ulong)part);
            state = Mix(state + 0x9E3779B97F4A7C15UL);
        }

        return new DeterministicRandom(state);
    }

    /// <summary>A stable 64-bit hash of text (FNV-1a); string.GetHashCode changes from run to run.</summary>
    public static long Hash(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var character in text)
        {
            hash ^= character;
            hash *= 1099511628211UL;
        }

        return unchecked((long)hash);
    }

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Mix(_state);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform in [0, <paramref name="exclusiveMax"/>).</summary>
    public int NextInt(int exclusiveMax) => exclusiveMax <= 1 ? 0 : (int)(NextDouble() * exclusiveMax);

    /// <summary>Uniform in [-<paramref name="magnitude"/>, +<paramref name="magnitude"/>].</summary>
    public double NextSigned(double magnitude) => (NextDouble() * 2 - 1) * magnitude;

    public bool NextBool() => (NextUInt64() & 1) == 1;

    public void Shuffle<T>(IList<T> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var other = NextInt(index + 1);
            (items[index], items[other]) = (items[other], items[index]);
        }
    }

    private static ulong Mix(ulong value)
    {
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
