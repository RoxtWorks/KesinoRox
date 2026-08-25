using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Central hub for one-off "juice" effects — camera shake, full-screen color flash,
// confetti burst — so every feature that wants a screen-shake or a win-flash calls
// one shared place instead of each owning its own coroutine/camera-reference plumbing.
public class JuiceManager : MonoBehaviour
{
    Transform cameraTransform;
    Quaternion cameraBaseRot;
    Vector3 cameraBasePos;
    Image flashOverlay;
    ParticleSystem confetti;
    Coroutine shakeRoutine;
    Light keyLight;
    float keyLightBaseIntensity;

    public void Build(Transform canvas, Transform cameraTransform, Vector3 confettiWorldPos, Light keyLight = null)
    {
        this.cameraTransform = cameraTransform;
        cameraBaseRot = cameraTransform.localRotation;
        cameraBasePos = cameraTransform.localPosition;
        this.keyLight = keyLight;
        if (keyLight != null) keyLightBaseIntensity = keyLight.intensity;

        // Full-screen wash, topmost UI layer, click-through — a quick color pulse
        // under everything else without blocking any button.
        var flashGO = new GameObject("FlashOverlay");
        flashGO.transform.SetParent(canvas, false);
        var rt = flashGO.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        flashOverlay = flashGO.AddComponent<Image>();
        flashOverlay.color = new Color(0f, 0f, 0f, 0f);
        flashOverlay.raycastTarget = false;
        flashGO.transform.SetAsLastSibling();

        BuildConfetti(confettiWorldPos);
    }

    // Legacy ParticleSystem, not VFX Graph — this project runs the Built-in Render
    // Pipeline, and VFX Graph only renders under URP/HDRP. The old particle system
    // is fully Built-in-RP compatible and gets the same "burst of color" result.
    void BuildConfetti(Vector3 worldPos)
    {
        var go = new GameObject("ConfettiBurst");
        go.transform.position = worldPos;
        confetti = go.AddComponent<ParticleSystem>();
        // A freshly-added ParticleSystem defaults to playOnAwake=true and can start
        // playing the instant it's enabled — before the code below finishes
        // configuring it, which throws "Setting the duration while system is still
        // playing" the moment main.duration is set. Force it stopped first.
        confetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = confetti.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1.2f;
        main.startLifetime = 1.1f;
        main.startSpeed = 5f;
        main.startSize = 0.12f;
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(1f, 0.85f, 0.3f));
        main.gravityModifier = 1.1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = confetti.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });

        var shape = confetti.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.3f;
        shape.rotation = new Vector3(-90f, 0f, 0f); // cone points up

        var colorOverLifetime = confetti.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.8f, 0.3f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = grad;

        var renderer = confetti.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Standard"));
        renderer.material = mat;
    }

    // intensity > 1 layers extra particles on top of the base burst — used to make a
    // 35:1 straight-up hit visibly bigger than a min even-money win instead of both
    // getting the identical confetti puff.
    public void PlayConfetti(float intensity = 1f)
    {
        if (confetti == null) return;
        confetti.Play();
        if (intensity > 1f) confetti.Emit(Mathf.RoundToInt(60 * (intensity - 1f)));
    }

    // Rotation-based rather than position-based — this scene's camera looks almost
    // straight down, where a small positional jolt barely reads; a tiny rotational
    // wobble is visible from any angle.
    public void Shake(float duration, float magnitudeDeg)
    {
        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(ShakeRoutine(duration, magnitudeDeg));
    }

    IEnumerator ShakeRoutine(float duration, float magnitudeDeg)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float damper = 1f - t / duration;
            float rx = Random.Range(-1f, 1f) * magnitudeDeg * damper;
            float ry = Random.Range(-1f, 1f) * magnitudeDeg * damper;
            float rz = Random.Range(-1f, 1f) * magnitudeDeg * damper;
            cameraTransform.localRotation = cameraBaseRot * Quaternion.Euler(rx, ry, rz);
            yield return null;
        }
        cameraTransform.localRotation = cameraBaseRot;
    }

    // Fired once per belt tick — deliberately NOT stacked/queued, each call just
    // restarts a very short decaying shake, which reads as continuous jitter that
    // eases off between ticks rather than a a queue of shakes fighting each other.
    public void MicroShake(float strength) => Shake(0.08f, 0.18f * strength);

    // Reserved for the top celebration tier only — a brief real-light brightening on
    // the actual 3D wheel, not just a screen-space overlay, so a huge win visibly
    // lights up the table itself for a moment.
    public void PulseLight(float boost, float duration)
    {
        if (keyLight != null) StartCoroutine(PulseLightRoutine(boost, duration));
    }

    IEnumerator PulseLightRoutine(float boost, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            keyLight.intensity = keyLightBaseIntensity + boost * Mathf.Sin(u * Mathf.PI);
            yield return null;
        }
        keyLight.intensity = keyLightBaseIntensity;
    }

    public void Flash(Color color, float duration)
    {
        StartCoroutine(FlashRoutine(color, duration));
    }

    IEnumerator FlashRoutine(Color color, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI) * color.a;
            flashOverlay.color = new Color(color.r, color.g, color.b, alpha);
            yield return null;
        }
        flashOverlay.color = new Color(color.r, color.g, color.b, 0f);
    }
}
