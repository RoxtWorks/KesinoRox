// One round of Three Pictures: up to MaxHands player hands (boxes) against one dealer hand.
// Only boxes with a bet are dealt. Deal order, like a real table: card 1 to every active box
// left to right then the dealer, then card 2, then card 3. Never touches Bankroll directly.
public class ThreePicturesRound
{
    public const int MaxHands = 5;

    readonly long[] bets = new long[MaxHands];
    readonly bool shuffleEachRound;

    public Shoe Shoe { get; }

    // shuffleEachRound: single-deck table reshuffles before every deal. Tests pass an ordered shoe and false.
    public ThreePicturesRound(Shoe shoe, bool shuffleEachRound = true)
    {
        Shoe = shoe;
        this.shuffleEachRound = shuffleEachRound;
    }

    public long GetBet(int box) => bets[box];
    public void PlaceBet(int box, long amount) => bets[box] += amount;
    public void ClearBet(int box) => bets[box] = 0;
    public void ClearAllBets() { for (int i = 0; i < MaxHands; i++) bets[i] = 0; }

    public long TotalOnTable()
    {
        long sum = 0;
        foreach (long b in bets) sum += b;
        return sum;
    }

    public ThreePicturesRoundResult Deal()
    {
        if (shuffleEachRound) Shoe.Shuffle();

        var result = new ThreePicturesRoundResult { Dealer = new ThreePicturesHand() };
        for (int box = 0; box < MaxHands; box++)
            if (bets[box] > 0)
                result.Boxes[box] = new ThreePicturesBoxResult { Box = box, Stake = bets[box], Hand = new ThreePicturesHand() };

        for (int card = 0; card < 3; card++)
        {
            foreach (var b in result.Boxes) b?.Hand.AddCard(Shoe.Draw());
            result.Dealer.AddCard(Shoe.Draw());
        }

        foreach (var b in result.Boxes)
        {
            if (b == null) continue;
            b.Outcome = ThreePicturesResolver.Resolve(b.Hand, result.Dealer);
            b.HalfPay = ThreePicturesResolver.IsHalfPayWin(b.Hand, b.Outcome);
            b.Return = ThreePicturesResolver.Payout(b.Stake, b.Hand, b.Outcome);
        }

        ClearAllBets();
        return result;
    }
}

public class ThreePicturesBoxResult
{
    public int Box;
    public long Stake;
    public ThreePicturesHand Hand;
    public ThreePicturesOutcome Outcome;
    public bool HalfPay;   // won with a point of 6 → paid 1:2
    public long Return;    // stake + winnings, 0 on loss
    public long Net => Return - Stake;
}

public class ThreePicturesRoundResult
{
    public ThreePicturesHand Dealer;
    // Indexed by box; null where no bet was placed
    public readonly ThreePicturesBoxResult[] Boxes = new ThreePicturesBoxResult[ThreePicturesRound.MaxHands];

    public long TotalStaked
    {
        get { long s = 0; foreach (var b in Boxes) if (b != null) s += b.Stake; return s; }
    }

    public long TotalReturned
    {
        get { long s = 0; foreach (var b in Boxes) if (b != null) s += b.Return; return s; }
    }
}
