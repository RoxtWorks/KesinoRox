// Stateful orchestrator for one round. Unlike BlackjackRound, baccarat has zero
// player decisions mid-hand — the third-card draw rules are fully automatic — so
// Deal() draws both hands, applies those rules, and resolves the round in one
// synchronous step instead of a multi-turn state machine.
public class BaccaratRound
{
    public Shoe Shoe { get; }
    public BaccaratHand Player { get; private set; }
    public BaccaratHand Banker { get; private set; }
    public BaccaratOutcome Outcome { get; private set; }
    public bool RoundOver { get; private set; }

    public BaccaratRound(Shoe shoe)
    {
        Shoe = shoe;
    }

    public void Deal()
    {
        // Reshuffle happens here, between rounds, never mid-hand.
        if (Shoe.NeedsReshuffle) Shoe.Shuffle();

        Player = new BaccaratHand();
        Banker = new BaccaratHand();
        RoundOver = false;

        Player.AddCard(Shoe.Draw());
        Banker.AddCard(Shoe.Draw());
        Player.AddCard(Shoe.Draw());
        Banker.AddCard(Shoe.Draw());

        if (!Player.IsNatural && !Banker.IsNatural)
        {
            bool playerDrew = false;
            int playerThirdValue = 0;
            if (BaccaratResolver.PlayerDraws(Player))
            {
                var card = Shoe.Draw();
                Player.AddCard(card);
                playerDrew = true;
                playerThirdValue = BaccaratResolver.CardValue(card);
            }

            if (BaccaratResolver.BankerDraws(Banker, playerDrew, playerThirdValue))
                Banker.AddCard(Shoe.Draw());
        }

        Outcome = BaccaratResolver.Resolve(Player, Banker);
        RoundOver = true;
    }
}
