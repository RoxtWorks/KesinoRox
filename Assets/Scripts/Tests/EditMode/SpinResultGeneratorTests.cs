using NUnit.Framework;

public class SpinResultGeneratorTests
{
    [Test]
    public void Spin_AlwaysInValidRange()
    {
        var generator = new SpinResultGenerator(new SystemRandomSource(1234));
        for (int i = 0; i < 10000; i++)
        {
            int result = generator.Spin();
            Assert.GreaterOrEqual(result, 0);
            Assert.LessOrEqual(result, 36);
        }
    }

    [Test]
    public void Spin_DistributionIsRoughlyUniform()
    {
        var generator = new SpinResultGenerator(new SystemRandomSource(42));
        int[] counts = new int[37];
        const int trials = 370000; // 10000 expected per pocket

        for (int i = 0; i < trials; i++)
            counts[generator.Spin()]++;

        int expected = trials / 37;
        int tolerance = (int)(expected * 0.15); // generous band, this just guards against range/skew bugs

        for (int pocket = 0; pocket < 37; pocket++)
        {
            Assert.That(counts[pocket], Is.InRange(expected - tolerance, expected + tolerance),
                $"Pocket {pocket} landed {counts[pocket]} times, expected ~{expected}");
        }
    }
}
