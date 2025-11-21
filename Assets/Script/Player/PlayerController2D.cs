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

    // ===== INSPECTOR FIELDS =====

    [Header("Move")]
    [Tooltip("Max horizontal speed.")]
    [SerializeField] private float moveSpeed = 8f;
    [Tooltip("How fast we reach target speed.")]
    [SerializeField] private float acceleration = 60f;
    [Tooltip("How fast we slow down when no input.")]
    [SerializeField] private float deceleration = 70f;
    [Tooltip("Extra deceleration while in air (to reduce floaty drift).")]
    [SerializeField] private float airDecelMultiplier = 0.5f;

    [Header("Jump")]
    [Tooltip("Initial jump velocity applied once.")]
    [SerializeField] private float jumpVelocity = 6f;
    [Tooltip("Extra gravity while falling for snappier feel.")]
    [SerializeField] private float fallGravityMultiplier = 2.2f;

    [Header("Leniency")]
    [Tooltip("Time after leaving ground where jump is still allowed.")]
    [SerializeField] private float coyoteTime = 0.12f;
    [Tooltip("Time before landing where a queued jump still triggers.")]
    [SerializeField] private float jumpBufferTime = 0.12f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;          // place an empty at feet
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.6f, 0.1f);
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Grounded Hold")]
    [Tooltip("Berapa lama status grounded stabil dipertahankan setelah lepas dari ground (anti jitter untuk FX).")]
    [SerializeField] private float groundedHoldDuration = 1f;

    [Header("Extras")]
    [Tooltip("Optional: allow a single mid-air jump.")]
    [SerializeField] private bool allowDoubleJump = false;

    // ===== RUNTIME FIELDS =====
    // Cached components / baselines
    private Rigidbody2D rb;
    private BoxCollider2D box;          // optional
    private Vector2 originalScale2D;    // from transform.localScale at Awake
    private Vector2 originalBoxSize;    // from BoxCollider2D.size at Awake (if any)
    private Vector2 originalGCSize;     // from groundCheckSize at Awake

    private float originalMoveSpeed;
    private float originalAcceleration;
    private float originalDeceleration;
    private float originalJumpVelocity;
    private float originalFallGravityMultiplier;

    // Move/jump state
    private float inputX;
    private float coyoteCounter;
    private float bufferCounter;
    private bool hasUsedDoubleJump;
    private bool jumpPressedThisFrame;

    // Ground state
    private bool rawGrounded;          // hasil OverlapBox langsung (bisa jitter)
    private bool stableGrounded;       // versi stabil (di-hold)
    private float groundedHoldTimer;
    private bool wasGroundedRaw;
    private bool wasGroundedStable;

    // Landing detect state
    private float airStartY;
    private float minYVelWhileAir;

    // ===== Animation & Facing =====
    [Header("Animation")]
    [Tooltip("Animator with bools: isWalking, isJumping, isLanding")]
    [SerializeField] private Animator animator;
    [Tooltip("SpriteRenderer to flip by direction. Defaults to self or child.")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("Speed threshold to count as walking.")]
    [SerializeField] private float walkSpeedThreshold = 0.1f;

    [Tooltip("Deadzone for reading left/right input when deciding facing.")]
    [SerializeField] private float inputFaceThreshold = 0.2f;
    [Tooltip("Deadzone for reading velocity when airborne to decide facing.")]
    [SerializeField] private float velFaceThreshold = 0.3f;

    [Header("Eat")]
    [SerializeField] private bool alreadyAte = false;

    // Animator parameter IDs
    private static readonly int ID_isWalking = Animator.StringToHash("isWalking");
    private static readonly int ID_isJumping = Animator.StringToHash("isJumping");
    private static readonly int ID_isLanding = Animator.StringToHash("isLanding");

    private int lastFacingDir = 1; // +1 = right, -1 = left
    private bool forceJumpBool;    // set true when DoJump(); cleared when grounded

    // ===== UNITY LIFECYCLE =====
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        box = GetComponent<BoxCollider2D>();

        if (rb.interpolation == RigidbodyInterpolation2D.None)
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        if (rb.collisionDetectionMode != CollisionDetectionMode2D.Continuous)
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        originalScale2D = new Vector2(transform.localScale.x, transform.localScale.y);
        if (box) originalBoxSize = box.size;
        originalGCSize = groundCheckSize;

        originalMoveSpeed = moveSpeed;
        originalAcceleration = acceleration;
        originalDeceleration = deceleration;
        originalJumpVelocity = jumpVelocity;
        originalFallGravityMultiplier = fallGravityMultiplier;

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

        // initial grounded state
        rawGrounded = CheckGrounded();
        stableGrounded = rawGrounded;
        groundedHoldTimer = groundedHoldDuration;
        wasGroundedRaw = rawGrounded;
        wasGroundedStable = stableGrounded;

        // Hook events to drive animator flags
        OnJumped += HandleOnJumped;
        OnLanded += HandleOnLanded;
    }

    private void Update()
    {
        ReadInput();
        UpdateGroundState();        // hitung raw + grounded stabil (hold)
        HandleJumpBuffer();
        HandleCoyoteTime();         // pakai rawGrounded
        HandleJumpLogic();
        UpdateAirState();           // pakai rawGrounded
        HandleLandingEvent();       // pakai stableGrounded
        UpdateFacingDirection();    // pakai rawGrounded
        UpdateAnimatorStates();     // pakai rawGrounded
    }

    private void FixedUpdate()
    {
        HandleHorizontalMovement();
        ApplyBetterGravity();
    }

    // ===== PUBLIC API / HELPERS =====
    /// <summary>
    /// Grounded versi stabil (dipakai FX / script lain).
    /// </summary>
    public bool IsGroundedPublic() => stableGrounded;

    [System.Serializable]
    private struct SizePresetMarker { }

    public bool CanPlayerEat() => !alreadyAte;

    public Transform GroundCheck => groundCheck;
    public float MoveSpeed => moveSpeed;
    public bool AlreadyAte => alreadyAte;
    public void SetAlreadyAte(bool value) => alreadyAte = value;


    // ===== PRIVATE LOGIC =====

    private void ReadInput()
    {
        inputX = Input.GetAxisRaw("Horizontal");   // -1..1 using A/D or Left/Right
        // Ganti sesuai mappingmu, sementara contoh pakai W
        jumpPressedThisFrame = Input.GetKeyDown(KeyCode.W);
    }

    private void UpdateGroundState()
    {
        // Hitung raw grounded via OverlapBox (bisa jitter)
        rawGrounded = CheckGrounded();

        // Update grounded stabil dengan hold timer (hanya utk FX/landing)
        if (rawGrounded)
        {
            stableGrounded = true;
            groundedHoldTimer = groundedHoldDuration;
        }
        else
        {
            if (stableGrounded)
            {
                groundedHoldTimer -= Time.unscaledDeltaTime;
                if (groundedHoldTimer <= 0f)
                {
                    stableGrounded = false;
                }
            }
        }
    }

    private void HandleJumpBuffer()
    {
        if (jumpPressedThisFrame)
        {
            bufferCounter = jumpBufferTime;
        }
        else
        {
            bufferCounter -= Time.unscaledDeltaTime;
        }
    }

    private void HandleCoyoteTime()
    {
        // Untuk coyote dan reset double jump, pakai rawGrounded
        if (rawGrounded)
        {
            coyoteCounter = coyoteTime;
            hasUsedDoubleJump = false;
        }
        else
        {
            coyoteCounter -= Time.unscaledDeltaTime;
        }
    }

    private void HandleJumpLogic()
    {
        if (bufferCounter <= 0f)
            return;

        if (coyoteCounter > 0f)
        {
            DoJump();
            bufferCounter = 0f;
        }
        else if (allowDoubleJump && !hasUsedDoubleJump)
        {
            DoJump();
            hasUsedDoubleJump = true;
            bufferCounter = 0f;
        }
    }

    private void UpdateAirState()
    {
        // Air-state untuk hitung jarak jatuh pakai rawGrounded
        if (!rawGrounded)
        {
            if (wasGroundedRaw) // baru saja lepas dari ground
            {
                airStartY = transform.position.y;
                minYVelWhileAir = 0f; // nanti jadi negatif
            }

            if (rb.velocity.y < minYVelWhileAir)
                minYVelWhileAir = rb.velocity.y;
        }
    }

    private void HandleLandingEvent()
    {
        // Event landing pakai grounded stabil (supaya tidak jitter)
        if (stableGrounded && !wasGroundedStable)
        {
            float fallDistance = airStartY - transform.position.y; // positive if fell
            OnLanded?.Invoke(fallDistance, minYVelWhileAir);
        }

        // Simpan state sebelumnya untuk frame berikutnya
        wasGroundedRaw = rawGrounded;
        wasGroundedStable = stableGrounded;
    }

    private void UpdateFacingDirection()
    {
        if (!spriteRenderer)
            return;

        float dir = 0f;

        if (rawGrounded)
        {
            // on ground: prefer input, only update if clear enough
            if (Mathf.Abs(inputX) >= inputFaceThreshold)
                dir = Mathf.Sign(inputX);
        }
        else
        {
            // in air: prefer actual motion, only if moving clear enough
            if (Mathf.Abs(rb.velocity.x) >= velFaceThreshold)
                dir = Mathf.Sign(rb.velocity.x);
            else if (Mathf.Abs(inputX) >= inputFaceThreshold)
                dir = Mathf.Sign(inputX); // intention fallback
        }

        if (dir != 0f) lastFacingDir = (int)dir;
        spriteRenderer.flipX = (lastFacingDir < 0);
    }

    private void UpdateAnimatorStates()
    {
        if (!animator)
            return;

        // Anim pakai rawGrounded supaya responsif
        bool isWalkingAnim = rawGrounded &&
                             Mathf.Abs(rb.velocity.x) > walkSpeedThreshold &&
                             Mathf.Abs(inputX) > 0.01f;

        bool isJumpingAnim = !rawGrounded && (forceJumpBool || rb.velocity.y > 0.01f);
        bool isLandingAnim = !rawGrounded && rb.velocity.y < -0.01f;

        animator.SetBool(ID_isWalking, isWalkingAnim);
        animator.SetBool(ID_isJumping, isJumpingAnim);
        animator.SetBool(ID_isLanding, isLandingAnim);

        // Begitu benar-benar menyentuh ground lagi (raw), reset state
        if (rawGrounded)
        {
            forceJumpBool = false;
            animator.SetBool(ID_isLanding, false);
            // isJumpingAnim juga akan otomatis false karena rawGrounded == true
        }
    }

    private void HandleHorizontalMovement()
    {
        float targetSpeed = inputX * moveSpeed;
        float speedDiff = targetSpeed - rb.velocity.x;

        float accelRate = (Mathf.Abs(targetSpeed) > 0.01f)
            ? acceleration
            : deceleration * (rawGrounded ? 1f : airDecelMultiplier);

        float movement = Mathf.Clamp(speedDiff * accelRate, -999f, 999f) * Time.fixedDeltaTime;
        rb.velocity = new Vector2(rb.velocity.x + movement, rb.velocity.y);
    }

    private void ApplyBetterGravity()
    {
        if (rb.velocity.y < -0.01f) // falling
        {
            rb.velocity += Vector2.up * (Physics2D.gravity.y * (fallGravityMultiplier - 1f) * Time.fixedDeltaTime);
        }
    }

    private void DoJump()
    {
        Vector2 v = rb.velocity;
        v.y = jumpVelocity;
        rb.velocity = v;

        coyoteCounter = 0f;
        forceJumpBool = true;

        // Loncat = langsung dianggap tidak grounded (dua-duanya)
        rawGrounded = false;
        stableGrounded = false;
        groundedHoldTimer = 0f;

        OnJumped?.Invoke();
    }

    private bool CheckGrounded()
    {
        if (groundCheck == null)
        {
            return Physics2D.OverlapBox(
                transform.position + Vector3.down * 0.1f,
                groundCheckSize,
                0f,
                groundMask
            );
        }
        else
        {
            var hit = Physics2D.OverlapBox(groundCheck.position, groundCheckSize, 0f, groundMask);
            return hit != null;
        }
    }

    // ===== GIZMOS =====
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.35f);
        Vector3 pos = groundCheck ? groundCheck.position : transform.position + Vector3.down * 0.1f;
        Gizmos.DrawCube(pos, groundCheckSize);
    }

    // ===== CALLBACKS / EVENT HANDLERS =====
    private void HandleOnJumped()
    {
        if (animator) animator.SetBool(ID_isJumping, true);
    }

    private void HandleOnLanded(float fallDistance, float minYVel)
    {
        if (animator)
        {
            // Grounded => no longer jumping or falling
            animator.SetBool(ID_isJumping, false);
            animator.SetBool(ID_isLanding, false);
        }
    }
}
