using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Casino-style European wheel built from Marcus's EuropeanRoulette_normal model (split into
// rotor / bowl meshes by RouletteWheelImporter). The rotor turns one way, the ball is spun the
// other way round the track, slows, drops, bounces over the frets and settles in the pocket.
// The number is decided by SpinResultGenerator before the spin starts — the ball's path is
// scripted backwards from that pocket, so the wheel only ever shows the real result.
public class RouletteBallWheel : MonoBehaviour, IRouletteWheel
{
    // Measured off the model: pocket 0 sits at -90 deg (atan2(z, x)) in rotor space, and the
    // European order 0, 32, 15, 19... runs toward decreasing angle (clockwise seen from above).
    const float ZeroAngleDeg = -90f;
    const float PocketStepDeg = 360f / WheelLayout.PocketCount;
    const float PocketR = 2.28f, PocketY = -0.30f;         // pocket floors
    const float TrackR = 3.82f, TrackY = 0.0f;             // ball running against the rim
    const float TrackInnerR = 3.12f, TrackInnerY = -0.15f; // bottom of the sloped track, rotor edge
    const float BallRadius = 0.1f;

    const float Duration = 8f;    // matches ConveyorBeltUI so the ball settles as the strip stops
    const float LaunchTime = 0.5f; // ball lifted out of its old pocket onto the track
    const float DropStart = 4.6f;  // ball loses speed and starts running down the slope
    const float DropEnd = 6.0f;    // ball reaches the rotor
    const float SettleEnd = 7.6f;  // bounces over, comes to rest in the pocket
    const float BallTurns = 7f;    // full laps round the track before the drop
    const float IdleSpeed = 10f;   // deg/s the rotor keeps turning between spins
    const float SpinBoost = 50f;   // extra deg/s from the croupier's push, fading over the spin

    static readonly Color HighlightBlue = new Color(0.25f, 0.55f, 1f);

    // Colours from the model's .mtl (Kd), with metal/gloss chosen per part
    static readonly Dictionary<string, (Color color, float metallic, float smoothness)> Looks =
        new Dictionary<string, (Color, float, float)>
        {
            { "MaterialBaseNegra",       (new Color(0.056f, 0.056f, 0.056f), 0.1f, 0.6f) },
            { "MaterialBaseRoja",        (new Color(0.64f, 0.007f, 0.003f), 0.1f, 0.6f) },
            { "MaterialFranjaVerde",     (new Color(0f, 0.358f, 0.005f), 0.1f, 0.6f) },
            { "MaterialNumeros",         (new Color(0.92f, 0.92f, 0.92f), 0f, 0.4f) },
            { "MaterialCilindroCentral", (new Color(0.706f, 0.528f, 0.14f), 0.85f, 0.75f) },
            { "MaterialDecoradoBlanco",  (new Color(0.75f, 0.75f, 0.78f), 0.8f, 0.7f) },
            { "MaterialParedesBolita",   (new Color(0.70f, 0.72f, 0.76f), 0.85f, 0.75f) },
            { "Material_OblicuoInterior",(new Color(0.64f, 0.203f, 0.038f), 0.1f, 0.65f) },
            { "MaterialOblicuoExterior", (new Color(0.287f, 0.231f, 0.131f), 0.05f, 0.7f) },
            { "MaterialExterior",        (new Color(0.274f, 0.136f, 0.082f), 0.05f, 0.6f) },
        };

    Transform rotor;
    Transform ball;
    float rotorYaw;
    bool spinning;
    float ballLocalAngle = ZeroAngleDeg; // pocket the ball rests in between spins (rotor space)
    readonly Dictionary<int, GameObject> highlightMarkers = new Dictionary<int, GameObject>();

    public Vector3 Center => transform.position;

    public void Build()
    {
        var rotorGO = new GameObject("Rotor");
        rotorGO.transform.SetParent(transform, false);
        rotor = rotorGO.transform;

        foreach (var mesh in Resources.LoadAll<Mesh>("RouletteWheel"))
        {
            bool isRotor = mesh.name.StartsWith("Rotor_");
            string material = mesh.name.Substring(mesh.name.IndexOf('_') + 1);
            var part = new GameObject(mesh.name);
            part.transform.SetParent(isRotor ? rotor : transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var look = Looks.TryGetValue(material, out var l) ? l : (Color.gray, 0f, 0.5f);
            part.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(look.Item1, look.Item2, look.Item3);
        }

        // Small blue lights on the rotor's outer edge beside every number with a bet on it
        foreach (int n in WheelLayout.PocketOrder)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = $"BetMarker_{n}";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(rotor, false);
            marker.transform.localPosition = Polar(PocketLocalAngle(n), 2.98f, -0.14f);
            marker.transform.localScale = new Vector3(0.15f, 0.01f, 0.15f);
            marker.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(HighlightBlue, 0f, 0.5f, HighlightBlue * 1.6f);
            marker.SetActive(false);
            highlightMarkers[n] = marker;
        }

        var ballGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ballGO.name = "Ball";
        Destroy(ballGO.GetComponent<Collider>());
        ballGO.transform.SetParent(transform, false);
        ballGO.transform.localScale = Vector3.one * BallRadius * 2f;
        ballGO.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.95f, 0.95f, 0.97f), 0.6f, 0.9f);
        ball = ballGO.transform;
        PlaceRestingBall();
    }

    void Update()
    {
        if (spinning || rotor == null) return;
        rotorYaw += IdleSpeed * Time.deltaTime;
        rotor.localRotation = Quaternion.Euler(0f, rotorYaw, 0f);
        PlaceRestingBall();
    }

    public void SetHighlightedNumbers(HashSet<int> numbers)
    {
        foreach (var kv in highlightMarkers)
            kv.Value.SetActive(numbers != null && numbers.Contains(kv.Key));
    }

    public void PlaySpin(int winningNumber)
    {
        StopAllCoroutines();
        StartCoroutine(SpinRoutine(winningNumber));
    }

    IEnumerator SpinRoutine(int winningNumber)
    {
        spinning = true;
        float yaw0 = rotorYaw;
        float targetA = PocketLocalAngle(winningNumber);
        // The ball comes off the track a few pockets short of its pocket and bounces the rest of the way
        float pre = (UnityEngine.Random.Range(2, 6) + UnityEngine.Random.Range(-0.3f, 0.3f)) * PocketStepDeg;

        Vector3 start = transform.InverseTransformPoint(ball.position);
        float alpha0 = Mathf.Atan2(start.z, start.x) * Mathf.Rad2Deg;
        float r0 = new Vector2(start.x, start.z).magnitude;
        float y0 = start.y;

        // Where on the track (wheel space) the ball has to be when it reaches the rotor, so the
        // rotor carries its target pocket under it: solved from the rotor's own fixed schedule.
        Vector3 dropPoint = Quaternion.Euler(0f, YawAt(yaw0, DropEnd), 0f) * Polar(targetA - pre, 1f, 0f);
        float alphaDrop = Mathf.Atan2(dropPoint.z, dropPoint.x) * Mathf.Rad2Deg;
        float travel = BallTurns * 360f + Mathf.Repeat(alphaDrop - alpha0, 360f);

        float t = 0f;
        while (t < Duration)
        {
            t += Time.deltaTime;
            rotorYaw = YawAt(yaw0, Mathf.Min(t, Duration));
            rotor.localRotation = Quaternion.Euler(0f, rotorYaw, 0f);

            if (t < DropEnd)
            {
                // Ball runs counter to the rotor, fast at first and slowing (ends at a quarter speed)
                float u = t / DropEnd;
                float alpha = alpha0 + travel * (u + 0.75f * u * (1f - u));
                float r, y;
                if (t < LaunchTime)
                {
                    float s = Smooth(t / LaunchTime);
                    r = Mathf.Lerp(r0, TrackR, s);
                    y = Mathf.Lerp(y0, TrackY + BallRadius, s) + 0.12f * Mathf.Sin(Mathf.PI * s);
                }
                else if (t < DropStart)
                {
                    r = TrackR;
                    y = TrackY + BallRadius;
                }
                else
                {
                    float s = (t - DropStart) / (DropEnd - DropStart);
                    float s2 = s * s;
                    r = Mathf.Lerp(TrackR, TrackInnerR, s2);
                    y = Mathf.Lerp(TrackY, TrackInnerY, s2) + BallRadius;
                    // clips a diamond on the way down
                    if (s > 0.35f && s < 0.6f) y += 0.07f * Mathf.Sin(Mathf.PI * (s - 0.35f) / 0.25f);
                }
                ball.localPosition = Polar(alpha, r, y);
            }
            else if (t < SettleEnd)
            {
                // On the rotor now (rotor space): bounces over the frets, a little back and forth, into the pocket
                float u = (t - DropEnd) / (SettleEnd - DropEnd);
                float fade = (1f - u) * (1f - u);
                float a = targetA - pre * Mathf.Pow(1f - u, 3f) + PocketStepDeg * 0.6f * Mathf.Sin(3f * Mathf.PI * u) * fade;
                float drop = Smooth(Mathf.Min(1f, u / 0.3f));
                float r = Mathf.Lerp(TrackInnerR, PocketR, drop);
                float y = Mathf.Lerp(TrackInnerY, PocketY, drop) + BallRadius + 0.16f * Mathf.Abs(Mathf.Sin(3f * Mathf.PI * u)) * fade;
                ball.position = rotor.TransformPoint(Polar(a, r, y));
            }
            else
            {
                ballLocalAngle = targetA;
                PlaceRestingBall();
            }
            yield return null;
        }

        ballLocalAngle = targetA;
        PlaceRestingBall();
        spinning = false;
    }

    // Rotor angle t seconds into a spin: idle speed plus the croupier's push fading to nothing at the end
    static float YawAt(float yaw0, float t) => yaw0 + IdleSpeed * t + SpinBoost * (t - t * t / (2f * Duration));

    void PlaceRestingBall() => ball.position = rotor.TransformPoint(Polar(ballLocalAngle, PocketR, PocketY + BallRadius));

    static float PocketLocalAngle(int number)
    {
        int index = Array.IndexOf(WheelLayout.PocketOrder, number);
        return ZeroAngleDeg - Mathf.Max(0, index) * PocketStepDeg;
    }

    static Vector3 Polar(float angleDeg, float r, float y)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        return new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
    }

    static float Smooth(float x) => x * x * (3f - 2f * x);

    static Material MakeMaterial(Color color, float metallic, float smoothness, Color? emission = null)
    {
        // Same stripping-safe fallback chain as RouletteTableBuilder
        var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader) { color = color };
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Glossiness", smoothness);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
        }
        return mat;
    }
}
