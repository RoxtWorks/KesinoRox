public class Bankroll
{
    public long Balance { get; private set; }
    public long StartingBalance { get; private set; }
    // Every dollar the player has put in this session (starting balance + every
    // top-up) — distinct from Balance, which also moves on bet wins/losses. Net
    // profit has to compare against this, not StartingBalance alone, or topping up
    // funds would misread as winnings.
    public long TotalFunded { get; private set; }

    public Bankroll(long startingBalance)
    {
        StartingBalance = startingBalance;
        Balance = startingBalance;
        TotalFunded = startingBalance;
    }

    public void Reset(long startingBalance)
    {
        StartingBalance = startingBalance;
        Balance = startingBalance;
        TotalFunded = startingBalance;
    }

    // Restores an arbitrary prior state (loading a save) where Balance, Starting
    // and TotalFunded aren't all equal — Reset() can't do this since it always
    // sets all three to the same value for a fresh session start.
    public void LoadState(long balance, long startingBalance, long totalFunded)
    {
        Balance = balance;
        StartingBalance = startingBalance;
        TotalFunded = totalFunded;
    }

    public bool CanAfford(long amount) => amount >= 0 && amount <= Balance;

    // Returns false and leaves balance untouched if funds are insufficient —
    // the caller (bet placement UI, batch simulator) must check this before
    // committing a bet rather than letting balance go negative.
    public bool TryWithdraw(long amount)
    {
        if (!CanAfford(amount)) return false;
        Balance -= amount;
        return true;
    }

    // Bet payouts — money returned by a spin. Doesn't count as new funding, so it
    // doesn't touch TotalFunded; this is exactly the money NetProfit should reflect.
    public void Deposit(long amount)
    {
        if (amount <= 0) return;
        Balance += amount;
    }

    // A top-up from the player (the HUD's ADD FUNDS button) — real new money in,
    // not winnings, so it raises TotalFunded too and must NOT show up as profit.
    public void AddFunds(long amount)
    {
        if (amount <= 0) return;
        Balance += amount;
        TotalFunded += amount;
    }

    public long NetProfit => Balance - TotalFunded;
    public bool IsBusted => Balance <= 0;
}
