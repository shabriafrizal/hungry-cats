using UnityEngine;

public class QuitGame : MonoBehaviour
{
    [Header("Optional")]
    public bool confirmWithEscape = true;   // press Esc to quit (Android back button maps to Escape)
    public bool savePlayerPrefs = true;

    void Update()
    {
        if (confirmWithEscape && Input.GetKeyDown(KeyCode.Escape))
            Quit();
    }

    public void Quit()
    {
        if (savePlayerPrefs) PlayerPrefs.Save();

#if UNITY_EDITOR
        // Stop Play Mode in Editor
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
        // WebGL cannot close the tab; consider showing a "Thanks for playing!" panel instead.
        Debug.Log("Quit requested - not supported on WebGL. Show exit UI instead.");
#else
        // Standalone / Mobile
        Application.Quit();
#endif
    }
}
