using NUnit.Framework;

public class BetResolverTests
{
    [Test]
    public void Straight_Win_Pays35To1()
    {
        var bet = Bet.Straight(17, 25);
        Assert.AreEqual(25 + 25 * 35, BetResolver.Resolve(bet, 17));
    }

    [Test]
    public void Straight_Loss_PaysZero()
    {
        var bet = Bet.Straight(17, 25);
        Assert.AreEqual(0, BetResolver.Resolve(bet, 18));
    }

    [Test]
    public void Red_WinsOnRedNumbers_LosesOnBlackAndZero()
    {
        var bet = new Bet(BetType.Red, 25);
        Assert.AreEqual(25 + 25, BetResolver.Resolve(bet, 1));  // red
        Assert.AreEqual(0, BetResolver.Resolve(bet, 2));        // black
        Assert.AreEqual(0, BetResolver.Resolve(bet, 0));        // green
    }

    [Test]
    public void Black_WinsOnBlackNumbers_LosesOnRedAndZero()
    {
        var bet = new Bet(BetType.Black, 25);
        Assert.AreEqual(50, BetResolver.Resolve(bet, 2));
        Assert.AreEqual(0, BetResolver.Resolve(bet, 1));
        Assert.AreEqual(0, BetResolver.Resolve(bet, 0));
    }

    [Test]
    public void OddEven_ExcludeZero()
    {
        Assert.IsFalse(BetResolver.IsWin(new Bet(BetType.Odd, 25), 0));
        Assert.IsFalse(BetResolver.IsWin(new Bet(BetType.Even, 25), 0));
        Assert.IsTrue(BetResolver.IsWin(new Bet(BetType.Odd, 25), 3));
        Assert.IsTrue(BetResolver.IsWin(new Bet(BetType.Even, 25), 4));
    }

    [Test]
    public void LowHigh_Boundaries()
    {
        var low = new Bet(BetType.Low1to18, 25);
        var high = new Bet(BetType.High19to36, 25);
        Assert.IsTrue(BetResolver.IsWin(low, 1));
        Assert.IsTrue(BetResolver.IsWin(low, 18));
        Assert.IsFalse(BetResolver.IsWin(low, 19));
        Assert.IsFalse(BetResolver.IsWin(low, 0));
        Assert.IsTrue(BetResolver.IsWin(high, 19));
        Assert.IsTrue(BetResolver.IsWin(high, 36));
        Assert.IsFalse(BetResolver.IsWin(high, 18));
    }

    [Test]
    public void Dozens_BucketCorrectlyAndPay2To1()
    {
        Assert.IsTrue(BetResolver.IsWin(new Bet(BetType.Dozen1, 100), 1));
        Assert.IsTrue(BetResolver.IsWin(new Bet(BetType.Dozen1, 100), 12));
        Assert.IsFalse(BetResolver.IsWin(new Bet(BetType.Dozen1, 100), 13));
        Assert.IsTrue(BetResolver.IsWin(new Bet(BetType.Dozen2, 100), 13));
        Assert.IsTrue(BetResolver.IsWin(new Bet(BetType.Dozen2, 100), 24));
        Assert.IsTrue(BetResolver.IsWin(new Bet(BetType.Dozen3, 100), 25));
        Assert.IsTrue(BetResolver.IsWin(new Bet(BetType.Dozen3, 100), 36));
        Assert.IsFalse(BetResolver.IsWin(new Bet(BetType.Dozen1, 100), 0));

        Assert.AreEqual(300, BetResolver.Resolve(new Bet(BetType.Dozen1, 100), 5));
    }

    [Test]
    public void Split_WinsOnEitherNumber_Pays17To1()
    {
        var bet = new Bet(BetType.Split, 25, new[] { 5, 6 });
        Assert.AreEqual(25 + 25 * 17, BetResolver.Resolve(bet, 5));
        Assert.AreEqual(25 + 25 * 17, BetResolver.Resolve(bet, 6));
        Assert.AreEqual(0, BetResolver.Resolve(bet, 4));
    }

    [Test]
    public void Street_WinsOnAnyOfThree_Pays11To1()
    {
        var bet = new Bet(BetType.Street, 25, new[] { 1, 2, 3 });
        Assert.AreEqual(25 + 25 * 11, BetResolver.Resolve(bet, 2));
        Assert.AreEqual(0, BetResolver.Resolve(bet, 4));
    }

    [Test]
    public void Corner_WinsOnAnyOfFour_Pays8To1()
    {
        var bet = new Bet(BetType.Corner, 25, new[] { 1, 2, 4, 5 });
        Assert.AreEqual(25 + 25 * 8, BetResolver.Resolve(bet, 4));
        Assert.AreEqual(0, BetResolver.Resolve(bet, 3));
    }

    [Test]
    public void SixLine_WinsOnAnyOfSix_Pays5To1()
    {
        var bet = new Bet(BetType.SixLine, 25, new[] { 1, 2, 3, 4, 5, 6 });
        Assert.AreEqual(25 + 25 * 5, BetResolver.Resolve(bet, 6));
        Assert.AreEqual(0, BetResolver.Resolve(bet, 7));
    }

    [Test]
    public void Columns_BucketByModulo3()
    {
        // Column1 = numbers ≡ 1 mod 3 (1,4,7,...,34), Column2 ≡ 2 mod 3, Column3 ≡ 0 mod 3
        Assert.IsTrue(BetResolver.IsWin(new Bet(BetType.Column1, 25), 1));
        Assert.IsTrue(BetResolver.IsWin(new Bet(BetType.Column1, 25), 34));
        Assert.IsTrue(BetResolver.IsWin(new Bet(BetType.Column2, 25), 2));
        Assert.IsTrue(BetResolver.IsWin(new Bet(BetType.Column3, 25), 3));
        Assert.IsTrue(BetResolver.IsWin(new Bet(BetType.Column3, 25), 36));
        Assert.IsFalse(BetResolver.IsWin(new Bet(BetType.Column1, 25), 0));
        Assert.IsFalse(BetResolver.IsWin(new Bet(BetType.Column2, 25), 0));
        Assert.IsFalse(BetResolver.IsWin(new Bet(BetType.Column3, 25), 0));
    }
}
