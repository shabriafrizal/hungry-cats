using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[DefaultExecutionOrder(-10000)]
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Mixer (assign in Inspector)")]
    public AudioMixer mixer;                  // optional, but recommended
    public AudioMixerGroup mixerMaster;
    public AudioMixerGroup mixerMusic;
    public AudioMixerGroup mixerSFX;
    public AudioMixerGroup mixerUI;

    [Header("Exposed Mixer Params (names must match in your mixer)")]
    public string masterVolParam = "MasterVolume";   // expects dB
    public string musicVolParam = "MusicVolume";
    public string sfxVolParam = "SFXVolume";
    public string uiVolParam = "UIVolume";

    [Header("Pooling (for SFX/UI one-shots)")]
    [Min(1)] public int sfxPoolSize = 12;
    [Min(1)] public int uiPoolSize = 8;

    [Header("Music")]
    public float defaultMusicFade = 0.75f;

    public AudioClip mainMusicClip;    // optional intro clip

    readonly Queue<AudioSource> _sfxFree = new Queue<AudioSource>();
    readonly Queue<AudioSource> _uiFree = new Queue<AudioSource>();
    readonly List<AudioSource> _sfxAll = new List<AudioSource>();
    readonly List<AudioSource> _uiAll = new List<AudioSource>();
    AudioSource _musicA, _musicB; // for crossfading
    bool _musicAActive;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildPool(_sfxFree, _sfxAll, sfxPoolSize, mixerSFX, "SFXSource");
        BuildPool(_uiFree, _uiAll, uiPoolSize, mixerUI, "UISource");

        // Music dual-source
        _musicA = CreateSource("MusicA", mixerMusic, loop: true);
        _musicB = CreateSource("MusicB", mixerMusic, loop: true);

        // Start main music if assigned
        if (mainMusicClip)
            PlayMusic(mainMusicClip, fade: 0f, loop: true, targetVolume: 0.1f);
    }

    AudioSource CreateSource(string name, AudioMixerGroup group, bool loop = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        var src = go.AddComponent<AudioSource>();
        src.outputAudioMixerGroup = group ? group : mixerMaster;
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = 0f;
        return src;
    }

    void BuildPool(Queue<AudioSource> freeQ, List<AudioSource> all, int count, AudioMixerGroup group, string namePrefix)
    {
        for (int i = 0; i < count; i++)
        {
            var src = CreateSource($"{namePrefix}_{i}", group);
            all.Add(src);
            freeQ.Enqueue(src);
        }
    }

    AudioSource Grab(Queue<AudioSource> freeQ, List<AudioSource> all, AudioMixerGroup group, string namePrefix)
    {
        // Reclaim any finished sources
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var s = all[i];
            if (!s.isPlaying && !freeQ.Contains(s)) freeQ.Enqueue(s);
        }

        if (freeQ.Count > 0) return freeQ.Dequeue();

        // If all busy, create one more (burst-safe)
        var extra = CreateSource($"{namePrefix}_X{all.Count}", group);
        all.Add(extra);
        return extra;
    }

    // -------------------------
    // Public API
    // -------------------------

    /// <summary>Play a 2D UI click/hover, routed to UI group.</summary>
    public void PlayUI(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (!clip) return;
        var src = Grab(_uiFree, _uiAll, mixerUI, "UISource");
        ConfigureAndPlay(src, clip, volume, pitch, Vector3.zero, is3D: false);
    }

    /// <summary>Play a SFX at world position (3D) or 2D if position is null.</summary>
    public void PlaySFX(AudioClip clip, Vector3? worldPos = null, float volume = 1f, float pitch = 1f, float spatialBlend = 1f)
    {
        if (!clip) return;
        var src = Grab(_sfxFree, _sfxAll, mixerSFX, "SFXSource");
        bool is3D = worldPos.HasValue;
        ConfigureAndPlay(src, clip, volume, pitch, worldPos ?? Vector3.zero, is3D, spatialBlend);
    }

    void ConfigureAndPlay(AudioSource src, AudioClip clip, float volume, float pitch, Vector3 pos, bool is3D, float spatialBlend = 0f)
    {
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.pitch = Mathf.Clamp(pitch, 0.01f, 3f);
        src.transform.position = pos;
        src.spatialBlend = is3D ? Mathf.Clamp01(spatialBlend) : 0f;
        src.minDistance = 1f;
        src.maxDistance = 25f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.Play();
    }

    /// <summary>Crossfade to music clip. If same clip already playing, does nothing.</summary>
    public void PlayMusic(AudioClip clip, float fade = -1f, bool loop = true, float targetVolume = 1f, float pitch = 1f)
    {
        if (!clip) { StopMusic(fade < 0f ? defaultMusicFade : fade); return; }

        fade = (fade < 0f) ? defaultMusicFade : fade;

        var from = _musicAActive ? _musicA : _musicB;
        var to = _musicAActive ? _musicB : _musicA;
        _musicAActive = !_musicAActive;

        to.Stop();
        to.clip = clip;
        to.loop = loop;
        to.pitch = pitch;
        to.volume = 0f;
        to.Play();

        if (fade <= 0f)
        {
            from.Stop();
            to.volume = targetVolume;
        }
        else
        {
            StopAllCoroutines();
            StartCoroutine(FadeMusic(from, to, fade, targetVolume));
        }
    }

    public void StopMusic(float fade = -1f)
    {
        fade = (fade < 0f) ? defaultMusicFade : fade;
        var from = _musicA.isPlaying ? _musicA : _musicB.isPlaying ? _musicB : null;
        if (!from) return;

        if (fade <= 0f)
        {
            from.Stop();
            return;
        }
        StopAllCoroutines();
        StartCoroutine(FadeMusic(from, null, fade, 0f));
    }

    System.Collections.IEnumerator FadeMusic(AudioSource from, AudioSource to, float duration, float toTarget)
    {
        float t = 0f;
        float fromStart = from ? from.volume : 0f;
        float toStart = to ? to.volume : 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float a = t / duration;
            if (from) from.volume = Mathf.Lerp(fromStart, 0f, a);
            if (to) to.volume = Mathf.Lerp(toStart, toTarget, a);
            yield return null;
        }
        if (from) { from.volume = 0f; from.Stop(); }
        if (to) to.volume = toTarget;
    }

    // ------------- Volumes (linear 0..1 -> dB) -------------
    public void SetMasterVolume(float linear01) => SetDb(masterVolParam, linear01);
    public void SetMusicVolume(float linear01) => SetDb(musicVolParam, linear01);
    public void SetSFXVolume(float linear01) => SetDb(sfxVolParam, linear01);
    public void SetUIVolume(float linear01) => SetDb(uiVolParam, linear01);

    public float GetMasterVolume() => GetVolume01(masterVolParam);
    public float GetMusicVolume() => GetVolume01(musicVolParam);
    public float GetSFXVolume() => GetVolume01(sfxVolParam);
    public float GetUIVolume() => GetVolume01(uiVolParam);

    void SetDb(string param, float linear01)
    {
        if (!mixer || string.IsNullOrEmpty(param)) return;
        float v = Mathf.Clamp01(linear01);
        // Convert linear [0..1] to dB [-80..0] (mute at ~-80 dB)
        float dB = v > 0.0001f ? Mathf.Log10(v) * 20f : -80f;
        mixer.SetFloat(param, dB);
    }

    public float GetVolume01(string param)
    {
        if (!mixer || string.IsNullOrEmpty(param)) return 1f;
        if (!mixer.GetFloat(param, out float dB)) return 1f;
        return Mathf.Clamp01(Mathf.Pow(10f, dB / 20f));
    }
}
