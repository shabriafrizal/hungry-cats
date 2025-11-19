using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController2D : MonoBehaviour
{
    // ===== EVENTS =====
    public System.Action OnJumped;
    /// <summary>Args: (fallDistanceInUnits, minYVelocityDuringAir)</summary>
    public System.Action<float, float> OnLanded;

    // ===== MOVEMENT & JUMPING =====

    [Header("Move")]
    [Tooltip("Max horizontal speed.")]
    public float moveSpeed = 8f;
    [Tooltip("How fast we reach target speed.")]
    public float acceleration = 60f;
    [Tooltip("How fast we slow down when no input.")]
    public float deceleration = 70f;
    [Tooltip("Extra deceleration while in air (to reduce floaty drift).")]
    public float airDecelMultiplier = 0.5f;

    [Header("Jump")]
    [Tooltip("Initial jump velocity applied once.")]
    public float jumpVelocity = 6f;
    [Tooltip("Extra gravity while falling for snappier feel.")]
    public float fallGravityMultiplier = 2.2f;

    [Header("Leniency")]
    [Tooltip("Time after leaving ground where jump is still allowed.")]
    public float coyoteTime = 0.12f;
    [Tooltip("Time before landing where a queued jump still triggers.")]
    public float jumpBufferTime = 0.12f;

    [Header("Ground Check")]
    public Transform groundCheck;          // place an empty at feet
    public Vector2 groundCheckSize = new Vector2(0.6f, 0.1f);
    public LayerMask groundMask = ~0;

    [Header("Extras")]
    [Tooltip("Optional: allow a single mid-air jump.")]
    public bool allowDoubleJump = false;

    // =========================
    //        SCALE SYSTEM
    // =========================
    public enum PlayerSize { Small, Normal, Big }

    [System.Serializable]
    public struct SizePreset
    {
        public PlayerSize size;
        public Vector2 scaleMultiplier;
        public Vector2 boxSizeMultiplier;
        public Vector2 groundCheckSizeMultiplier;

        [Header("Movement/Jump Multipliers (relative to ORIGINALS)")]
        public float moveSpeedMultiplier;
        public float accelerationMultiplier;
        public float decelerationMultiplier;
        public float jumpVelocityMultiplier;
        public float fallGravityMultiplierMultiplier;
    }

    [Header("Scale Presets (Multipliers from ORIGINAL)")]
    public SizePreset smallPreset = new SizePreset
    {
        size = PlayerSize.Small,
        scaleMultiplier = new Vector2(0.5f, 0.5f),
        boxSizeMultiplier = new Vector2(0.85f, 0.75f),
        groundCheckSizeMultiplier = new Vector2(0.6f, 1f),

        moveSpeedMultiplier = 1.15f,
        accelerationMultiplier = 1.1f,
        decelerationMultiplier = 1.0f,
        jumpVelocityMultiplier = 0.9f,
        fallGravityMultiplierMultiplier = 1.05f
    };

    public SizePreset normalPreset = new SizePreset
    {
        size = PlayerSize.Normal,
        scaleMultiplier = new Vector2(1f, 1f),
        boxSizeMultiplier = new Vector2(1f, 1f),
        groundCheckSizeMultiplier = new Vector2(1f, 1f),

        moveSpeedMultiplier = 1f,
        accelerationMultiplier = 1f,
        decelerationMultiplier = 1f,
        jumpVelocityMultiplier = 1f,
        fallGravityMultiplierMultiplier = 1f
    };

    public SizePreset bigPreset = new SizePreset
    {
        size = PlayerSize.Big,
        scaleMultiplier = new Vector2(1.5f, 1.5f),
        boxSizeMultiplier = new Vector2(1.25f, 1.15f),
        groundCheckSizeMultiplier = new Vector2(1.5f, 1f),

        moveSpeedMultiplier = 0.85f,
        accelerationMultiplier = 0.9f,
        decelerationMultiplier = 1.05f,
        jumpVelocityMultiplier = 1.2f,
        fallGravityMultiplierMultiplier = 0.95f
    };

    // Cached components / baselines
    Rigidbody2D _rb;
    BoxCollider2D _box;          // optional
    Vector2 _originalScale2D;    // from transform.localScale at Awake
    Vector2 _originalBoxSize;    // from BoxCollider2D.size at Awake (if any)
    Vector2 _originalGCSize;     // from groundCheckSize at Awake

    float _originalMoveSpeed;
    float _originalAcceleration;
    float _originalDeceleration;
    float _originalJumpVelocity;
    float _originalFallGravityMultiplier;

    // Move/jump state
    float _inputX;
    float _coyoteCounter;
    float _bufferCounter;
    bool _hasUsedDoubleJump;

    // Landing detect state
    bool _wasGrounded;
    float _airStartY;
    float _minYVelWhileAir;

    // ===== Animation & Facing =====
    [Header("Animation")]
    [Tooltip("Animator with bools: isWalking, isJumping, isLanding")]
    public Animator animator;
    [Tooltip("SpriteRenderer to flip by direction. Defaults to self or child.")]
    public SpriteRenderer spriteRenderer;
    [Tooltip("Speed threshold to count as walking.")]
    public float walkSpeedThreshold = 0.1f;

    [Tooltip("Deadzone for reading left/right input when deciding facing.")]
    public float inputFaceThreshold = 0.2f;
    [Tooltip("Deadzone for reading velocity when airborne to decide facing.")]
    public float velFaceThreshold = 0.3f;

    [Header("Eat")]
    public bool alreadyAte = false;

    // Animator parameter IDs
    static readonly int ID_isWalking = Animator.StringToHash("isWalking");
    static readonly int ID_isJumping = Animator.StringToHash("isJumping");
    static readonly int ID_isLanding = Animator.StringToHash("isLanding");

    int _lastFacingDir = 1; // +1 = right, -1 = left
    bool _forceJumpBool;    // set true when DoJump(); cleared when grounded

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _box = GetComponent<BoxCollider2D>();

        if (_rb.interpolation == RigidbodyInterpolation2D.None) _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        if (_rb.collisionDetectionMode != CollisionDetectionMode2D.Continuous) _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        _originalScale2D = new Vector2(transform.localScale.x, transform.localScale.y);
        if (_box) _originalBoxSize = _box.size;
        _originalGCSize = groundCheckSize;

        _originalMoveSpeed = moveSpeed;
        _originalAcceleration = acceleration;
        _originalDeceleration = deceleration;
        _originalJumpVelocity = jumpVelocity;
        _originalFallGravityMultiplier = fallGravityMultiplier;

        if (!groundCheck)
        {
            GameObject feet = new GameObject("Feet (auto)");
            feet.transform.SetParent(transform, false);
            feet.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            groundCheck = feet.transform;
        }

        // auto-find visuals
        if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        if (!animator) animator = GetComponentInChildren<Animator>(true);

        // Hook events to drive animator flags
        OnJumped += HandleOnJumped;
        OnLanded += HandleOnLanded;
    }

    void Update()
    {
        // ----- INPUT -----
        _inputX = Input.GetAxisRaw("Horizontal");   // -1..1 using A/D or Left/Right
        bool jumpDown = Input.GetKeyDown(KeyCode.W); // Jump is W (example)

        if (jumpDown) _bufferCounter = jumpBufferTime;
        else _bufferCounter -= Time.unscaledDeltaTime;

        bool grounded = IsGrounded();

        // Track coyote time
        if (grounded)
        {
            _coyoteCounter = coyoteTime;
            _hasUsedDoubleJump = false; // reset when touching ground
        }
        else
        {
            _coyoteCounter -= Time.unscaledDeltaTime;
        }

        // Air state bookkeeping for landing event
        if (!grounded)
        {
            if (_wasGrounded) // just left ground
            {
                _airStartY = transform.position.y;
                _minYVelWhileAir = 0f; // becomes negative (downwards)
            }
            if (_rb.velocity.y < _minYVelWhileAir) _minYVelWhileAir = _rb.velocity.y;
        }

        // Handle jump press (buffer + coyote)
        if (_bufferCounter > 0f)
        {
            if (_coyoteCounter > 0f)
            {
                DoJump();
                _bufferCounter = 0f;
            }
            else if (allowDoubleJump && !_hasUsedDoubleJump)
            {
                DoJump();
                _hasUsedDoubleJump = true;
                _bufferCounter = 0f;
            }
        }

        // Landing event: transition air -> ground
        if (grounded && !_wasGrounded)
        {
            float fallDistance = _airStartY - transform.position.y; // positive if fell
            OnLanded?.Invoke(fallDistance, _minYVelWhileAir);
        }
        _wasGrounded = grounded;

        // ===== Flip sprite with deadzones + memory =====
        if (spriteRenderer)
        {
            float dir = 0f;

            if (grounded)
            {
                // on ground: prefer input, only update if clear enough
                if (Mathf.Abs(_inputX) >= inputFaceThreshold)
                    dir = Mathf.Sign(_inputX);
            }
            else
            {
                // in air: prefer actual motion, only if moving clear enough
                if (Mathf.Abs(_rb.velocity.x) >= velFaceThreshold)
                    dir = Mathf.Sign(_rb.velocity.x);
                else if (Mathf.Abs(_inputX) >= inputFaceThreshold)
                    dir = Mathf.Sign(_inputX); // intention fallback
            }

            if (dir != 0f) _lastFacingDir = (int)dir;
            spriteRenderer.flipX = (_lastFacingDir < 0);
        }

        // ===== Drive animator booleans =====
        if (animator)
        {
            bool isWalking = grounded &&
                             Mathf.Abs(_rb.velocity.x) > walkSpeedThreshold &&
                             Mathf.Abs(_inputX) > 0.01f;

            // Jumping while rising OR right after we pressed jump
            bool isJumping = !grounded && (_forceJumpBool || _rb.velocity.y > 0.01f);

            // 'Landing' means falling/downward while airborne
            bool isLanding = !grounded && _rb.velocity.y < -0.01f;

            animator.SetBool(ID_isWalking, isWalking);
            animator.SetBool(ID_isJumping, isJumping);
            animator.SetBool(ID_isLanding, isLanding);

            if (grounded)
            {
                _forceJumpBool = false;
                if (animator.GetBool(ID_isJumping)) animator.SetBool(ID_isJumping, false);
                if (animator.GetBool(ID_isLanding)) animator.SetBool(ID_isLanding, false);
            }
        }
    }

    void FixedUpdate()
    {
        // ----- HORIZONTAL MOVE -----
        float targetSpeed = _inputX * moveSpeed;
        float speedDiff = targetSpeed - _rb.velocity.x;

        float accelRate = (Mathf.Abs(targetSpeed) > 0.01f)
            ? acceleration
            : deceleration * (IsGrounded() ? 1f : airDecelMultiplier);

        float movement = Mathf.Clamp(speedDiff * accelRate, -999f, 999f) * Time.fixedDeltaTime;
        _rb.velocity = new Vector2(_rb.velocity.x + movement, _rb.velocity.y);

        // ----- BETTER GRAVITY -----
        if (_rb.velocity.y < -0.01f) // falling
        {
            _rb.velocity += Vector2.up * (Physics2D.gravity.y * (fallGravityMultiplier - 1f) * Time.fixedDeltaTime);
        }
    }

    void DoJump()
    {
        Vector2 v = _rb.velocity;
        v.y = jumpVelocity;
        _rb.velocity = v;

        _coyoteCounter = 0f;
        _forceJumpBool = true;
        OnJumped?.Invoke();
    }

    bool IsGrounded()
    {
        if (groundCheck == null)
            return Physics2D.OverlapBox(transform.position + Vector3.down * 0.1f, groundCheckSize, 0f, groundMask);
        else
            return Physics2D.OverlapBox(groundCheck.position, groundCheckSize, 0f, groundMask);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.35f);
        Vector3 pos = groundCheck ? groundCheck.position : transform.position + Vector3.down * 0.1f;
        Gizmos.DrawCube(pos, groundCheckSize);
    }

    // ===== Public helpers =====
    public bool IsGroundedPublic() => IsGrounded();

    // -------------------------
    // CLEAN SCALE API
    // -------------------------
    [System.Serializable] public struct _SizePresetMarker { }

    public void ApplySize(SizePreset preset)
    {
        // 1) Transform scale
        Vector3 finalScale = new Vector3(
            _originalScale2D.x * preset.scaleMultiplier.x,
            _originalScale2D.y * preset.scaleMultiplier.y,
            transform.localScale.z
        );
        transform.localScale = finalScale;

        // 2) BoxCollider2D (if present)
        if (_box)
        {
            _box.size = new Vector2(
                _originalBoxSize.x * preset.boxSizeMultiplier.x,
                _originalBoxSize.y * preset.boxSizeMultiplier.y
            );
        }

        // 3) Ground check region
        groundCheckSize = new Vector2(
            _originalGCSize.x * preset.groundCheckSizeMultiplier.x,
            _originalGCSize.y * preset.groundCheckSizeMultiplier.y
        );

        // 4) Parameter adjustments (movement/jump)
        moveSpeed = _originalMoveSpeed * Mathf.Max(0.01f, preset.moveSpeedMultiplier);
        acceleration = _originalAcceleration * Mathf.Max(0.01f, preset.accelerationMultiplier);
        deceleration = _originalDeceleration * Mathf.Max(0.01f, preset.decelerationMultiplier);
        jumpVelocity = _originalJumpVelocity * Mathf.Max(0.01f, preset.jumpVelocityMultiplier);
        fallGravityMultiplier = _originalFallGravityMultiplier * Mathf.Max(0.01f, preset.fallGravityMultiplierMultiplier);
    }

    public bool CanPlayerEat()
    {
        return !alreadyAte;
    }

    public void ApplyScaleMultiplier(Vector2 scaleMultiplier)
    {
        ApplySize(new SizePreset
        {
            size = PlayerSize.Normal,
            scaleMultiplier = scaleMultiplier,
            boxSizeMultiplier = scaleMultiplier,
            groundCheckSizeMultiplier = new Vector2(scaleMultiplier.x, 1f),

            moveSpeedMultiplier = 1f,
            accelerationMultiplier = 1f,
            decelerationMultiplier = 1f,
            jumpVelocityMultiplier = 1f,
            fallGravityMultiplierMultiplier = 1f
        });
    }

    public void SetPlayerToSmall() => ApplySize(smallPreset);
    public void SetPlayerToNormal() => ApplySize(normalPreset);
    public void SetPlayerToBig() => ApplySize(bigPreset);

    // SendMessage-friendly overloads (for FoodSystems)
    public void SetPlayerToSmall(FoodSystems _) => SetPlayerToSmall();
    public void SetPlayerToNormal(FoodSystems _) => SetPlayerToNormal();
    public void SetPlayerToBig(FoodSystems _) => SetPlayerToBig();

    // ===== Animator event handlers =====
    void HandleOnJumped()
    {
        if (animator) animator.SetBool(ID_isJumping, true);
    }

    void HandleOnLanded(float fallDistance, float minYVel)
    {
        if (animator)
        {
            // Grounded => no longer jumping or falling
            animator.SetBool(ID_isJumping, false);
            animator.SetBool(ID_isLanding, false);
        }
    }
}
