using System.Collections.Generic;

public static class WheelLayout
{
    // European single-zero wheel, physical pocket order (clockwise from 0)
    public static readonly int[] PocketOrder = new int[]
    {
        0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8,
        23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12,
        35, 3, 26
    };

    public const int PocketCount = 37; // single-zero (realistic European wheel, not American double-zero)

    public static bool IsRed(int number)
    {
        var red = new HashSet<int> { 1,3,5,7,9,12,14,16,18,19,21,23,25,27,30,32,34,36 };
        return red.Contains(number);
    }
}
