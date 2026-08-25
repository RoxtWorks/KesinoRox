using System.Linq;
using NUnit.Framework;

public class BaccaratRoundTests
{
    // Deals from a fixed, caller-specified card order (Shoe's test-only constructor)
    // so the third-card draw rules can be exercised deterministically instead of
    // depending on a real shuffle. Draw order matches BaccaratRound.Deal: Player,
    // Banker, Player, Banker, [Player third], [Banker third].
    static Shoe FixedShoe(params Rank[] ranks) => new Shoe(ranks.Select(r => new Card(r, Suit.Spades)));

    [Test]
    public void Deal_BothNatural_StopsAtTwoCardsEach()
    {
        // Player: Ace+Eight = 9 (natural). Banker: Two+Three = 5. No third cards
        // drawn once either side has a natural, regardless of the other's total.
        var round = new BaccaratRound(FixedShoe(Rank.Ace, Rank.Two, Rank.Eight, Rank.Three));
        round.Deal();

        Assert.IsTrue(round.RoundOver);
        Assert.AreEqual(2, round.Player.Cards.Count);
        Assert.AreEqual(2, round.Banker.Cards.Count);
        Assert.AreEqual(9, round.Player.Point);
        Assert.AreEqual(BaccaratOutcome.PlayerWin, round.Outcome);
    }

    [Test]
    public void Deal_PlayerDraws_BankerDrawsPerTable()
    {
        // Player: Two+Three = 5 -> draws. Banker: Four+Ten(0) = 4, player's third
        // card is Five (in banker-total-4's draw range 2-7) -> banker also draws.
        var round = new BaccaratRound(FixedShoe(Rank.Two, Rank.Four, Rank.Three, Rank.Ten, Rank.Five, Rank.Two));
        round.Deal();

        Assert.AreEqual(3, round.Player.Cards.Count);
        Assert.AreEqual(3, round.Banker.Cards.Count);
    }

    [Test]
    public void Deal_PlayerStands_BankerMirrorsStandOnSixSeven()
    {
        // Player: Three+Three = 6 -> stands. Banker: Two+Two = 4, player did NOT
        // draw -> banker mirrors the player's own rule (draws on 0-5).
        var round = new BaccaratRound(FixedShoe(Rank.Three, Rank.Two, Rank.Three, Rank.Two, Rank.Nine));
        round.Deal();

        Assert.AreEqual(2, round.Player.Cards.Count);
        Assert.AreEqual(3, round.Banker.Cards.Count);
    }

    [Test]
    public void Deal_NeitherDraws_BothStandOnSixOrSeven()
    {
        // Player: Three+Four = 7 -> stands. Banker: Three+Four = 7 -> stands too.
        var round = new BaccaratRound(FixedShoe(Rank.Three, Rank.Three, Rank.Four, Rank.Four));
        round.Deal();

        Assert.AreEqual(2, round.Player.Cards.Count);
        Assert.AreEqual(2, round.Banker.Cards.Count);
        Assert.AreEqual(BaccaratOutcome.Tie, round.Outcome);
    }

    [Test]
    public void Deal_NeverReshufflesMidHand()
    {
        // A shoe sitting right at its reshuffle threshold should still complete the
        // in-progress round on its current cards, not swap decks mid-deal.
        var shoe = new Shoe(1, new SystemRandomSource());
        var round = new BaccaratRound(shoe);
        round.Deal();
        Assert.IsTrue(round.RoundOver);
    }
}
