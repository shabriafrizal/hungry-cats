using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Playables; // for PlayableDirector (Timeline)

[DisallowMultipleComponent]
public class PauseSystems : MonoBehaviour
{
    [Header("General")]
    public bool canPause = true;

    [Header("UI")]
    [Tooltip("Root GameObject of your pause panel/canvas.")]
    public GameObject pauseUI;
    [Tooltip("Optional: UI to select when paused (e.g., Resume Button).")]
    public Selectable firstSelected;

    [Header("Controls")]
    public KeyCode toggleKey = KeyCode.Escape;

    [Header("Audio")]
    [Tooltip("Optional SFX when toggling pause.")]
    public AudioClip pauseSFX;

    [Header("Keep Animating While Paused (Unscaled Time)")]
    [Tooltip("Animators that should continue playing while paused.")]
    public Animator[] unscaledAnimators;
    [Tooltip("Timelines that should continue while paused.")]
    public PlayableDirector[] unscaledTimelines;
    [Tooltip("Particle Systems that should continue while paused.")]
    public ParticleSystem[] unscaledParticles;

    public static bool IsPaused { get; private set; }

    void Awake()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        if (pauseUI) pauseUI.SetActive(false);
    }

    void Update()
    {
        if (!canPause) return;

        if (Input.GetKeyDown(toggleKey))
            Toggle();
    }

    public void Toggle()
    {
        if (pauseSFX) SoundManager.Instance.PlayUI(pauseSFX, 1f, 1f);
        SetPaused(!IsPaused);
    }

    // For your Resume button
    public void Resume()
    {
        if (IsPaused) Toggle();
    }

    public void SetPaused(bool paused)
    {
        if (IsPaused == paused) return;

        IsPaused = paused;
        Time.timeScale = paused ? 0f : 1f;

        // UI show/hide
        if (pauseUI) pauseUI.SetActive(paused);

        // Optional: focus a button when paused
        if (paused && firstSelected)
        {
            EventSystem.current?.SetSelectedGameObject(null);
            firstSelected.Select();
        }

        // === Unscaled systems ===
        // 1) Animators
        if (unscaledAnimators != null)
        {
            foreach (var a in unscaledAnimators)
            {
                if (!a) continue;
                a.updateMode = paused ? AnimatorUpdateMode.UnscaledTime : AnimatorUpdateMode.Normal;
            }
        }

        // 2) Timeline
        if (unscaledTimelines != null)
        {
            foreach (var d in unscaledTimelines)
            {
                if (!d) continue;
                d.timeUpdateMode = paused
                    ? DirectorUpdateMode.UnscaledGameTime
                    : DirectorUpdateMode.GameTime;
            }
        }

        // 3) Particles
        if (unscaledParticles != null)
        {
            foreach (var ps in unscaledParticles)
            {
                if (!ps) continue;
                var main = ps.main;
                main.useUnscaledTime = paused;
            }
        }
    }

    public void SetPausedNoUI(bool paused)
    {
        if (IsPaused == paused) return;

        IsPaused = paused;
        Time.timeScale = paused ? 0f : 1f;

        // === Unscaled systems ===
        // 1) Animators
        if (unscaledAnimators != null)
        {
            foreach (var a in unscaledAnimators)
            {
                if (!a) continue;
                a.updateMode = paused ? AnimatorUpdateMode.UnscaledTime : AnimatorUpdateMode.Normal;
            }
        }

        // 2) Timeline
        if (unscaledTimelines != null)
        {
            foreach (var d in unscaledTimelines)
            {
                if (!d) continue;
                d.timeUpdateMode = paused
                    ? DirectorUpdateMode.UnscaledGameTime
                    : DirectorUpdateMode.GameTime;
            }
        }

        // 3) Particles
        if (unscaledParticles != null)
        {
            foreach (var ps in unscaledParticles)
            {
                if (!ps) continue;
                var main = ps.main;
                main.useUnscaledTime = paused;
            }
        }
    }
}
