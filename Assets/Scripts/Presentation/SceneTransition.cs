using TransitionsPlus;
using UnityEngine;
using UnityEngine.SceneManagement;

// Every cross-scene nav (menu <-> games) routes through here instead of a raw
// SceneManager.LoadScene, so the whole project gets the same fade transition for
// free instead of a hard cut. Profile lives in Resources so it's loadable from
// pure code with no scene-authored reference.
public static class SceneTransition
{
    const string ProfilePath = "Transitions/MenuTransition";
    static TransitionProfile cachedProfile;

    static TransitionProfile GetProfile()
    {
        if (cachedProfile == null)
            cachedProfile = Resources.Load<TransitionProfile>(ProfilePath);
        return cachedProfile;
    }

    public static void Load(string sceneName)
    {
        var profile = GetProfile();
        if (profile == null)
        {
            // Fallback so navigation still works even if the profile failed to load.
            SceneManager.LoadScene(sceneName);
            return;
        }

        // Plays forward (covers the screen), then swaps scenes once fully covered —
        // see Reveal() below for the other half of this transition.
        profile.invert = false;
        TransitionAnimator.Start(profile, sceneNameToLoad: sceneName);
    }

    // Call once from a scene's own Start(), after everything in it is actually
    // built, to play the matching "uncover" half instead of the new scene just
    // hard-cutting into view. The transition instance that covered the screen in
    // Load() gets destroyed the instant LoadScene swaps scenes (Unity tears down
    // every object in the old scene, transition overlay included), so this has to
    // be a brand-new instance started fresh here — there's nothing left over from
    // the cover to continue. profile.invert flips playback direction: instead of
    // starting clear and animating to fully covered, it starts fully covered
    // (matching exactly how the previous scene just left the screen) and animates
    // to clear, revealing this scene the same way it was hidden going in.
    public static void Reveal()
    {
        var profile = GetProfile();
        if (profile == null) return;
        profile.invert = true;
        TransitionAnimator.Start(profile);
    }
}
