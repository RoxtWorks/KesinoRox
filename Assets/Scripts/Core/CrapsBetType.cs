// Every flat, single-slot craps bet — one dictionary entry each. Come bets are
// NOT here since several can be active at once, each parked at its own point;
// those live in a List<ComeWager> instead (see ComeWager.cs).
// Crapless craps: Don't Pass and Don't Come are not offered — every non-7 total
// is a valid point, which makes the don't-side math untenable.
public enum CrapsBetType
{
    PassLine,
    PassOdds,
    Field,
    Place2,
    Place3,
    Place4,
    Place5,
    Place6,
    Place8,
    Place9,
    Place10,
    Place11,
    Place12,
    Hard4,
    Hard6,
    Hard8,
    Hard10,
    AnyCraps,
    AnySeven,
    AnyEleven,
    Horn
}
