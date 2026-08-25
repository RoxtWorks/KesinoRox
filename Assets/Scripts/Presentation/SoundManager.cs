using UnityEngine;

// Thin SFX player — loads clips from Resources/Sounds at runtime (this project has
// no serialized prefabs/scene objects to hang AudioClip fields off, everything is
// built in code) and fires them via a single PlayOneShot source, which mixes
// overlapping plays automatically (needed for the belt's rapid tick sound).
public class SoundManager : MonoBehaviour
{
    AudioSource source;
    // Tsk gets its own source — AudioSource.pitch is shared by every PlayOneShot call
    // on that source, including ones still ringing out. Randomizing pitch on the
    // shared source would detune chip/win/lose sounds mid-playback if they happened
    // to overlap a tick; a dedicated source keeps that randomization contained to
    // ticks only, where it's needed most (the same sound fires dozens of times a spin).
    AudioSource tickSource;
    AudioSource musicSource;
    AudioClip chip, click, win, lose, reset, addMoney, tsk;

    public void Build()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;

        tickSource = gameObject.AddComponent<AudioSource>();
        tickSource.playOnAwake = false;

        chip = Resources.Load<AudioClip>("Sounds/Chip");
        click = Resources.Load<AudioClip>("Sounds/Click");
        win = Resources.Load<AudioClip>("Sounds/Win");
        lose = Resources.Load<AudioClip>("Sounds/Lose");
        reset = Resources.Load<AudioClip>("Sounds/Reset");
        addMoney = Resources.Load<AudioClip>("Sounds/AddMoney");
        tsk = Resources.Load<AudioClip>("Sounds/Tsk");

        // Separate source from the one-shot SFX source — music needs to loop and
        // sit at its own (lower) volume rather than firing-and-forgetting like a
        // sound effect.
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.volume = 0.175f;
        var music = Resources.Load<AudioClip>("Music/26 5am - Goodbye _ Towball's Crossing");
        if (music != null) musicSource.clip = music;
    }

    // Calling Play() in the same call as AddComponent<AudioSource> silently doesn't
    // latch — isPlaying reads back false even though everything (clip, loop, volume)
    // is set correctly. Kept as a separate call fired after the rest of the scene
    // finishes building, which reliably works.
    public void PlayMusic()
    {
        if (musicSource.clip != null && !musicSource.isPlaying) musicSource.Play();
    }

    // Global mute — AudioListener.volume is a single engine-wide knob, so toggling
    // it here mutes music and every SFX source at once regardless of which scene's
    // SoundManager instance made the call. Persisted so muting carries across scene
    // loads and the next session, instead of resetting on every game switch.
    const string MutePrefKey = "RouletteSim_Muted";

    public static bool IsMuted => AudioListener.volume <= 0f;

    public static void ApplyPersistedMuteState()
    {
        AudioListener.volume = PlayerPrefs.GetInt(MutePrefKey, 0) == 1 ? 0f : 1f;
    }

    public static bool ToggleMute()
    {
        bool nowMuted = !IsMuted;
        AudioListener.volume = nowMuted ? 0f : 1f;
        PlayerPrefs.SetInt(MutePrefKey, nowMuted ? 1 : 0);
        PlayerPrefs.Save();
        return nowMuted;
    }

    public void PlayChip() => Play(chip);
    public void PlayClick() => Play(click);
    public void PlayWin() => Play(win);
    public void PlayLose() => Play(lose);
    public void PlayReset() => Play(reset);
    public void PlayAddMoney() => Play(addMoney);

    // Fires once per pocket crossed during an 8s spin — at full volume it stacks up
    // and dominates the mix, so it's turned down 40% relative to every other one-shot.
    // Wider pitch range than the others since it repeats so often within one spin.
    public void PlayTsk()
    {
        if (tsk == null) return;
        tickSource.pitch = Random.Range(0.88f, 1.12f);
        tickSource.PlayOneShot(tsk, 0.3f);
    }

    void Play(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null) return;
        // Small pitch variance so repeated plays (chip stacking, several spins in a
        // row) don't sound like the exact same robotic sample every time.
        source.pitch = Random.Range(0.96f, 1.04f);
        source.PlayOneShot(clip, volumeScale);
    }
}
