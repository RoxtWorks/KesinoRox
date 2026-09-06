// Standard (non-crapless) craps bets. Point numbers are 4/5/6/8/9/10 only —
// 2/3/11/12 crap out on the come-out roll instead of becoming points, so there
// are no Place bets on those numbers. Don't Pass and Don't Come ARE offered here
// (unlike crapless craps where the don't-side math is untenable).
public enum NormalCrapsBetType
{
    PassLine,
    PassOdds,
    DontPass,
    DontPassOdds,
    Field,
    Place4,
    Place5,
    Place6,
    Place8,
    Place9,
    Place10,
    Hard4,
    Hard6,
    Hard8,
    Hard10,
    AnyCraps,
    AnySeven,
    AnyEleven,
    Horn,
    Lay4,
    Lay5,
    Lay6,
    Lay8,
    Lay9,
    Lay10,
    AtsLows,   // Small: 2-3-4-5-6 all before 7, pays 30:1
    AtsHighs,  // Tall:  8-9-10-11-12 all before 7, pays 30:1
    AtsAll     // All:   all 10 numbers before 7, pays 155:1
}
