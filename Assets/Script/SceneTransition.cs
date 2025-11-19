using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class SceneTransition : MonoBehaviour
{
    [Header("Transition")]
    public Animator transitionAnimator;
    public string triggerName = "ChangeScene";
    [Tooltip("Used if we can't read the current clip length safely.")]
    public float fallbackDuration = 1f;
    public bool useUnscaledTime = true;

    [Header("SFX (optional)")]
    public AudioClip transitionSFX;  // will be routed via SoundManager->UI if present

    void Awake()
    {
        if (!transitionAnimator)
            transitionAnimator = GetComponent<Animator>();
    }

    void Start()
    {
        SoundManager.Instance.PlayUI(transitionSFX, 1f, 1f);
        Time.timeScale = 1f;
    }

    public void ChangeScene(string sceneName)
    {
        StartCoroutine(Co_Transition(sceneName));
    }

    IEnumerator Co_Transition(string sceneName)
    {
        if (transitionAnimator)
        {
            transitionAnimator.SetTrigger(triggerName);

            // play SFX (prefer SoundManager UI bus)
            if (transitionSFX)
            {
                if (SoundManager.Instance)
                    SoundManager.Instance.PlayUI(transitionSFX, 1f, 1f);
            }

            // wait a frame so animator actually enters the new state
            yield return null;

            // get active clip length; fallback if unavailable
            float wait = fallbackDuration;
            var clips = transitionAnimator.GetCurrentAnimatorClipInfo(0);
            if (clips != null && clips.Length > 0 && clips[0].clip)
                wait = clips[0].clip.length;

            if (useUnscaledTime) yield return new WaitForSecondsRealtime(wait);
            else yield return new WaitForSeconds(wait);
        }

        SceneManager.LoadScene(sceneName);
    }
}
