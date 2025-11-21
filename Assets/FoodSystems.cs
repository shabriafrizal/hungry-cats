using UnityEngine;
using UnityEngine.Events;

[AddComponentMenu("Game/Interactables/Food Systems (Single Sprite Swap + Call Other)")]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))] // must be Trigger
public class FoodSystems : MonoBehaviour
{
    [System.Serializable] public class FoodEvent : UnityEvent<FoodSystems> { }
    [System.Serializable] public class OtherGOEvent : UnityEvent<GameObject> { }

    [Header("Player Detection")]
    public string playerTag = "Player";
    public KeyCode changeKey = KeyCode.S;

    [Header("UI / Hint")]
    [Tooltip("Shown when player is inside the trigger; hidden on exit or after swap.")]
    public GameObject showOnEnter;

    [Header("Sprite Swap")]
    [Tooltip("Sprite to set when pressing the key inside the trigger.")]
    public Sprite newSprite;

    public enum PlayerSideEffect { None, MakeSmall, MakeBig, Toggle }

    [Header("Player Size Action (Optional)")]
    [Tooltip("What to do to the player when this food is swapped.")]
    public PlayerSideEffect sideEffectPlayer = PlayerSideEffect.None;

    [Header("SFX")]
    public AudioClip eatSFX;

    [Header("UnityEvents (Inspector-assignable)")]
    public FoodEvent onEnter;         // passes this FoodSystems
    public FoodEvent onExit;          // passes this FoodSystems
    public FoodEvent onSwapped;       // passes this FoodSystems
    public OtherGOEvent onEnterOther; // passes other GameObject
    public OtherGOEvent onExitOther;  // passes other GameObject
    public OtherGOEvent onSwapOther;  // passes other GameObject

    [Header("Level Manager")]
    public LevelManager levelManager;

    // Internal
    SpriteRenderer _sr;
    Sprite _original;
    bool _playerInside;
    bool _changed;

    // Cache for the current player in trigger (robust for child colliders)
    Collider2D _currentTriggerCol;
    PlayerController2D _currentPlayerController;
    PlayerSizeController2D _currentPlayerSizeController;

    void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _original = _sr.sprite;

        var col = GetComponent<Collider2D>();
        if (col && !col.isTrigger)
        {
            Debug.LogWarning("[FoodSystems] Collider2D is not Trigger. Forcing isTrigger = true.");
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
        if (!_playerInside || _changed) return;

        if (Input.GetKeyDown(changeKey))
        {

            // Cegah makan jika player sudah pernah makan
            if (_currentPlayerController && !_currentPlayerController.CanPlayerEat())
            {
                return;
            }

            // Guard: need a target sprite
            if (!newSprite)
            {
                Debug.LogWarning("[FoodSystems] New sprite not assigned.");
                return;
            }

            // SFX (optional)
            SoundManager.Instance?.PlaySFX(eatSFX);

            // Swap sprite and mark as done
            _sr.sprite = newSprite;
            _changed = true;

            // Hide hint after swap
            if (showOnEnter) showOnEnter.SetActive(false);

            // Mark player ate + size side effect + counters + events
            if (_currentPlayerController)
            {
                _currentPlayerController.SetAlreadyAte(true);           // <- important flag for BedSystems
                ApplySideEffectToPlayer(_currentPlayerSizeController);
            }

            if (levelManager != null)
            {
                // Assuming your LM increments like this (keep your original method name)
                levelManager.addFishEaten(1);
            }

            onSwapped?.Invoke(this);
            if (_currentPlayerController) onSwapOther?.Invoke(_currentPlayerController.gameObject);
            else if (_currentTriggerCol) onSwapOther?.Invoke(_currentTriggerCol.gameObject);
        }
    }

    // ---------- Trigger (2D) ----------
    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        _playerInside = true;
        _currentTriggerCol = other;

        // Resolve player robustly (feet/body child colliders supported)
        GameObject root = other.attachedRigidbody ? other.attachedRigidbody.gameObject : other.gameObject;
        _currentPlayerController = root.GetComponentInParent<PlayerController2D>();
        _currentPlayerSizeController = root.GetComponentInParent<PlayerSizeController2D>();


        if (showOnEnter && !_changed) showOnEnter.SetActive(true);

        onEnter?.Invoke(this);
        onEnterOther?.Invoke(root);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        // Only clear if the exiting collider belongs to the same player
        var exitingRB = other.attachedRigidbody;
        var cachedRB = _currentPlayerController ? _currentPlayerController.GetComponent<Rigidbody2D>() : null;

        bool samePlayer =
            (exitingRB && cachedRB && exitingRB == cachedRB) ||
            (!exitingRB && _currentPlayerController && other.transform.IsChildOf(_currentPlayerController.transform));

        if (samePlayer)
        {
            onExit?.Invoke(this);
            onExitOther?.Invoke(_currentPlayerController ? _currentPlayerController.gameObject : other.gameObject);

            _playerInside = false;
            if (showOnEnter) showOnEnter.SetActive(false);
            _currentTriggerCol = null;
            _currentPlayerController = null;
            _currentPlayerSizeController = null;
        }
    }

    // ---------- Helpers ----------
    void ApplySideEffectToPlayer(PlayerSizeController2D psc)
    {
        if (!psc) return;

        switch (sideEffectPlayer)
        {
            case PlayerSideEffect.MakeSmall:
                psc.SetSmall();
                break;

            case PlayerSideEffect.MakeBig:
                psc.SetBig();
                break;

            case PlayerSideEffect.Toggle:
                // Simple toggle heuristic:
                // If roughly <= normal scale -> go BIG, else go SMALL.
                // (Works fine with your default 1x, 0.5x, 1.5x presets.)
                float sx = psc.transform.localScale.x;
                if (sx <= 1.01f) psc.SetBig();
                else psc.SetSmall();
                break;

            case PlayerSideEffect.None:
            default:
                break;
        }
    }
}
