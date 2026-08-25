using NUnit.Framework;

public class BankrollTests
{
    [Test]
    public void StartsAtGivenBalance()
    {
        var bankroll = new Bankroll(1000);
        Assert.AreEqual(1000, bankroll.Balance);
        Assert.AreEqual(1000, bankroll.StartingBalance);
    }

    [Test]
    public void Withdraw_SucceedsWhenAffordable()
    {
        var bankroll = new Bankroll(1000);
        Assert.IsTrue(bankroll.TryWithdraw(400));
        Assert.AreEqual(600, bankroll.Balance);
    }

    [Test]
    public void Withdraw_FailsAndLeavesBalanceUntouchedWhenInsufficient()
    {
        var bankroll = new Bankroll(100);
        Assert.IsFalse(bankroll.TryWithdraw(101));
        Assert.AreEqual(100, bankroll.Balance);
    }

    [Test]
    public void Deposit_IncreasesBalance()
    {
        var bankroll = new Bankroll(100);
        bankroll.Deposit(50);
        Assert.AreEqual(150, bankroll.Balance);
    }

    [Test]
    public void Busted_WhenBalanceHitsZero()
    {
        var bankroll = new Bankroll(50);
        bankroll.TryWithdraw(50);
        Assert.AreEqual(0, bankroll.Balance);
        Assert.IsTrue(bankroll.IsBusted);
    }

    [Test]
    public void NetProfit_ReflectsChangeFromStart()
    {
        var bankroll = new Bankroll(500);
        bankroll.TryWithdraw(100);
        bankroll.Deposit(300);
        Assert.AreEqual(700, bankroll.Balance);
        Assert.AreEqual(200, bankroll.NetProfit);
    }

    [Test]
    public void LoadState_RestoresArbitraryPriorSessionExactly()
    {
        var bankroll = new Bankroll(1000);
        bankroll.LoadState(balance: 1850, startingBalance: 1000, totalFunded: 6000);
        Assert.AreEqual(1850, bankroll.Balance);
        Assert.AreEqual(1000, bankroll.StartingBalance);
        Assert.AreEqual(6000, bankroll.TotalFunded);
        Assert.AreEqual(-4150, bankroll.NetProfit);
    }
}
