// Produces the actual roulette outcome. This is the whole game's fairness
// contract — a uniform draw over every physical pocket, exactly like a real
// European wheel: no bias, no house-favoring skew, nothing bet-aware.
public class SpinResultGenerator
{
    readonly IRandomSource rng;

    public SpinResultGenerator(IRandomSource rng) { this.rng = rng; }
    public SpinResultGenerator() : this(new SystemRandomSource()) { }

    // Returns the logical winning number, 0-36. Independent of WheelLayout.PocketOrder,
    // which only matters for where the number is drawn on the wheel's rim, not for odds.
    public int Spin() => rng.Next(0, WheelLayout.PocketCount);
}
