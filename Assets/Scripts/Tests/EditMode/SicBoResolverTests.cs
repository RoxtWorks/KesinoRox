using NUnit.Framework;

[TestFixture]
public class SicBoResolverTests
{
    // ── Big / Small ────────────────────────────────────────────────────────
    [Test] public void Big_Wins_On_11() =>
        Assert.AreEqual(200, SicBoResolver.BigSmallPayout(100, 11, false, true));

    [Test] public void Big_Wins_On_17() =>
        Assert.AreEqual(200, SicBoResolver.BigSmallPayout(100, 17, false, true));

    [Test] public void Small_Wins_On_4() =>
        Assert.AreEqual(200, SicBoResolver.BigSmallPayout(100, 4, false, false));

    [Test] public void Small_Wins_On_10() =>
        Assert.AreEqual(200, SicBoResolver.BigSmallPayout(100, 10, false, false));

    [Test] public void Big_Loses_On_Triple() =>
        Assert.AreEqual(0, SicBoResolver.BigSmallPayout(100, 12, true, true));

    [Test] public void Small_Loses_On_Triple() =>
        Assert.AreEqual(0, SicBoResolver.BigSmallPayout(100, 9, true, false));

    [Test] public void Big_Loses_On_Small_Total() =>
        Assert.AreEqual(0, SicBoResolver.BigSmallPayout(100, 8, false, true));

    // ── Odd / Even ─────────────────────────────────────────────────────────
    [Test] public void Odd_Wins_On_Odd_Total() =>
        Assert.AreEqual(200, SicBoResolver.Payout(SicBoBetType.Odd, 100, 1, 2, 4));

    [Test] public void Even_Wins_On_Even_Total() =>
        Assert.AreEqual(200, SicBoResolver.Payout(SicBoBetType.Even, 100, 1, 2, 5));

    [Test] public void Odd_Loses_On_Even_Total() =>
        Assert.AreEqual(0, SicBoResolver.Payout(SicBoBetType.Odd, 100, 1, 2, 5));

    [Test] public void Even_Loses_On_Triple() =>
        Assert.AreEqual(0, SicBoResolver.Payout(SicBoBetType.Even, 100, 2, 2, 2));

    // ── Totals ─────────────────────────────────────────────────────────────
    [Test] public void Total4_Pays_50to1() =>
        Assert.AreEqual(5100, SicBoResolver.TotalPayout(100, 4, 4));

    [Test] public void Total17_Pays_50to1() =>
        Assert.AreEqual(5100, SicBoResolver.TotalPayout(100, 17, 17));

    [Test] public void Total5_Pays_30to1() =>
        Assert.AreEqual(3100, SicBoResolver.TotalPayout(100, 5, 5));

    [Test] public void Total15_Pays_18to1() =>
        Assert.AreEqual(1900, SicBoResolver.TotalPayout(100, 15, 15));

    [Test] public void Total10_Pays_6to1() =>
        Assert.AreEqual(700, SicBoResolver.TotalPayout(100, 10, 10));

    [Test] public void Total_Loses_On_Wrong_Roll() =>
        Assert.AreEqual(0, SicBoResolver.TotalPayout(100, 7, 9));

    // ── Singles ────────────────────────────────────────────────────────────
    [Test] public void Single_One_Match_Pays_1to1() =>
        Assert.AreEqual(200, SicBoResolver.SinglePayout(100, 3, 3, 1, 2));

    [Test] public void Single_Two_Matches_Pays_2to1() =>
        Assert.AreEqual(300, SicBoResolver.SinglePayout(100, 5, 5, 5, 2));

    [Test] public void Single_Three_Matches_Pays_12to1() =>
        Assert.AreEqual(1300, SicBoResolver.SinglePayout(100, 6, 6, 6, 6));

    [Test] public void Single_No_Match_Returns_Zero() =>
        Assert.AreEqual(0, SicBoResolver.SinglePayout(100, 4, 1, 2, 3));

    // ── Doubles (Pairs) ────────────────────────────────────────────────────
    [Test] public void Double_Two_Matching_Pays_8to1() =>
        Assert.AreEqual(900, SicBoResolver.DoublePayout(100, 3, 3, 3, 1));

    [Test] public void Double_Three_Matching_Also_Pays_8to1() =>
        Assert.AreEqual(900, SicBoResolver.DoublePayout(100, 2, 2, 2, 2));

    [Test] public void Double_One_Match_Returns_Zero() =>
        Assert.AreEqual(0, SicBoResolver.DoublePayout(100, 5, 5, 1, 2));

    // ── Combos ─────────────────────────────────────────────────────────────
    [Test] public void Combo_Both_Faces_Present_Pays_5to1() =>
        Assert.AreEqual(600, SicBoResolver.ComboPayout(100, 1, 2, 1, 2, 3));

    [Test] public void Combo_Only_One_Face_Returns_Zero() =>
        Assert.AreEqual(0, SicBoResolver.ComboPayout(100, 1, 6, 1, 2, 3));

    [Test] public void Combo_Enum_Decodes_In_Order()
    {
        Assert.AreEqual((1, 2), SicBoResolver.ComboFaces(SicBoBetType.Combo12));
        Assert.AreEqual((3, 6), SicBoResolver.ComboFaces(SicBoBetType.Combo36));
        Assert.AreEqual((5, 6), SicBoResolver.ComboFaces(SicBoBetType.Combo56));
    }

    // ── Triples ────────────────────────────────────────────────────────────
    [Test] public void AnyTriple_Pays_24to1_On_Triple() =>
        Assert.AreEqual(2500, SicBoResolver.AnyTriplePayout(100, true));

    [Test] public void AnyTriple_Returns_Zero_On_No_Triple() =>
        Assert.AreEqual(0, SicBoResolver.AnyTriplePayout(100, false));

    [Test] public void SpecificTriple_Pays_150to1_On_Exact_Match() =>
        Assert.AreEqual(15100, SicBoResolver.SpecificTriplePayout(100, 4, 4, 4, 4));

    [Test] public void SpecificTriple_Zero_On_Non_Triple() =>
        Assert.AreEqual(0, SicBoResolver.SpecificTriplePayout(100, 4, 4, 4, 3));

    // ── Three single number combination ────────────────────────────────────
    [Test] public void ThreeNumber_Pays_30to1_Any_Order() =>
        Assert.AreEqual(3100, SicBoResolver.Payout(SicBoBetType.Three246, 100, 6, 2, 4));

    [Test] public void ThreeNumber_Loses_With_A_Pair() =>
        Assert.AreEqual(0, SicBoResolver.Payout(SicBoBetType.Three246, 100, 2, 2, 4));

    [Test] public void ThreeNumber_Enum_Decodes_In_Order()
    {
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, SicBoResolver.ThreeNumberFaces(SicBoBetType.Three123));
        CollectionAssert.AreEqual(new[] { 2, 4, 6 }, SicBoResolver.ThreeNumberFaces(SicBoBetType.Three246));
        CollectionAssert.AreEqual(new[] { 4, 5, 6 }, SicBoResolver.ThreeNumberFaces(SicBoBetType.Three456));
    }

    // ── Specific double + single ───────────────────────────────────────────
    [Test] public void PairSingle_Pays_50to1() =>
        Assert.AreEqual(5100, SicBoResolver.Payout(SicBoBetType.Pair4Single1, 100, 4, 1, 4));

    [Test] public void PairSingle_Loses_On_Triple_Of_Pair_Face() =>
        Assert.AreEqual(0, SicBoResolver.Payout(SicBoBetType.Pair4Single1, 100, 4, 4, 4));

    [Test] public void PairSingle_Loses_When_Single_Is_The_Pair() =>
        Assert.AreEqual(0, SicBoResolver.Payout(SicBoBetType.Pair1Single4, 100, 4, 1, 4));

    [Test] public void PairSingle_Enum_Decodes_In_Order()
    {
        Assert.AreEqual((1, 2), SicBoResolver.PairSingleFaces(SicBoBetType.Pair1Single2));
        Assert.AreEqual((2, 1), SicBoResolver.PairSingleFaces(SicBoBetType.Pair2Single1));
        Assert.AreEqual((4, 5), SicBoResolver.PairSingleFaces(SicBoBetType.Pair4Single5));
        Assert.AreEqual((6, 5), SicBoResolver.PairSingleFaces(SicBoBetType.Pair6Single5));
    }

    // ── Four number combination ────────────────────────────────────────────
    [Test] public void FourNumber_Pays_7to1_On_Three_Of_The_Four() =>
        Assert.AreEqual(800, SicBoResolver.Payout(SicBoBetType.Four2356, 100, 6, 2, 5));

    [Test] public void FourNumber_Loses_With_A_Pair() =>
        Assert.AreEqual(0, SicBoResolver.Payout(SicBoBetType.Four2356, 100, 6, 6, 5));

    [Test] public void FourNumber_Loses_When_A_Die_Is_Outside_The_Set() =>
        Assert.AreEqual(0, SicBoResolver.Payout(SicBoBetType.Four2356, 100, 6, 4, 5));
}
