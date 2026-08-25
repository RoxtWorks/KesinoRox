using System.Collections.Generic;
using System.Linq;

// Runs many spins synchronously against a fixed, repeated bet set (a "flat strategy") —
// no animation, no frame ticks, just the odds. This is the actual point of the tool:
// testing a betting strategy's bankroll behaviour over volume in well under a second.
public static class BatchSimulator
{
    public static SessionHistory Run(Bankroll bankroll, IReadOnlyList<Bet> strategy, SpinResultGenerator generator, int spinCount)
    {
        var history = new SessionHistory();
        long totalStake = strategy.Sum(b => b.Amount);

        for (int i = 0; i < spinCount; i++)
        {
            if (!bankroll.TryWithdraw(totalStake)) break; // can't afford another round, stop early

            int winningNumber = generator.Spin();
            long totalReturned = strategy.Sum(b => BetResolver.Resolve(b, winningNumber));
            bankroll.Deposit(totalReturned);

            history.Add(new SpinRecord(i, winningNumber, totalStake, totalReturned, bankroll.Balance));
        }

        return history;
    }
}
