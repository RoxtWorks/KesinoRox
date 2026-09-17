using System.Collections.Generic;

// One round of Three Pictures (Royal Three Pictures / 3 Kings).
// Auto-deals 3 cards to each side, resolves in one synchronous step —
// no player mid-hand decisions (same structure as BaccaratRound).
public class ThreePicturesRound
{
    readonly Dictionary<ThreePicturesBetType, long> bets = new Dictionary<ThreePicturesBetType, long>();

    public Shoe Shoe { get; }
    public ThreePicturesHand Player  { get; private set; }
    public ThreePicturesHand Dealer  { get; private set; }
    public ThreePicturesOutcome Outcome { get; private set; }
    public bool RoundOver { get; private set; }

    public ThreePicturesRound(Shoe shoe) { Shoe = shoe; }

    public long GetBet(ThreePicturesBetType type) => bets.TryGetValue(type, out var v) ? v : 0;
    public void PlaceBet(ThreePicturesBetType type, long amount) => bets[type] = GetBet(type) + amount;
    public void ClearBet(ThreePicturesBetType type) => bets[type] = 0;

    public ThreePicturesRoundResult Deal()
    {
        if (Shoe.NeedsReshuffle) Shoe.Shuffle();

        Player = new ThreePicturesHand();
        Dealer = new ThreePicturesHand();
        RoundOver = false;

        // Alternate deal: P-D-P-D-P-D (same as baccarat convention)
        Player.AddCard(Shoe.Draw());
        Dealer.AddCard(Shoe.Draw());
        Player.AddCard(Shoe.Draw());
        Dealer.AddCard(Shoe.Draw());
        Player.AddCard(Shoe.Draw());
        Dealer.AddCard(Shoe.Draw());

        Outcome = ThreePicturesResolver.Resolve(Player, Dealer);
        RoundOver = true;

        long mainBet   = GetBet(ThreePicturesBetType.Main);
        long royalBet  = GetBet(ThreePicturesBetType.RoyalBonus);

        var result = new ThreePicturesRoundResult
        {
            Outcome       = Outcome,
            PlayerHand    = Player,
            DealerHand    = Dealer,
            MainReturn    = mainBet  > 0 ? ThreePicturesResolver.MainPayout(mainBet, Outcome)         : 0,
            RoyalReturn   = royalBet > 0 ? ThreePicturesResolver.RoyalBonusPayout(royalBet, Player)   : 0
        };

        bets[ThreePicturesBetType.Main]       = 0;
        bets[ThreePicturesBetType.RoyalBonus] = 0;

        return result;
    }
}

public class ThreePicturesRoundResult
{
    public ThreePicturesOutcome  Outcome    { get; set; }
    public ThreePicturesHand     PlayerHand { get; set; }
    public ThreePicturesHand     DealerHand { get; set; }
    public long MainReturn  { get; set; }
    public long RoyalReturn { get; set; }
    public long TotalReturned => MainReturn + RoyalReturn;
}
