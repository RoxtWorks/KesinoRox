using System.Collections.Generic;

// Feeds a pre-set sequence of die values for deterministic testing.
// Each Roll() consumes two values (die1, die2). Throws if sequence runs out.
public class FixedDiceSource : IRandomSource
{
    readonly Queue<int> values;

    public FixedDiceSource(params int[] vals) { values = new Queue<int>(vals); }

    public int Next(int minInclusive, int maxExclusive) => values.Dequeue();
}
