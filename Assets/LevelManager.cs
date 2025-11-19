using TMPro;
using UnityEngine;
using System.Collections;
using UnityEngine.Events;

public class LevelManager : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI textFishEaten;

    [Header("Win Rules")]
    public int fishToWin = 5;
    [Tooltip("Tag used to find alive player objects.")]
    public string playerTag = "Player";

    [Header("END UI")]
    public GameObject winUI;
    public GameObject loseUI;

    [Header("Audio")]
    public AudioClip growlSFX;
    public AudioClip winSFX;
    public AudioClip loseSFX;

    [Header("Pause System")]
    public PauseSystems pauseSystems;

    [Header("Events")]
    [Tooltip("Invoked once when win condition is met.")]
    public UnityEvent onWin;
    [Tooltip("Invoked once when lose condition is met.")]
    public UnityEvent onLose;

    private int fishEaten = 0;
    private bool hasEnded = false;

    void Start()
    {
        SoundManager.Instance?.StartCoroutine(GrowlSound());
        if (winUI) winUI.SetActive(false);
        if (loseUI) loseUI.SetActive(false);
        changeTextFishEaten();
        EvaluateWinIfNeeded(); // initial check only for win-start states
    }

    // ==== Public API ====
    public int GetFishEaten() => fishEaten;

    public void addFishEaten(int value)
    {
        fishEaten += value;
        changeTextFishEaten();
        EvaluateWinIfNeeded();
    }

    public void resetFishEaten()
    {
        fishEaten = 0;
        changeTextFishEaten();
        hasEnded = false;
        if (winUI) winUI.SetActive(false);
        if (loseUI) loseUI.SetActive(false);
    }

    /// <summary>
    /// Call this to immediately lose the level (e.g., Water touched).
    /// Safe to call multiple times; it only fires once.
    /// </summary>
    public void TriggerLose()
    {
        if (hasEnded) return;
        hasEnded = true;

        if (pauseSystems)
        {
            pauseSystems.canPause = false;
            pauseSystems.SetPausedNoUI(true);
        }

        if (loseUI) loseUI.SetActive(true);
        SoundManager.Instance?.PlaySFX(loseSFX);
        onLose?.Invoke();
        Debug.Log($"[LevelManager] LOSE triggered immediately. fish={fishEaten}/{fishToWin}");
    }

    /// <summary>
    /// Optional: keep this if you still want the “win when last player sleeps & fish goal met” behavior.
    /// Call this after a player is removed by BedSystems.
    /// </summary>
    public void NotifyPlayerRemoved()
    {
        StartCoroutine(EvaluateWinNextFrame());
    }

    // ==== Internals ====
    void changeTextFishEaten()
    {
        if (textFishEaten != null)
            textFishEaten.text = $"{fishEaten} / {fishToWin}";
    }

    System.Collections.IEnumerator EvaluateWinNextFrame()
    {
        yield return null;
        EvaluateWinIfNeeded();
    }

    void EvaluateWinIfNeeded()
    {
        if (hasEnded) return;

        int alivePlayers = CountAlivePlayers();
        bool fishGoalMet = fishEaten >= fishToWin;

        // Preserve your original “win shape”: win only when no players left AND fish goal met.
        if (alivePlayers <= 0 && fishGoalMet)
        {
            hasEnded = true;

            if (pauseSystems)
            {
                pauseSystems.canPause = false;
                pauseSystems.SetPausedNoUI(true);
            }

            if (winUI) winUI.SetActive(true);
            SoundManager.Instance?.PlaySFX(winSFX);
            onWin?.Invoke();
            Debug.Log($"[LevelManager] WIN! players={alivePlayers}, fish={fishEaten}/{fishToWin}");
        }
    }

    int CountAlivePlayers()
    {
        var arr = GameObject.FindGameObjectsWithTag(playerTag);
        return arr?.Length ?? 0;
    }

    IEnumerator GrowlSound()
    {
        // delay 3 seconds before playing growl SFX
        yield return new WaitForSeconds(3f);
        SoundManager.Instance?.PlaySFX(growlSFX);
    }
}
