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

    public static void Load(string sceneName)
    {
        if (cachedProfile == null)
            cachedProfile = Resources.Load<TransitionProfile>(ProfilePath);

        if (cachedProfile == null)
        {
            // Fallback so navigation still works even if the profile failed to load.
            SceneManager.LoadScene(sceneName);
            return;
        }

        TransitionAnimator.Start(cachedProfile, sceneNameToLoad: sceneName);
    }
}
