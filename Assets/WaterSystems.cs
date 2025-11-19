using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider2D))]
public class WaterSystems : MonoBehaviour
{
    [Header("Player Detection")]
    public string playerTag = "Player";

    [Header("SFX")]
    public AudioClip splashSFX;

    [Header("Level Manager")]
    public LevelManager levelManager;

    [Header("On Kill (per player)")]
    public UnityEvent onPlayerKilled;

    [Header("Player Disposal")]
    public bool disablePlayerControl = true;
    public bool destroyPlayerOnKill = true;
    public float destroyDelay = 0.05f;

    void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (col && !col.isTrigger)
        {
            Debug.LogWarning("[WaterSystems] Collider2D is not set to Trigger. Setting it now.");
            col.isTrigger = true;
        }
    }

    void Start()
    {
        if (!levelManager) levelManager = FindFirstObjectByType<LevelManager>();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        GameObject root = other.attachedRigidbody ? other.attachedRigidbody.gameObject : other.gameObject;
        var player = root.GetComponentInParent<PlayerController2D>();
        if (!player) return;

        if (splashSFX) SoundManager.Instance?.PlaySFX(splashSFX);

        // 1) Immediately trigger lose for the entire level
        levelManager?.TriggerLose();

        // 2) (Optional) Clean up the specific player for visuals/consistency
        if (disablePlayerControl)
        {
            player.enabled = false;
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb)
            {
                rb.velocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.isKinematic = true;
            }
        }

        player.gameObject.SetActive(false);
        if (destroyPlayerOnKill)
            Destroy(player.gameObject, destroyDelay);

        onPlayerKilled?.Invoke();
    }
}
