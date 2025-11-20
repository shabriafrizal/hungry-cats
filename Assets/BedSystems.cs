using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider2D))]
public class BedSystems : MonoBehaviour
{
    [Header("Player Detection")]
    public string playerTag = "Player";
    public KeyCode changeKey = KeyCode.S;

    [Header("UI / Hint")]
    [Tooltip("Shown when player is inside the trigger; hidden on exit or after sleep.")]
    public GameObject showOnEnter;

    [Header("Sprite Swap")]
    [Tooltip("Sprite to set when pressing the key inside the trigger.")]
    public Sprite newSprite;

    [Header("SFX")]
    public AudioClip sleepSFX;

    [Header("Level Manager")]
    public LevelManager levelManager;

    [Header("On Sleep")]
    [Tooltip("Invoked when sleep is confirmed (after SFX is triggered).")]
    public UnityEvent onSlept;

    [Header("Player Disposal")]
    [Tooltip("Disable player control before removing (if found).")]
    public bool disablePlayerControl = true;
    [Tooltip("Destroy the player object after sleeping.")]
    public bool destroyPlayerOnSleep = true;
    [Tooltip("Delay before destroying the player object (seconds).")]
    public float destroyDelay = 0.15f;

    // Internal
    bool _playerInside;
    bool _slept;

    // We cache BOTH the collider we entered with (for exit comparisons) and the player controller
    SpriteRenderer _sr;
    Sprite _original;
    Collider2D _currentTriggerCollider;
    PlayerController2D _currentPlayer;

    void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _original = _sr.sprite;

        var col = GetComponent<Collider2D>();
        if (col && !col.isTrigger)
        {
            Debug.LogWarning("[BedSystems] Collider2D is not set to Trigger. Setting it now.");
            col.isTrigger = true;
        }
    }

    void Start()
    {
        if (!levelManager) levelManager = FindFirstObjectByType<LevelManager>();
        if (showOnEnter) showOnEnter.SetActive(false);
    }

    void Update()
    {
        // If no player inside or already slept, nothing to do
        if (!_playerInside || _slept) return;

        // Decide if we can sleep now:
        // - If we have a LevelManager: require GetFishEaten >= fishToWin
        // - And this specific player's alreadyAte must be true
        bool lmOK = (levelManager == null) || (levelManager.GetFishEaten() >= levelManager.fishToWin);
        bool playerOK = _currentPlayer && _currentPlayer.AlreadyAte;
        bool canSleepNow = lmOK && playerOK;

        // Live toggle hint based on current eligibility
        if (showOnEnter && showOnEnter.activeSelf != canSleepNow)
            showOnEnter.SetActive(canSleepNow);

        // Confirm sleep
        if (canSleepNow && Input.GetKeyDown(changeKey))
        {
            // Guard: need a target sprite
            if (!newSprite)
            {
                Debug.LogWarning("[FoodSystems] New sprite not assigned.");
                return;
            }

            if (sleepSFX) SoundManager.Instance?.PlaySFX(sleepSFX);

            // Swap sprite and mark as done
            _sr.sprite = newSprite;
            _slept = true;

            if (showOnEnter) showOnEnter.SetActive(false);
            onSlept?.Invoke();

            // Remove current player safely
            if (_currentPlayer)
            {
                GameObject target = _currentPlayer.gameObject;

                if (disablePlayerControl)
                {
                    _currentPlayer.enabled = false;

                    var rb = _currentPlayer.GetComponent<Rigidbody2D>();
                    if (rb)
                    {
                        rb.velocity = Vector2.zero;
                        rb.angularVelocity = 0f;
                        rb.isKinematic = true; // avoid late physics pushes during cleanup
                    }
                }

                // Deactivate immediately so LevelManager logic updates right away
                target.SetActive(false);

                // Notify LevelManager to re-evaluate win condition (if your LM supports this)
                levelManager?.NotifyPlayerRemoved();

                // Optionally destroy after a short delay
                if (destroyPlayerOnSleep)
                    Destroy(target, destroyDelay);
            }
        }
    }

    // ---------- Trigger (2D) ----------
    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        _playerInside = true;
        _currentTriggerCollider = other;

        // Resolve the player root and component robustly:
        // - Prefer attachedRigidbody root if present
        // - Then search up the hierarchy
        GameObject root = other.attachedRigidbody ? other.attachedRigidbody.gameObject : other.gameObject;
        _currentPlayer = root.GetComponentInParent<PlayerController2D>();

        // Initial hint state upon entry
        if (showOnEnter && !_slept)
        {
            bool lmOK = (levelManager == null) || (levelManager.GetFishEaten() >= levelManager.fishToWin);
            bool playerOK = _currentPlayer && _currentPlayer.AlreadyAte;
            showOnEnter.SetActive(lmOK && playerOK);
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        // Only clear if the exiting collider belongs to the same player we cached
        // Compare by shared Rigidbody (safest for multi-collider characters)
        var exitingRB = other.attachedRigidbody;
        var cachedRB = _currentPlayer ? _currentPlayer.GetComponent<Rigidbody2D>() : null;

        bool samePlayer =
            (exitingRB && cachedRB && exitingRB == cachedRB) ||
            (!exitingRB && _currentPlayer && other.transform.IsChildOf(_currentPlayer.transform));

        if (samePlayer)
        {
            _playerInside = false;
            if (showOnEnter) showOnEnter.SetActive(false);
            _currentTriggerCollider = null;
            _currentPlayer = null;
        }
    }
}
