// Seedable RNG abstraction so SpinResultGenerator can be tested deterministically
// without depending on UnityEngine.Random (which has global, non-injectable state).
public interface IRandomSource
{
    int Next(int minInclusive, int maxExclusive);
}

public class SystemRandomSource : IRandomSource
{
    readonly System.Random rng;

    public SystemRandomSource() : this(System.Guid.NewGuid().GetHashCode()) { }
    public SystemRandomSource(int seed) { rng = new System.Random(seed); }

    public int Next(int minInclusive, int maxExclusive) => rng.Next(minInclusive, maxExclusive);
}
