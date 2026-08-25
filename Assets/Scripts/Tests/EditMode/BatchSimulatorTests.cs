using System.Collections.Generic;
using NUnit.Framework;

public class BatchSimulatorTests
{
    [Test]
    public void RunsExactlyNSpins_WhenAffordable()
    {
        var bankroll = new Bankroll(1_000_000);
        var strategy = new List<Bet> { new Bet(BetType.Red, 25) };
        var history = BatchSimulator.Run(bankroll, strategy, new SpinResultGenerator(new SystemRandomSource(1)), 1000);

        Assert.AreEqual(1000, history.SpinCount);
    }

    [Test]
    public void BalanceDeltasAreInternallyConsistent()
    {
        var bankroll = new Bankroll(10000);
        var strategy = new List<Bet> { new Bet(BetType.Red, 25) };
        var history = BatchSimulator.Run(bankroll, strategy, new SpinResultGenerator(new SystemRandomSource(7)), 200);

        long expectedBalance = 10000;
        foreach (var record in history.Records)
        {
            expectedBalance += record.NetChange;
            Assert.AreEqual(expectedBalance, record.BalanceAfter);
        }
        Assert.AreEqual(bankroll.Balance, expectedBalance);
    }

    [Test]
    public void StopsEarly_WhenBankrollCannotAffordNextRound()
    {
        var bankroll = new Bankroll(60); // enough for exactly 2 rounds of 25, not more once losses accrue
        var strategy = new List<Bet> { Bet.Straight(17, 25) }; // near-certain repeated loss
        var history = BatchSimulator.Run(bankroll, strategy, new SpinResultGenerator(new SystemRandomSource(99)), 10000);

        Assert.Less(history.SpinCount, 10000);
        Assert.GreaterOrEqual(bankroll.Balance, 0);
    }

    [Test]
    public void LongRunFlatRedBet_TrendsTowardHouseEdge()
    {
        var bankroll = new Bankroll(10_000_000);
        var strategy = new List<Bet> { new Bet(BetType.Red, 100) };
        var history = BatchSimulator.Run(bankroll, strategy, new SpinResultGenerator(new SystemRandomSource(555)), 200_000);

        long totalStaked = history.SpinCount * 100L;
        double actualEdge = -(double)(bankroll.Balance - 10_000_000) / totalStaked;

        // European single-zero house edge is 1/37 ≈ 2.70%. Generous tolerance band —
        // this is a regression guard against a scope/payout bug, not a precision test.
        Assert.That(actualEdge, Is.InRange(0.0, 0.06));
    }
}
