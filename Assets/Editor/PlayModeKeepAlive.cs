using UnityEditor;

// Editor Play Mode ticks off the Editor's own idle loop, not a dedicated render
// thread. If the Game view window sits with zero mouse/keyboard input for a stretch
// (common while just watching a spin play out), that loop can stop firing entirely,
// freezing every Time.deltaTime-driven coroutine mid-lerp — the wheel spin lands on
// whatever pocket it happened to be rotating through, not the real result.
//
// QueuePlayerLoopUpdate is the API Unity documents specifically for this case (keep
// the player loop running while the Editor isn't focused). Two earlier versions of
// this file broke things:
//   - Calling EditorWindow.Repaint() on the Game view every tick triggered
//     "PlayerLoop internal function has been called recursively" and corrupted UI
//     construction.
//   - Calling QueuePlayerLoopUpdate() unconditionally every single tick ALSO
//     triggered the same recursive-PlayerLoop error (confirmed by disabling this
//     method's body and reproducing cleanly, then re-enabling and reproducing the
//     crash again) — almost certainly fighting with the MCP bridge's own
//     EditorApplication.update hook for player-loop control.
// Throttling to one queued update per ~100ms real time avoids the reentrancy while
// still being far more than enough to keep an 8s spin animation from stalling.
[InitializeOnLoad]
static class PlayModeKeepAlive
{
    const double MinIntervalSeconds = 0.1;
    static double lastQueueTime;

    static PlayModeKeepAlive()
    {
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
        double now = EditorApplication.timeSinceStartup;
        if (now - lastQueueTime < MinIntervalSeconds) return;
        lastQueueTime = now;
        EditorApplication.QueuePlayerLoopUpdate();
    }
}
