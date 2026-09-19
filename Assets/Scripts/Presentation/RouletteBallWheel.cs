using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Casino European wheel, built in code: a lathe-turned wood bowl and ball track, a rotor with a
// wood cone, deep pockets split by chrome frets, a sloped lacquered number ring with the numbers
// set into it, a chrome turret, chrome deflector diamonds, all sunk into green felt.
//
// The number is decided by SpinResultGenerator before the spin starts. The ball runs round the
// track against the rotor, slows and drops; from there a small physics simulation (fret hits,
// hops, rattling back, friction) plays out, and the drop point is chosen so wherever that
// simulation comes to rest is the winning pocket. Every landing is different — sometimes it
// falls straight in, sometimes it bounces across several pockets or rattles back one.
public class RouletteBallWheel : MonoBehaviour, IRouletteWheel
{
    // ---- Geometry (world units, wheel centred on the origin, felt at y = FeltY) ----
    const float PocketStepDeg = 360f / WheelLayout.PocketCount;
    const float ZeroAngleDeg = -90f;                  // pocket 0 faces the camera at rest
    const float PocketInnerR = 1.95f, PocketOuterR = 2.60f, PocketFloorY = -0.30f;
    const float RingInnerY = -0.18f, RingOuterR = 3.15f, RingOuterY = -0.10f; // sloped number ring
    const float PocketR = 2.27f;                      // where a resting ball sits
    const float TrackInnerR = 3.22f, TrackInnerY = -0.04f, TrackOuterR = 3.95f, TrackOuterY = 0.14f;
    const float TrackR = 3.84f;                       // ball running against the wall
    const float FeltY = -0.05f;
    const float BallRadius = 0.1f;

    // ---- Timing ----
    const float Duration = 8f;     // matches ConveyorBeltUI so the ball settles as the strip stops
    const float SettleBy = 7.85f;  // simulation must finish by here (the strip, which follows the ball, reports at 8s)
    const float LaunchTime = 0.5f; // ball lifted out of its old pocket onto the track
    const float DescentTime = 1.3f; // track wall → rotor edge
    const float BallTurns = 7f;    // laps round the track before the drop
    const float IdleSpeed = 10f;   // deg/s the rotor keeps turning between spins
    const float SpinBoost = 50f;   // extra deg/s from the croupier's push, fading over the spin

    static readonly Color HighlightBlue = new Color(0.25f, 0.55f, 1f);

    Transform rotor;
    Transform ball;
    float rotorYaw;
    bool spinning;
    float ballLocalAngle = ZeroAngleDeg; // pocket the ball rests in between spins (rotor space)
    readonly Dictionary<int, GameObject> highlightMarkers = new Dictionary<int, GameObject>();
    System.Random rng = new System.Random();

    public Vector3 Center => transform.position;

    // ================================================================ build

    public void Build()
    {
        var rotorGO = new GameObject("Rotor");
        rotorGO.transform.SetParent(transform, false);
        rotor = rotorGO.transform;

        var mahogany = WoodMaterial(new Color(0.20f, 0.07f, 0.03f), new Color(0.42f, 0.16f, 0.07f), 11, 0.82f);
        var walnut = WoodMaterial(new Color(0.16f, 0.08f, 0.04f), new Color(0.34f, 0.19f, 0.09f), 29, 0.85f);
        var trackWood = WoodMaterial(new Color(0.13f, 0.06f, 0.03f), new Color(0.28f, 0.12f, 0.05f), 47, 0.9f);
        var chrome = Mat(new Color(0.92f, 0.92f, 0.95f), 1f, 0.93f);
        var brass = Mat(new Color(0.86f, 0.68f, 0.32f), 1f, 0.88f);
        var red = Mat(new Color(0.58f, 0.03f, 0.03f), 0.05f, 0.88f);
        var black = Mat(new Color(0.03f, 0.03f, 0.035f), 0.05f, 0.9f);
        var green = Mat(new Color(0.02f, 0.36f, 0.14f), 0.05f, 0.88f);
        var felt = FeltMaterial();

        // Bowl (fixed): ball track sloping down to the rotor, the wall the ball runs against, the rim
        AddLathe(transform, "Track", trackWood, 4f, new[]
        {
            new Vector2(TrackInnerR, TrackInnerY - 0.10f), new Vector2(TrackInnerR, TrackInnerY),
            new Vector2(3.60f, 0.05f), new Vector2(TrackOuterR, TrackOuterY),
        });
        AddLathe(transform, "Rim", mahogany, 3f, new[]
        {
            new Vector2(TrackOuterR, TrackOuterY), new Vector2(3.99f, 0.25f), new Vector2(4.03f, 0.34f),
            new Vector2(4.08f, 0.37f), new Vector2(4.50f, 0.37f), new Vector2(4.57f, 0.34f),
            new Vector2(4.60f, 0.27f), new Vector2(4.61f, FeltY - 0.02f),
        });
        AddLathe(transform, "RimTrim", brass, 1f, new[] { new Vector2(4.075f, 0.372f), new Vector2(4.105f, 0.372f) });
        AddLathe(transform, "BowlFloor", black, 1f, new[] { new Vector2(0f, -0.45f), new Vector2(TrackInnerR + 0.01f, -0.45f) });
        AddLathe(transform, "Felt", felt, 8f, new[] { new Vector2(4.61f, FeltY - 0.02f), new Vector2(40f, FeltY - 0.02f) });

        // Deflector diamonds on the track, alternating along and across the ball's path
        for (int i = 0; i < 8; i++)
        {
            float a = 22.5f + i * 45f;
            var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
            d.name = $"Diamond_{i}";
            Destroy(d.GetComponent<Collider>());
            d.transform.SetParent(transform, false);
            d.transform.localPosition = Polar(a, 3.55f, TrackSurfaceY(3.55f) + 0.025f);
            d.transform.localRotation = Quaternion.Euler(0f, -a + (i % 2 == 0 ? 0f : 90f), 0f) * Quaternion.Euler(0f, 45f, 0f);
            d.transform.localScale = new Vector3(i % 2 == 0 ? 0.30f : 0.10f, 0.06f, i % 2 == 0 ? 0.10f : 0.30f);
            d.GetComponent<MeshRenderer>().sharedMaterial = brass;
        }

        // Rotor: wood cone, pocket ring, sloped number ring, outer lip
        AddLathe(rotor, "Cone", walnut, 6f, new[]
        {
            new Vector2(0.42f, -0.02f), new Vector2(1.2f, -0.10f), new Vector2(PocketInnerR - 0.05f, RingInnerY),
        });
        AddLathe(rotor, "ConeRing", chrome, 1f, new[] { new Vector2(PocketInnerR - 0.05f, RingInnerY), new Vector2(PocketInnerR, RingInnerY) });
        AddLathe(rotor, "PocketInnerWall", black, 1f, new[] { new Vector2(PocketInnerR, RingInnerY), new Vector2(PocketInnerR, PocketFloorY) });
        AddLathe(rotor, "PocketOuterWall", black, 1f, new[] { new Vector2(PocketOuterR, PocketFloorY), new Vector2(PocketOuterR, RingInnerY) });
        AddLathe(rotor, "RotorLip", mahogany, 2f, new[]
        {
            new Vector2(RingOuterR, RingOuterY), new Vector2(RingOuterR + 0.04f, RingOuterY - 0.01f), new Vector2(RingOuterR + 0.05f, -0.30f),
        });

        // Pocket floors and the number band, one wedge per pocket, coloured red / black / green
        var redMesh = new MeshBuilder();
        var blackMesh = new MeshBuilder();
        var greenMesh = new MeshBuilder();
        foreach (int n in WheelLayout.PocketOrder)
        {
            var mb = n == 0 ? greenMesh : WheelLayout.IsRed(n) ? redMesh : blackMesh;
            float c = PocketLocalAngle(n);
            mb.Wedge(c - PocketStepDeg / 2f, c + PocketStepDeg / 2f, PocketInnerR, PocketFloorY, PocketOuterR, PocketFloorY);
            mb.Wedge(c - PocketStepDeg / 2f, c + PocketStepDeg / 2f, PocketOuterR, RingInnerY, RingOuterR, RingOuterY);
        }
        AddMesh(rotor, "RedSegments", redMesh.ToMesh(), red);
        AddMesh(rotor, "BlackSegments", blackMesh.ToMesh(), black);
        AddMesh(rotor, "GreenSegments", greenMesh.ToMesh(), green);

        // Chrome frets between pockets, and thin chrome lines between numbers
        for (int i = 0; i < WheelLayout.PocketCount; i++)
        {
            float a = ZeroAngleDeg - (i + 0.5f) * PocketStepDeg;
            var fret = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fret.name = $"Fret_{i}";
            Destroy(fret.GetComponent<Collider>());
            fret.transform.SetParent(rotor, false);
            fret.transform.localPosition = Polar(a, (PocketInnerR + PocketOuterR) / 2f, (PocketFloorY + RingInnerY) / 2f + 0.01f);
            fret.transform.localRotation = Quaternion.Euler(0f, 90f - a, 0f);
            fret.transform.localScale = new Vector3(0.05f, RingInnerY - PocketFloorY + 0.02f, PocketOuterR - PocketInnerR);
            fret.GetComponent<MeshRenderer>().sharedMaterial = chrome;

            var line = new MeshBuilder();
            line.Wedge(a - 0.35f, a + 0.35f, PocketOuterR, RingInnerY + 0.004f, RingOuterR, RingOuterY + 0.004f);
            AddMesh(rotor, $"NumberLine_{i}", line.ToMesh(), brass);
        }

        // Numbers set into the sloped ring, reading upright from outside the wheel
        var font = TMP_Settings.defaultFontAsset;
        float ringMidR = (PocketOuterR + RingOuterR) / 2f;
        foreach (int n in WheelLayout.PocketOrder)
        {
            float a = PocketLocalAngle(n);
            var go = new GameObject($"Number_{n}");
            go.transform.SetParent(rotor, false);
            // Lie on the ring's slope, text facing up out of it, top of the digits pointing at the centre
            Vector3 radial = Polar(a, 1f, 0f);
            Vector3 along = (radial * (RingOuterR - PocketOuterR) + Vector3.up * (RingOuterY - RingInnerY)).normalized;
            Vector3 normal = (-radial * (RingOuterY - RingInnerY) + Vector3.up * (RingOuterR - PocketOuterR)).normalized;
            go.transform.localPosition = Polar(a, ringMidR, (RingInnerY + RingOuterY) / 2f) + normal * 0.004f;
            go.transform.localRotation = Quaternion.LookRotation(-normal, -along);
            var tmp = go.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            tmp.text = n.ToString();
            tmp.fontSize = 2.6f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.color = new Color(0.97f, 0.92f, 0.78f);
            tmp.rectTransform.sizeDelta = new Vector2(0.6f, 0.4f);
            tmp.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Turret: chrome spindle with four arms
        AddLathe(rotor, "Turret", chrome, 1f, new[]
        {
            new Vector2(0f, 0.74f), new Vector2(0.06f, 0.70f), new Vector2(0.05f, 0.64f), new Vector2(0.12f, 0.56f),
            new Vector2(0.16f, 0.50f), new Vector2(0.15f, 0.44f), new Vector2(0.09f, 0.38f), new Vector2(0.10f, 0.20f),
            new Vector2(0.14f, 0.10f), new Vector2(0.26f, 0.06f), new Vector2(0.40f, 0.02f), new Vector2(0.44f, -0.03f),
        }, smooth: true);
        for (int i = 0; i < 4; i++)
        {
            float a = 45f + i * 90f;
            var arm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arm.name = $"TurretArm_{i}";
            Destroy(arm.GetComponent<Collider>());
            arm.transform.SetParent(rotor, false);
            arm.transform.localPosition = Polar(a, 0.42f, 0.47f);
            arm.transform.localRotation = Quaternion.Euler(0f, 90f - a, 0f) * Quaternion.Euler(90f, 0f, 0f);
            arm.transform.localScale = new Vector3(0.075f, 0.30f, 0.075f);
            arm.GetComponent<MeshRenderer>().sharedMaterial = chrome;
            var knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            knob.name = $"TurretKnob_{i}";
            Destroy(knob.GetComponent<Collider>());
            knob.transform.SetParent(rotor, false);
            knob.transform.localPosition = Polar(a, 0.74f, 0.47f);
            knob.transform.localScale = Vector3.one * 0.15f;
            knob.GetComponent<MeshRenderer>().sharedMaterial = chrome;
        }

        // Small blue lights on the rotor's outer lip beside every number with a bet on it
        var blue = Mat(HighlightBlue, 0f, 0.5f, HighlightBlue * 1.6f);
        foreach (int n in WheelLayout.PocketOrder)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = $"BetMarker_{n}";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(rotor, false);
            marker.transform.localPosition = Polar(PocketLocalAngle(n), RingOuterR - 0.05f, RingOuterY + 0.01f);
            marker.transform.localScale = new Vector3(0.1f, 0.006f, 0.1f);
            marker.GetComponent<MeshRenderer>().sharedMaterial = blue;
            marker.SetActive(false);
            highlightMarkers[n] = marker;
        }

        var ballGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ballGO.name = "Ball";
        Destroy(ballGO.GetComponent<Collider>());
        ballGO.transform.SetParent(transform, false);
        ballGO.transform.localScale = Vector3.one * BallRadius * 2f;
        ballGO.GetComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.96f, 0.95f, 0.92f), 0f, 0.92f);
        ball = ballGO.transform;
        PlaceRestingBall();

        SetUpLighting();
    }

    // Warm overhead casino light, and a soft studio-style environment so chrome and lacquer have something to reflect
    void SetUpLighting()
    {
        var spotGO = new GameObject("WheelSpotlight");
        spotGO.transform.SetParent(transform, false);
        spotGO.transform.localPosition = new Vector3(0.6f, 11f, -3.5f);
        spotGO.transform.LookAt(transform.position);
        var spot = spotGO.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.spotAngle = 52f;
        spot.innerSpotAngle = 26f;
        spot.range = 25f;
        spot.intensity = 2.6f;
        spot.color = new Color(1f, 0.93f, 0.80f);
        spot.shadows = LightShadows.Soft;
        spot.shadowStrength = 0.75f;

        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = MakeEnvironmentCubemap();
        RenderSettings.reflectionIntensity = 1f;
        QualitySettings.antiAliasing = Mathf.Max(QualitySettings.antiAliasing, 4);
    }

    // ================================================================ spin

    void Update()
    {
        if (spinning || rotor == null) return;
        rotorYaw += IdleSpeed * Time.deltaTime;
        rotor.localRotation = Quaternion.Euler(0f, rotorYaw, 0f);
        PlaceRestingBall();
    }

    float lastPocket;
    bool pocketTracked;

    public bool LiveBallPocket(out float pocketIndex)
    {
        Vector3 lp = rotor.InverseTransformPoint(ball.position);
        float a = Mathf.Atan2(lp.z, lp.x) * Mathf.Rad2Deg;
        float raw = Mathf.Repeat((ZeroAngleDeg - a) / PocketStepDeg, WheelLayout.PocketCount);
        if (!pocketTracked) { lastPocket = raw; pocketTracked = true; }
        else
        {
            // Unwrap: take the shortest way round from where it was last frame
            float d = raw - Mathf.Repeat(lastPocket, WheelLayout.PocketCount);
            if (d > WheelLayout.PocketCount / 2f) d -= WheelLayout.PocketCount;
            if (d < -WheelLayout.PocketCount / 2f) d += WheelLayout.PocketCount;
            lastPocket += d;
        }
        pocketIndex = lastPocket;
        return true;
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

        // Play the landing out first: how far (in pockets) the ball travels once it reaches the rotor
        // depends only on physics, so run it, then aim the drop so it finishes in the winning pocket.
        float omega0 = RandomRange(55f, 200f);
        Settle settle = null;
        for (int attempt = 0; attempt < 40 && settle == null; attempt++)
        {
            var s = SimulateSettle(omega0 * RandomRange(0.85f, 1.15f));
            if (s.Duration >= 0.7f && s.Duration <= 2.2f) settle = s;
        }
        settle ??= SimulateSettle(omega0, forceQuick: true);

        float dropEnd = SettleBy - settle.Duration;
        float dropStart = dropEnd - DescentTime;
        float dropLocal = targetA - settle.FinalAngle; // the simulation starts at 0 and ends on a pocket centre

        Vector3 start = transform.InverseTransformPoint(ball.position);
        float alpha0 = Mathf.Atan2(start.z, start.x) * Mathf.Rad2Deg;
        float r0 = new Vector2(start.x, start.z).magnitude;
        float y0 = start.y;

        // Where the ball has to be on the track (wheel space) at the drop so the rotor carries the right spot under it
        Vector3 dropPoint = Quaternion.Euler(0f, YawAt(yaw0, dropEnd), 0f) * Polar(dropLocal + settle.Path[0].x, 1f, 0f);
        float alphaDrop = Mathf.Atan2(dropPoint.z, dropPoint.x) * Mathf.Rad2Deg;
        float travel = BallTurns * 360f + Mathf.Repeat(alphaDrop - alpha0, 360f);
        // Ease so the ball's speed relative to the rotor at the drop matches the simulation's starting speed
        float rotorSpeedAtDrop = IdleSpeed + SpinBoost * (1f - dropEnd / Duration);
        float k = Mathf.Clamp(1f - (settle.StartOmega - rotorSpeedAtDrop) * dropEnd / travel, 0.2f, 0.9f);

        float t = 0f;
        while (t < Duration)
        {
            t += Time.deltaTime;
            rotorYaw = YawAt(yaw0, Mathf.Min(t, Duration));
            rotor.localRotation = Quaternion.Euler(0f, rotorYaw, 0f);

            if (t < dropEnd)
            {
                float u = t / dropEnd;
                float alpha = alpha0 + travel * (u + k * u * (1f - u));
                float r, y;
                if (t < LaunchTime)
                {
                    float s = Smooth(t / LaunchTime);
                    r = Mathf.Lerp(r0, TrackR, s);
                    y = Mathf.Lerp(y0, TrackSurfaceY(TrackR) + BallRadius, s) + 0.18f * Mathf.Sin(Mathf.PI * s);
                }
                else if (t < dropStart)
                {
                    r = TrackR;
                    y = TrackSurfaceY(TrackR) + BallRadius;
                }
                else
                {
                    // Losing speed, it runs down the slope towards the rotor
                    float s = (t - dropStart) / DescentTime;
                    r = Mathf.Lerp(TrackR, TrackInnerR, s * s);
                    y = TrackSurfaceY(r) + BallRadius;
                }
                ball.localPosition = Polar(alpha, r, y);
            }
            else
            {
                var p = settle.Sample(t - dropEnd);
                ball.position = rotor.TransformPoint(Polar(dropLocal + p.x, p.y, PocketFloorY + BallRadius + p.z));
            }
            yield return null;
        }

        ballLocalAngle = targetA;
        PlaceRestingBall();
        spinning = false;
    }

    // ---- The landing, simulated in rotor space: angle (deg), radius, height above the pocket floor ----

    class Settle
    {
        public readonly List<Vector3> Path = new List<Vector3>(); // x = angle, y = radius, z = height
        public float Duration;
        public float FinalAngle;
        public float StartOmega;
        public const float Dt = 1f / 120f;

        public Vector3 Sample(float time)
        {
            float f = time / Dt;
            int i = Mathf.Clamp((int)f, 0, Path.Count - 1);
            int j = Mathf.Min(i + 1, Path.Count - 1);
            return Vector3.Lerp(Path[i], Path[j], f - i);
        }
    }

    // Height of the rotor surface above the pocket floor at radius r
    static float RotorFloor(float r) =>
        r >= PocketOuterR ? (RingInnerY - PocketFloorY) + (r - PocketOuterR) / (RingOuterR - PocketOuterR) * (RingOuterY - RingInnerY) : 0f;

    Settle SimulateSettle(float omega0, bool forceQuick = false)
    {
        const float g = 26f;
        float fretHeight = RingInnerY - PocketFloorY; // frets are as tall as the pocket walls
        var s = new Settle { StartOmega = omega0 };
        // Starts anywhere across a pocket's width; pocket centres are at whole multiples of the step
        float phi = RandomRange(-PocketStepDeg / 2f, PocketStepDeg / 2f), omega = omega0;
        float r = TrackInnerR;
        float h = TrackInnerY - PocketFloorY;
        float vh = 0f;
        float vr = -RandomRange(0.9f, 1.6f);
        float t = 0f, still = 0f;

        while (t < 3.5f)
        {
            float dt = Settle.Dt;
            // Inward: rolls down the number ring, drops into the pocket ring
            if (r > PocketR) { vr -= 3f * dt; r = Mathf.Max(PocketR, r + vr * dt); }
            bool inPockets = r <= PocketOuterR + 0.02f;

            // Up and down: gravity, bouncing on the floor
            vh -= g * dt;
            h += vh * dt;
            float floor = RotorFloor(r);
            bool grounded = false;
            if (h <= floor)
            {
                h = floor;
                if (vh < -0.6f) vh = -vh * RandomRange(0.25f, 0.45f);
                else { vh = 0f; grounded = true; }
            }

            // Around: friction, and the frets
            omega *= 1f - (grounded ? (forceQuick ? 4f : 1.3f) : 0.12f) * dt;
            float next = phi + omega * dt;
            if (inPockets && h < fretHeight * 0.9f)
            {
                float b = FretBetween(phi, next);
                if (!float.IsNaN(b))
                {
                    float linear = Mathf.Abs(omega) * Mathf.Deg2Rad * r;
                    if (linear > RandomRange(0.8f, 3.6f))
                    {
                        // Clips the fret and hops over it
                        omega *= RandomRange(0.45f, 0.75f);
                        vh = RandomRange(1.2f, 3.0f) * Mathf.Clamp(linear / 4f, 0.35f, 1f);
                    }
                    else
                    {
                        // Not enough speed: knocks back off the fret
                        omega = -omega * RandomRange(0.25f, 0.85f);
                        next = b - Mathf.Sign(omega) * -0.01f;
                        vh = RandomRange(0.2f, 1.4f);
                    }
                }
            }
            phi = next;

            // Low and slow in a pocket: rolls to the middle of it and stops
            if (inPockets && grounded && Mathf.Abs(omega) < 40f)
            {
                float centre = Mathf.Round(phi / PocketStepDeg) * PocketStepDeg;
                omega += -(phi - centre) * 60f * dt;
                omega *= 1f - 5f * dt;
                still = Mathf.Abs(omega) < 2f && Mathf.Abs(phi - centre) < 0.4f ? still + dt : 0f;
                if (still > 0.08f)
                {
                    s.Path.Add(new Vector3(centre, r, 0f));
                    s.FinalAngle = centre;
                    s.Duration = t + dt;
                    return s;
                }
            }

            s.Path.Add(new Vector3(phi, r, h));
            t += dt;
        }
        // Didn't settle in time — finish in the nearest pocket
        float last = Mathf.Round(phi / PocketStepDeg) * PocketStepDeg;
        s.Path.Add(new Vector3(last, PocketR, 0f));
        s.FinalAngle = last;
        s.Duration = t;
        return s;
    }

    // Fret boundaries sit halfway between pocket centres (centres at multiples of the step)
    static float FretBetween(float from, float to)
    {
        float lo = Mathf.Min(from, to), hi = Mathf.Max(from, to);
        float b = (Mathf.Floor((lo / PocketStepDeg) - 0.5f) + 1.5f) * PocketStepDeg;
        return b > lo && b <= hi ? b : float.NaN;
    }

    float RandomRange(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // ================================================================ helpers

    // Rotor angle t seconds into a spin: idle speed plus the croupier's push fading to nothing at the end
    static float YawAt(float yaw0, float t) => yaw0 + IdleSpeed * t + SpinBoost * (t - t * t / (2f * Duration));

    static float TrackSurfaceY(float r)
    {
        if (r <= 3.60f) return Mathf.Lerp(TrackInnerY, 0.05f, (r - TrackInnerR) / (3.60f - TrackInnerR));
        return Mathf.Lerp(0.05f, TrackOuterY, (r - 3.60f) / (TrackOuterR - 3.60f));
    }

    void PlaceRestingBall() => ball.position = rotor.TransformPoint(Polar(ballLocalAngle, PocketR, PocketFloorY + BallRadius));

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

    // Revolves a (radius, height) profile round the Y axis. Each profile segment is its own band with
    // its own normal (crisp bevels) unless smooth, where normals are shared (turned metal).
    static void AddLathe(Transform parent, string name, Material mat, float uvRepeat, Vector2[] profile, bool smooth = false)
    {
        const int segments = 144;
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        float along = 0f;
        for (int p = 0; p < profile.Length - 1; p++)
        {
            Vector2 a = profile[p], b = profile[p + 1];
            Vector2 dir = (b - a).normalized;
            // Normal is the profile direction turned left: a profile running outward faces up,
            // running down faces outward, running up faces inward — order each profile to suit
            Vector2 na = new Vector2(-dir.y, dir.x), nb = na;
            if (smooth)
            {
                Vector2 da = p > 0 ? ((a - profile[p - 1]).normalized + dir).normalized : dir;
                Vector2 db = p + 2 < profile.Length ? ((profile[p + 2] - b).normalized + dir).normalized : dir;
                na = new Vector2(-da.y, da.x);
                nb = new Vector2(-db.y, db.x);
            }
            float len = (b - a).magnitude;
            int baseIndex = verts.Count;
            for (int s = 0; s <= segments; s++)
            {
                float ang = s * Mathf.PI * 2f / segments;
                float c = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                verts.Add(new Vector3(a.x * c, a.y, a.x * sn));
                verts.Add(new Vector3(b.x * c, b.y, b.x * sn));
                normals.Add(new Vector3(na.x * c, na.y, na.x * sn).normalized);
                normals.Add(new Vector3(nb.x * c, nb.y, nb.x * sn).normalized);
                float u = (float)s / segments * uvRepeat * 6f;
                uvs.Add(new Vector2(u, along * 0.8f));
                uvs.Add(new Vector2(u, (along + len) * 0.8f));
            }
            for (int s = 0; s < segments; s++)
            {
                int i0 = baseIndex + s * 2;
                tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
                tris.Add(i0 + 2); tris.Add(i0 + 1); tris.Add(i0 + 3);
            }
            along += len;
        }
        var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        // Faces whichever way the camera sees them: flip winding where the normal points away from the triangle's facing
        FixWinding(mesh);
        mesh.RecalculateBounds();
        AddMesh(parent, name, mesh, mat);
    }

    static void FixWinding(Mesh mesh)
    {
        var v = mesh.vertices; var n = mesh.normals; var t = mesh.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            if (Vector3.Dot(face, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]) < 0f) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
        }
        mesh.triangles = t;
    }

    static void AddMesh(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // Annular wedges between two (radius, height) points, facing up
    class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<int> tris = new List<int>();

        public void Wedge(float a0, float a1, float r0, float y0, float r1, float y1)
        {
            const int steps = 4;
            int b = verts.Count;
            for (int s = 0; s <= steps; s++)
            {
                float a = Mathf.Lerp(a0, a1, (float)s / steps);
                verts.Add(Polar(a, r0, y0));
                verts.Add(Polar(a, r1, y1));
            }
            for (int s = 0; s < steps; s++)
            {
                int i = b + s * 2;
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i + 2); tris.Add(i + 1); tris.Add(i + 3);
            }
        }

        public Mesh ToMesh()
        {
            var m = new Mesh();
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            // Wedges must face up
            var n = m.normals;
            if (n.Length > 0 && n[0].y < 0f)
            {
                var t = m.triangles;
                for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                m.triangles = t;
                m.RecalculateNormals();
            }
            m.RecalculateBounds();
            return m;
        }
    }

    // ---- Materials and procedural textures ----

    static Shader standard;
    static Shader StandardShader() =>
        standard != null ? standard : standard = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");

    static Material Mat(Color color, float metallic, float smoothness, Color? emission = null)
    {
        var mat = new Material(StandardShader()) { color = color };
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Glossiness", smoothness);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
        }
        return mat;
    }

    // Lacquered wood: grain runs across each veneer strip, with fine pores
    static Material WoodMaterial(Color dark, Color light, int seed, float smoothness)
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (float)x / size, v = (float)y / size;
            float warp = Mathf.PerlinNoise(seed + u * 2f, v * 1.5f) * 0.6f;
            float grain = Mathf.Abs(Mathf.Sin((u * 14f + warp) * Mathf.PI));
            grain = Mathf.Pow(grain, 2.2f);
            float pores = Mathf.PerlinNoise(seed * 3.1f + u * 80f, v * 6f) * 0.25f;
            float tone = Mathf.Clamp01(0.25f + grain * 0.5f + pores);
            px[y * size + x] = Color.Lerp(dark, light, tone);
        }
        tex.SetPixels(px);
        tex.Apply(true);
        var mat = Mat(Color.white, 0f, smoothness);
        mat.mainTexture = tex;
        return mat;
    }

    static Material FeltMaterial()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat };
        var px = new Color[size * size];
        var baseCol = new Color(0.03f, 0.21f, 0.10f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float n = Mathf.PerlinNoise(x * 0.35f, y * 0.35f) * 0.10f + UnityEngine.Random.value * 0.05f;
            px[y * size + x] = baseCol * (0.9f + n);
        }
        tex.SetPixels(px);
        tex.Apply(true);
        var mat = Mat(Color.white, 0f, 0.08f);
        mat.mainTexture = tex;
        return mat;
    }

    // Dark room with warm ceiling lights — gives the chrome and lacquer believable highlights
    static Cubemap MakeEnvironmentCubemap()
    {
        const int size = 64;
        var cube = new Cubemap(size, TextureFormat.RGBA32, true);
        var lamps = new[] { new Vector3(0.2f, 1f, -0.3f).normalized, new Vector3(-0.7f, 0.6f, 0.4f).normalized, new Vector3(0.8f, 0.5f, 0.5f).normalized };
        foreach (CubemapFace face in Enum.GetValues(typeof(CubemapFace)))
        {
            if (face == CubemapFace.Unknown) continue;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                Vector3 d = FaceDirection(face, u, v).normalized;
                float sky = Mathf.Clamp01(d.y * 0.5f + 0.5f);
                Color c = Color.Lerp(new Color(0.03f, 0.04f, 0.03f), new Color(0.35f, 0.30f, 0.24f), sky * sky);
                foreach (var l in lamps)
                {
                    float spot = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(d, l)), 60f);
                    c += new Color(1f, 0.92f, 0.78f) * spot * 3f;
                }
                px[y * size + x] = c;
            }
            cube.SetPixels(px, face);
        }
        cube.Apply(true);
        return cube;
    }

    static Vector3 FaceDirection(CubemapFace face, float u, float v) => face switch
    {
        CubemapFace.PositiveX => new Vector3(1f, -v, -u),
        CubemapFace.NegativeX => new Vector3(-1f, -v, u),
        CubemapFace.PositiveY => new Vector3(u, 1f, v),
        CubemapFace.NegativeY => new Vector3(u, -1f, -v),
        CubemapFace.PositiveZ => new Vector3(u, -v, 1f),
        _ => new Vector3(-u, -v, -1f),
    };
}
