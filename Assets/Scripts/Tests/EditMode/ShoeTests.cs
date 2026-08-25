using System.Collections.Generic;
using NUnit.Framework;

public class ShoeTests
{
    [Test]
    public void Composition_HasExactRankAndSuitCountsForDeckCount()
    {
        var shoe = new Shoe(6, new SystemRandomSource(1));
        var rankCounts = new Dictionary<Rank, int>();
        var suitCounts = new Dictionary<Suit, int>();
        for (int i = 0; i < 312; i++)
        {
            var card = shoe.Draw();
            rankCounts[card.Rank] = rankCounts.GetValueOrDefault(card.Rank) + 1;
            suitCounts[card.Suit] = suitCounts.GetValueOrDefault(card.Suit) + 1;
        }
        foreach (Rank rank in System.Enum.GetValues(typeof(Rank)))
            Assert.AreEqual(24, rankCounts[rank], $"{rank} should appear 24 times (4 suits x 6 decks)");
        foreach (Suit suit in System.Enum.GetValues(typeof(Suit)))
            Assert.AreEqual(78, suitCounts[suit], $"{suit} should appear 78 times (13 ranks x 6 decks)");
    }

    [Test]
    public void NeedsReshuffle_FalseWhenFreshlyShuffled()
    {
        var shoe = new Shoe(6, new SystemRandomSource(1));
        Assert.IsFalse(shoe.NeedsReshuffle);
    }

    [Test]
    public void NeedsReshuffle_TrueOnceRemainingDropsToQuarter()
    {
        var shoe = new Shoe(6, new SystemRandomSource(1));
        // 312 total, threshold is 78 remaining (25%) — draw down to exactly that.
        for (int i = 0; i < 312 - 78; i++) shoe.Draw();
        Assert.AreEqual(78, shoe.RemainingCards);
        Assert.IsTrue(shoe.NeedsReshuffle);
    }

    [Test]
    public void Draw_AutoReshufflesRatherThanRunningOut()
    {
        var shoe = new Shoe(6, new SystemRandomSource(1));
        for (int i = 0; i < 312; i++) shoe.Draw();
        // Shoe is now fully depleted — the next draw must not throw, and must come
        // from a freshly reshuffled shoe instead.
        Assert.DoesNotThrow(() => shoe.Draw());
        Assert.AreEqual(311, shoe.RemainingCards);
    }

    [Test]
    public void SameSeed_ProducesIdenticalDrawSequence()
    {
        var shoeA = new Shoe(6, new SystemRandomSource(42));
        var shoeB = new Shoe(6, new SystemRandomSource(42));
        for (int i = 0; i < 50; i++)
        {
            var a = shoeA.Draw();
            var b = shoeB.Draw();
            Assert.AreEqual(a.Rank, b.Rank);
            Assert.AreEqual(a.Suit, b.Suit);
        }
    }
}
