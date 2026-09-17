using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public class ThreePicturesTests
{
    static Card C(Rank r, Suit s = Suit.Spades) => new Card(r, s);

    static ThreePicturesHand Hand(params Rank[] ranks)
    {
        var h = new ThreePicturesHand();
        foreach (var r in ranks) h.AddCard(C(r));
        return h;
    }

    // ── Hand values ────────────────────────────────────────────────────────
    [Test] public void Point_Drops_Tens_Digit() =>
        Assert.AreEqual(9, Hand(Rank.Nine, Rank.Queen, Rank.Queen).Point);

    [Test] public void Ace_Counts_One_Tens_And_Pictures_Count_Zero() =>
        Assert.AreEqual(1, Hand(Rank.Ace, Rank.Ten, Rank.King).Point);

    [Test] public void Ten_Is_Not_A_Picture() =>
        Assert.AreEqual(1, Hand(Rank.Jack, Rank.Ten, Rank.Nine).PictureCount);

    [Test] public void Three_Pictures_Is_Royal() =>
        Assert.IsTrue(Hand(Rank.King, Rank.Queen, Rank.Jack).IsRoyal);

    [Test] public void Two_Pictures_And_Ten_Is_Not_Royal() =>
        Assert.IsFalse(Hand(Rank.King, Rank.Queen, Rank.Ten).IsRoyal);

    // ── Ranking ────────────────────────────────────────────────────────────
    [Test] public void Three_Pictures_Beats_Point_Nine() =>
        Assert.AreEqual(ThreePicturesOutcome.PlayerWins,
            ThreePicturesResolver.Resolve(Hand(Rank.Jack, Rank.Jack, Rank.King), Hand(Rank.Nine, Rank.Queen, Rank.King)));

    [Test] public void Dealer_Three_Pictures_Beats_Player_Point() =>
        Assert.AreEqual(ThreePicturesOutcome.DealerWins,
            ThreePicturesResolver.Resolve(Hand(Rank.Nine, Rank.Ten, Rank.Ten), Hand(Rank.Queen, Rank.Queen, Rank.Queen)));

    [Test] public void Both_Three_Pictures_Tie() =>
        Assert.AreEqual(ThreePicturesOutcome.Tie,
            ThreePicturesResolver.Resolve(Hand(Rank.King, Rank.King, Rank.King), Hand(Rank.Jack, Rank.Queen, Rank.King)));

    [Test] public void Higher_Point_Wins() =>
        Assert.AreEqual(ThreePicturesOutcome.PlayerWins,
            ThreePicturesResolver.Resolve(Hand(Rank.Four, Rank.Four, Rank.Ten), Hand(Rank.Seven, Rank.Ten, Rank.Ten)));

    // Marcus's example: Q-Q-9 (9, two pictures) beats J-10-9 (9, one picture)
    [Test] public void Equal_Points_More_Pictures_Wins() =>
        Assert.AreEqual(ThreePicturesOutcome.PlayerWins,
            ThreePicturesResolver.Resolve(Hand(Rank.Queen, Rank.Queen, Rank.Nine), Hand(Rank.Jack, Rank.Ten, Rank.Nine)));

    [Test] public void Equal_Points_Fewer_Pictures_Loses() =>
        Assert.AreEqual(ThreePicturesOutcome.DealerWins,
            ThreePicturesResolver.Resolve(Hand(Rank.Jack, Rank.Ten, Rank.Nine), Hand(Rank.Queen, Rank.Queen, Rank.Nine)));

    [Test] public void Equal_Points_Equal_Pictures_Tie() =>
        Assert.AreEqual(ThreePicturesOutcome.Tie,
            ThreePicturesResolver.Resolve(Hand(Rank.King, Rank.Four, Rank.Three), Hand(Rank.Jack, Rank.Five, Rank.Two)));

    // ── Payouts ────────────────────────────────────────────────────────────
    [Test] public void Win_Pays_One_To_One() =>
        Assert.AreEqual(200, ThreePicturesResolver.Payout(100, Hand(Rank.Nine, Rank.Ten, Rank.Ten), ThreePicturesOutcome.PlayerWins));

    [Test] public void Win_With_Six_Pays_One_To_Two() =>
        Assert.AreEqual(150, ThreePicturesResolver.Payout(100, Hand(Rank.Six, Rank.Ten, Rank.King), ThreePicturesOutcome.PlayerWins));

    [Test] public void Win_With_Six_Half_Chip_Rounds_Down() =>
        Assert.AreEqual(37, ThreePicturesResolver.Payout(25, Hand(Rank.Two, Rank.Four, Rank.Queen), ThreePicturesOutcome.PlayerWins));

    [Test] public void Three_Pictures_Win_Pays_Full() =>
        Assert.AreEqual(200, ThreePicturesResolver.Payout(100, Hand(Rank.Jack, Rank.Queen, Rank.King), ThreePicturesOutcome.PlayerWins));

    [Test] public void Tie_Is_A_Push() =>
        Assert.AreEqual(100, ThreePicturesResolver.Payout(100, Hand(Rank.Six, Rank.Ten, Rank.King), ThreePicturesOutcome.Tie));

    [Test] public void Loss_Returns_Zero() =>
        Assert.AreEqual(0, ThreePicturesResolver.Payout(100, Hand(Rank.Nine, Rank.Ten, Rank.Ten), ThreePicturesOutcome.DealerWins));

    // ── Round ──────────────────────────────────────────────────────────────
    [Test]
    public void Only_Bet_Boxes_Are_Dealt_Card_By_Card_Then_Dealer()
    {
        // Boxes 1 and 3 bet. Order: B1, B3, D, B1, B3, D, B1, B3, D
        var shoe = new Shoe(new List<Card>
        {
            C(Rank.Nine), C(Rank.King), C(Rank.Two),
            C(Rank.Ten),  C(Rank.Queen), C(Rank.Three),
            C(Rank.Ten),  C(Rank.Jack), C(Rank.Two)
        });
        var round = new ThreePicturesRound(shoe, shuffleEachRound: false);
        round.PlaceBet(1, 100);
        round.PlaceBet(3, 50);

        var r = round.Deal();

        Assert.IsNull(r.Boxes[0]); Assert.IsNull(r.Boxes[2]); Assert.IsNull(r.Boxes[4]);
        Assert.AreEqual(9, r.Boxes[1].Hand.Point);
        Assert.IsTrue(r.Boxes[3].Hand.IsRoyal);
        Assert.AreEqual(7, r.Dealer.Point);
        Assert.AreEqual(ThreePicturesOutcome.PlayerWins, r.Boxes[1].Outcome);
        Assert.AreEqual(ThreePicturesOutcome.PlayerWins, r.Boxes[3].Outcome);
        Assert.AreEqual(150, r.TotalStaked);
        Assert.AreEqual(300, r.TotalReturned);
        Assert.AreEqual(0, round.TotalOnTable());
    }

    [Test]
    public void Five_Hands_Settle_Independently()
    {
        // Dealer: 6 with one picture (Six, Ten, King)
        var cards = new List<Card>();
        Rank[][] boxes =
        {
            new[] { Rank.Nine, Rank.Ten, Rank.Ten },    // 9 → win 1:1
            new[] { Rank.Six, Rank.Queen, Rank.King },  // 6, 2 pics vs dealer 6, 1 pic → win on 6, pays 1:2
            new[] { Rank.Six, Rank.Ten, Rank.Jack },    // 6, 1 pic → tie
            new[] { Rank.Two, Rank.Two, Rank.Ten },     // 4 → loss
            new[] { Rank.Jack, Rank.Jack, Rank.Queen }  // three pictures → win 1:1
        };
        Rank[] dealer = { Rank.Six, Rank.Ten, Rank.King };
        for (int card = 0; card < 3; card++)
        {
            foreach (var b in boxes) cards.Add(C(b[card]));
            cards.Add(C(dealer[card]));
        }
        var round = new ThreePicturesRound(new Shoe(cards), shuffleEachRound: false);
        for (int i = 0; i < 5; i++) round.PlaceBet(i, 100);

        var r = round.Deal();

        Assert.AreEqual(200, r.Boxes[0].Return);
        Assert.IsTrue(r.Boxes[1].HalfPay);
        Assert.AreEqual(150, r.Boxes[1].Return);
        Assert.AreEqual(ThreePicturesOutcome.Tie, r.Boxes[2].Outcome);
        Assert.AreEqual(100, r.Boxes[2].Return);
        Assert.AreEqual(0, r.Boxes[3].Return);
        Assert.AreEqual(200, r.Boxes[4].Return);
        Assert.AreEqual(500, r.TotalStaked);
        Assert.AreEqual(650, r.TotalReturned);
    }
}
