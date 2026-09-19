using System.Collections.Generic;
using UnityEngine;

// What the betting controller needs from whichever 3D wheel is on the table: the casino
// ball wheel (RouletteBallWheel) or the older needle wheel (ClassicRouletteWheel).
public interface IRouletteWheel
{
    Vector3 Center { get; }
    void PlaySpin(int winningNumber);
    void SetHighlightedNumbers(HashSet<int> numbers);
    // The pocket the ball is next to right now, as a running index into WheelLayout.PocketOrder
    // (fractional between pockets, never wrapping) — lets the number strip follow the ball.
    // False when the wheel has no ball to follow.
    bool LiveBallPocket(out float pocketIndex);
}

// The original primitive wheel + needle, kept so GameManager can switch back to it.
public class ClassicRouletteWheel : IRouletteWheel
{
    readonly RouletteTableBuilder builder;
    readonly WheelSpinAnimator animator;

    public ClassicRouletteWheel(RouletteTableBuilder builder, WheelSpinAnimator animator)
    {
        this.builder = builder;
        this.animator = animator;
    }

    public Vector3 Center => builder.wheelPivot.position;
    public void PlaySpin(int winningNumber) => animator.PlaySpin(winningNumber);
    public void SetHighlightedNumbers(HashSet<int> numbers) => builder.SetHighlightedNumbers(numbers);
    public bool LiveBallPocket(out float pocketIndex) { pocketIndex = 0f; return false; }
}
