using System.Collections.Generic;

// One round of Three Card Poker.
// Flow: PlaceBet (Ante required, PairPlus optional) → Deal() → Play() (matches the Ante) or Fold().
// Folding forfeits the Ante; Pair Plus is settled on the player's cards either way.
public class ThreeCardPokerRound
{
    readonly Dictionary<ThreeCardPokerBetType, long> bets =
        new Dictionary<ThreeCardPokerBetType, long>();

    public Shoe Shoe { get; }
    public ThreeCardPokerHand PlayerHand { get; private set; }
    public ThreeCardPokerHand DealerHand { get; private set; }
    public ThreeCardPokerOutcome Outcome { get; private set; }
    public bool RoundOver { get; private set; }
    public bool PlayerFolded { get; private set; }

    readonly bool shuffleEachHand;

    // shuffleEachHand: single-deck table reshuffles before every hand. Tests pass an ordered shoe and false.
    public ThreeCardPokerRound(Shoe shoe, bool shuffleEachHand = true)
    {
        Shoe = shoe;
        this.shuffleEachHand = shuffleEachHand;
    }

    public long GetBet(ThreeCardPokerBetType type) =>
        bets.TryGetValue(type, out var v) ? v : 0;
    public void PlaceBet(ThreeCardPokerBetType type, long amount) =>
        bets[type] = GetBet(type) + amount;
    public void ClearBet(ThreeCardPokerBetType type) => bets[type] = 0;
    public void ClearAllBets() => bets.Clear();
    public long TotalOnTable() => GetBet(ThreeCardPokerBetType.Ante) + GetBet(ThreeCardPokerBetType.Play) + GetBet(ThreeCardPokerBetType.PairPlus);

    // Deal 3 cards to each side. Call before any Play/Fold decision.
    public void Deal()
    {
        if (shuffleEachHand || Shoe.NeedsReshuffle) Shoe.Shuffle();

        PlayerHand  = new ThreeCardPokerHand();
        DealerHand  = new ThreeCardPokerHand();
        PlayerFolded = false;
        RoundOver   = false;

        // Standard deal: P-D-P-D-P-D
        PlayerHand.AddCard(Shoe.Draw());
        DealerHand.AddCard(Shoe.Draw());
        PlayerHand.AddCard(Shoe.Draw());
        DealerHand.AddCard(Shoe.Draw());
        PlayerHand.AddCard(Shoe.Draw());
        DealerHand.AddCard(Shoe.Draw());
    }

    // Player places Play bet (must equal Ante) and hand is resolved.
    public ThreeCardPokerRoundResult Play()
    {
        long anteBet = GetBet(ThreeCardPokerBetType.Ante);
        PlaceBet(ThreeCardPokerBetType.Play, anteBet); // auto-match Ante
        return Resolve(folded: false);
    }

    // Player folds: forfeits Ante (and Play if already placed).
    // PairPlus still pays out.
    public ThreeCardPokerRoundResult Fold()
    {
        PlayerFolded = true;
        return Resolve(folded: true);
    }

    ThreeCardPokerRoundResult Resolve(bool folded)
    {
        Outcome  = ThreeCardPokerResolver.Resolve(PlayerHand, DealerHand);
        RoundOver = true;

        long anteBet   = GetBet(ThreeCardPokerBetType.Ante);
        long playBet   = GetBet(ThreeCardPokerBetType.Play);
        long ppBet     = GetBet(ThreeCardPokerBetType.PairPlus);

        long anteReturn  = folded ? 0 : ThreeCardPokerResolver.AntePayout(anteBet, Outcome);
        long playReturn  = folded ? 0 : ThreeCardPokerResolver.PlayPayout(playBet, Outcome);
        long bonusReturn = folded ? 0 : ThreeCardPokerResolver.AnteBonusPayout(anteBet, PlayerHand);
        long ppReturn    = ppBet > 0   ? ThreeCardPokerResolver.PairPlusPayout(ppBet, PlayerHand) : 0;

        long totalStaked   = anteBet + playBet + ppBet;
        long totalReturned = anteReturn + playReturn + bonusReturn + ppReturn;

        var result = new ThreeCardPokerRoundResult
        {
            Outcome        = Outcome,
            PlayerHand     = PlayerHand,
            DealerHand     = DealerHand,
            DealerQualified= DealerHand.DealerQualifies,
            PlayerFolded   = folded,
            AnteReturn     = anteReturn,
            PlayReturn     = playReturn,
            AnteBonusReturn= bonusReturn,
            PairPlusReturn = ppReturn,
            TotalStaked    = totalStaked,
            TotalReturned  = totalReturned
        };

        bets[ThreeCardPokerBetType.Ante]     = 0;
        bets[ThreeCardPokerBetType.Play]      = 0;
        bets[ThreeCardPokerBetType.PairPlus]  = 0;

        return result;
    }
}

public class ThreeCardPokerRoundResult
{
    public ThreeCardPokerOutcome Outcome         { get; set; }
    public ThreeCardPokerHand    PlayerHand      { get; set; }
    public ThreeCardPokerHand    DealerHand      { get; set; }
    public bool  DealerQualified  { get; set; }
    public bool  PlayerFolded     { get; set; }
    public long  AnteReturn       { get; set; }
    public long  PlayReturn       { get; set; }
    public long  AnteBonusReturn  { get; set; }
    public long  PairPlusReturn   { get; set; }
    public long  TotalStaked      { get; set; }
    public long  TotalReturned    { get; set; }
    public long  NetChange        => TotalReturned - TotalStaked;
}
