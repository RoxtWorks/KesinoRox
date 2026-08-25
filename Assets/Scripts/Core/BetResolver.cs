using System;
using System.Linq;

// Standard European roulette payout table. Pure function of (bet, winning number) —
// no RNG, no state, safe to hammer in a tight loop for batch simulation.
public static class BetResolver
{
    public static bool IsWin(Bet bet, int winningNumber)
    {
        switch (bet.Type)
        {
            case BetType.Straight:
            case BetType.Split:
            case BetType.Street:
            case BetType.Corner:
            case BetType.SixLine:
                return bet.Numbers.Contains(winningNumber);
            case BetType.Red: return winningNumber != 0 && WheelLayout.IsRed(winningNumber);
            case BetType.Black: return winningNumber != 0 && !WheelLayout.IsRed(winningNumber);
            case BetType.Odd: return winningNumber != 0 && winningNumber % 2 == 1;
            case BetType.Even: return winningNumber != 0 && winningNumber % 2 == 0;
            case BetType.Low1to18: return winningNumber >= 1 && winningNumber <= 18;
            case BetType.High19to36: return winningNumber >= 19 && winningNumber <= 36;
            case BetType.Dozen1: return winningNumber >= 1 && winningNumber <= 12;
            case BetType.Dozen2: return winningNumber >= 13 && winningNumber <= 24;
            case BetType.Dozen3: return winningNumber >= 25 && winningNumber <= 36;
            case BetType.Column1: return winningNumber != 0 && winningNumber % 3 == 1;
            case BetType.Column2: return winningNumber != 0 && winningNumber % 3 == 2;
            case BetType.Column3: return winningNumber != 0 && winningNumber % 3 == 0;
            default: throw new ArgumentOutOfRangeException(nameof(bet.Type), bet.Type, null);
        }
    }

    static int PayoutToOne(BetType type)
    {
        switch (type)
        {
            case BetType.Straight: return 35;
            case BetType.Split: return 17;
            case BetType.Street: return 11;
            case BetType.Corner: return 8;
            case BetType.SixLine: return 5;
            case BetType.Dozen1:
            case BetType.Dozen2:
            case BetType.Dozen3:
            case BetType.Column1:
            case BetType.Column2:
            case BetType.Column3: return 2;
            default: return 1; // Red/Black/Odd/Even/Low/High are even money
        }
    }

    // Total returned to the player if they win: stake back + winnings. 0 if they lose.
    public static long Resolve(Bet bet, int winningNumber)
    {
        if (!IsWin(bet, winningNumber)) return 0;
        return bet.Amount + bet.Amount * PayoutToOne(bet.Type);
    }
}
