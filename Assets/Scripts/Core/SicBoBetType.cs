// All Sic Bo bet types. Three dice are rolled; every bet below can be active
// simultaneously. One-roll game — all bets resolve on each roll.
public enum SicBoBetType
{
    // Big/Small — simplest bets, 1:1 (lose on any triple)
    Big,   // total 11-17 (excluding triples)
    Small, // total 4-10  (excluding triples)

    // Specific totals (4-17) — payouts vary by probability
    Total4,  Total5,  Total6,  Total7,  Total8,
    Total9,  Total10, Total11, Total12, Total13,
    Total14, Total15, Total16, Total17,

    // Single number — bet that at least one die shows this face
    Single1, Single2, Single3, Single4, Single5, Single6,

    // Specific double (pair) — at least two dice show this face
    Double1, Double2, Double3, Double4, Double5, Double6,

    // Two-number combination — exactly these two values appear on two dice
    Combo12, Combo13, Combo14, Combo15, Combo16,
    Combo23, Combo24, Combo25, Combo26,
    Combo34, Combo35, Combo36,
    Combo45, Combo46,
    Combo56,

    // Any triple — all three dice show the same face (any)
    AnyTriple,

    // Specific triple — all three dice show this exact face
    Triple1, Triple2, Triple3, Triple4, Triple5, Triple6,

    // Odd / Even total, 1:1 (lose on any triple)
    Odd, Even,

    // Three single number combination — these three different faces all show. Pays 30:1.
    Three123, Three124, Three125, Three126, Three134, Three135, Three136, Three145, Three146, Three156,
    Three234, Three235, Three236, Three245, Three246, Three256, Three345, Three346, Three356, Three456,

    // Specific double + single — two dice show the pair face, the third the single face. Pays 50:1.
    Pair1Single2, Pair1Single3, Pair1Single4, Pair1Single5, Pair1Single6,
    Pair2Single1, Pair2Single3, Pair2Single4, Pair2Single5, Pair2Single6,
    Pair3Single1, Pair3Single2, Pair3Single4, Pair3Single5, Pair3Single6,
    Pair4Single1, Pair4Single2, Pair4Single3, Pair4Single5, Pair4Single6,
    Pair5Single1, Pair5Single2, Pair5Single3, Pair5Single4, Pair5Single6,
    Pair6Single1, Pair6Single2, Pair6Single3, Pair6Single4, Pair6Single5,

    // Four number combination — three different faces from the four all show. Pays 7:1.
    Four1234, Four2345, Four2356, Four3456
}
