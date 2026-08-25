using System;
using System.Collections;
using UnityEngine;

// Rotates the 3D wheel disc to visually land on the same winning number the
// conveyor belt shows, synced to the same duration so both finish together. Now
// that pocket numbers are legible (fixed divider placement, steep camera), the
// wheel can be part of the reveal again — purely a rotation, no ball/physics.
public class WheelSpinAnimator : MonoBehaviour
{
    public Transform wheelPivot;
    public Renderer markerRenderer;

    // 8s / 4 rotations ≈ half a real wheel's ~16s spin at the same rotation rate —
    // a real wheel holds a fairly constant speed (the ball does the dramatic
    // deceleration, not the wheel), so scaling duration and rotation count together
    // keeps the same "how fast is it actually turning" feel instead of just
    // stretching out the same manic speed over more time.
    const float Duration = 8f; // matches ConveyorBeltUI so both land together
    static readonly Color MarkerIdle = new Color(0.85f, 0.68f, 0.24f); // gold, matches RouletteTableBuilder's Gold

    static float EaseOut(float u) => 1f - Mathf.Pow(1f - u, 5f);

    public void PlaySpin(int winningNumber)
    {
        StartCoroutine(SpinRoutine(winningNumber));
    }

    IEnumerator SpinRoutine(int winningNumber)
    {
        SetMarkerColor(MarkerIdle);

        int pocketIndex = Array.IndexOf(WheelLayout.PocketOrder, winningNumber);
        if (pocketIndex < 0) pocketIndex = 0;
        float pocketLocalAngleDeg = pocketIndex * 360f / WheelLayout.PocketCount;

        float startYaw = wheelPivot.eulerAngles.y;
        // Unity's Y-axis rotation is left-handed: rotating a parent by +θ moves a
        // child at local angle 'a' to world angle (a - θ), SUBTRACTING — verified
        // empirically, not assumed (a plausible-looking "world = local + wheelYaw"
        // formula would have landed the wheel on the wrong pocket every time). So the
        // winning pocket lands at the marker when pocketLocalAngleDeg - wheelYaw ==
        // marker angle, i.e. wheelYaw = pocketLocalAngleDeg - marker angle.
        float targetYaw = pocketLocalAngleDeg - RouletteTableBuilder.ResultMarkerAngleDeg;
        while (targetYaw < startYaw) targetYaw += 360f;
        targetYaw += 360f * 4f; // extra full turns so the spin actually reads as a spin

        // Straight ease-out to the landing angle, no overshoot — a real wheel just
        // decelerates to a stop, it doesn't pass the winning pocket and correct back
        // to it. Overshoot-then-settle was tried here as a "hit a stop" flourish, but
        // on a result that's already decided in advance, snapping back onto the exact
        // winning number reads as the wheel being steered there rather than genuinely
        // stopping — the wrong impression for a fairness-sensitive simulator.
        float t = 0f;
        while (t < Duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / Duration);
            wheelPivot.rotation = Quaternion.Euler(0f, Mathf.Lerp(startYaw, targetYaw, EaseOut(u)), 0f);
            yield return null;
        }
        wheelPivot.rotation = Quaternion.Euler(0f, targetYaw, 0f);

        Color resultColor = winningNumber == 0 ? UIFactory.FeltGreen
            : WheelLayout.IsRed(winningNumber) ? UIFactory.RedBet
            : UIFactory.BlackBet;
        SetMarkerColor(resultColor);
    }

    void SetMarkerColor(Color color)
    {
        if (markerRenderer == null) return;
        markerRenderer.material.color = color;
    }
}
