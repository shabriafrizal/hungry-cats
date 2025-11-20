using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Game/FX/Player FX 2D (Footsteps, Jump, Landing + Meow)")]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerFX2D : MonoBehaviour
{
    // ===== INSPECTOR FIELDS =====

    [Header("References")]
    [SerializeField] private PlayerController2D controller;   // auto-found if null (still used for grounded state + events)
    [SerializeField] private Transform feet;                  // default = controller.groundCheck (fallback = transform)

    [Header("FX Ground Check (Local to FX)")]
    [Tooltip("Local ground check size used by FX (gizmos and fallback tests). World units.")]
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.6f, 0.1f);
    [Tooltip("Draw gizmo at feet/groundCheck using this FX size.")]
    [SerializeField] private bool drawGroundGizmo = true;

    [Header("Footsteps")]
    [Tooltip("Pool of footstep clips, one chosen randomly per step.")]
    [SerializeField] private List<AudioClip> footstepClips = new();  // choose 1 random per step
    [Tooltip("Min horizontal speed to start playing footsteps.")]
    [SerializeField] private float minFootstepSpeed = 0.2f;
    [Tooltip("Interval when walking slowly.")]
    [SerializeField] private float stepIntervalSlow = 0.5f;
    [Tooltip("Interval at full moveSpeed.")]
    [SerializeField] private float stepIntervalFast = 0.24f;
    [Tooltip("Scale step volume by speed (0..1).")]
    [SerializeField] private bool scaleFootstepBySpeed = true;
    [SerializeField] private float footstepBaseVolume = 0.9f;

    [Header("Meow (Footstep-style)")]
    [Tooltip("Pool of meow clips to trigger at step cadence.")]
    [SerializeField] private List<AudioClip> meowClips = new();
    [Tooltip("Chance (0..1) to meow on a given step tick.")]
    [Range(0f, 1f)]
    [SerializeField] private float meowChancePerStep = 0.15f;
    [Tooltip("If true, meow replaces the footstep on that tick. If false, it layers with footstep.")]
    [SerializeField] private bool meowReplaceFootstep = true;
    [Tooltip("Meow volume (linear 0..1).")]
    [Range(0f, 1f)]
    [SerializeField] private float meowVolume = 1f;
    [Tooltip("Random pitch range for meow (min..max).")]
    [SerializeField] private Vector2 meowPitchRange = new Vector2(0.96f, 1.06f);

    [Header("Jump")]
    [SerializeField] private AudioClip jumpClip;
    [SerializeField] private ParticleSystem jumpParticles;    // prefab or scene object

    [Header("Landing")]
    [SerializeField] private AudioClip landSoftClip;
    [SerializeField] private AudioClip landHardClip;
    [SerializeField] private ParticleSystem landSoftParticles;
    [SerializeField] private ParticleSystem landHardParticles;

    [Header("Landing Block Thresholds")]
    [Tooltip("Size of one 'block' in world units (e.g., 1u = 1 tile).")]
    [SerializeField] private float blockHeight = 1f;
    [Tooltip("Soft landing triggers when fall distance >= this many blocks.")]
    [SerializeField] private float softLandingBlocks = 2f;
    [Tooltip("Hard landing triggers when fall distance >= this many blocks.")]
    [SerializeField] private float hardLandingBlocks = 5f;

    [Header("Landing Filters (Optional)")]
    [Tooltip("Ignore tiny landings unless these minimums are met.")]
    [SerializeField] private bool requireMinFallForVFX = true;
    [Tooltip("Minimum fall distance in blocks for any VFX/SFX to play.")]
    [SerializeField] private float minVFXBlocks = 0.25f;
    [Tooltip("Minimum downward speed (|vy|) for any VFX/SFX to play.")]
    [SerializeField] private float minVFXSpeedY = 2.0f;

    // ===== RUNTIME FIELDS =====

    private Rigidbody2D rb;
    private float stepTimer;
    private float meowCooldownTimer;

    // ===== UNITY LIFECYCLE =====

    private void Reset()
    {
        controller = GetComponent<PlayerController2D>();
        rb = GetComponent<Rigidbody2D>();
    }

    private void Awake()
    {
        if (!controller) controller = GetComponent<PlayerController2D>();
        rb = GetComponent<Rigidbody2D>();

        // feet spawn defaults to controller.groundCheck → fallback to transform
        if (!feet)
        {
            feet = (controller && controller.GroundCheck)
                ? controller.GroundCheck
                : transform;
        }
    }

    private void OnEnable()
    {
        if (controller == null) return;

        controller.OnJumped += HandleJump;
        controller.OnLanded += HandleLanded;
    }

    private void OnDisable()
    {
        if (controller == null) return;

        controller.OnJumped -= HandleJump;
        controller.OnLanded -= HandleLanded;
    }

    private void Update()
    {
        // Keep feet following controller.groundCheck live if it changes
        if (controller && controller.GroundCheck && feet != controller.GroundCheck)
            feet = controller.GroundCheck;

        meowCooldownTimer -= Time.deltaTime;
        HandleFootsteps(Time.deltaTime);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGroundGizmo) return;

        // Draw using this FX's own GroundCheckSize, at the same feet position used by FX
        Transform p = feet;
        if (!p && controller) p = controller.GroundCheck;
        if (!p) p = transform;

        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.25f);
        Gizmos.DrawCube(p.position, (Vector3)groundCheckSize);
    }

    // ===== FOOTSTEPS & MEOW =====

    private void HandleFootsteps(float deltaTime)
    {
        if (!IsGrounded())
        {
            stepTimer = 0f;
            return;
        }

        float speed = Mathf.Abs(rb.velocity.x);
        if (speed < minFootstepSpeed)
        {
            stepTimer = 0f;
            return;
        }

        // Interval blends from slow -> fast based on speed/moveSpeed
        float t = (controller && controller.MoveSpeed > 0f)
            ? Mathf.Clamp01(speed / controller.MoveSpeed)
            : 1f;

        float interval = Mathf.Lerp(stepIntervalSlow, stepIntervalFast, t);

        stepTimer -= deltaTime;
        if (stepTimer > 0f) return;

        stepTimer = interval;

        bool didMeow = TryMeow();
        if (!didMeow || !meowReplaceFootstep)
        {
            PlayFootstep(t);
        }
    }

    private bool TryMeow()
    {
        if (meowCooldownTimer > 0f) return false;
        if (meowClips == null || meowClips.Count == 0) return false;
        if (Random.value > meowChancePerStep) return false;

        AudioClip clip = meowClips[Random.Range(0, meowClips.Count)];
        float pitch = Mathf.Lerp(meowPitchRange.x, meowPitchRange.y, Random.value);

        SoundManager.Instance?.PlaySFX(
            clip,
            feet ? feet.position : transform.position,
            meowVolume,
            pitch,
            0f
        );

        // Randomized cooldown window keeps it cute and non-repetitive
        meowCooldownTimer = Random.Range(5.0f, 20.0f);
        return true;
    }

    private void PlayFootstep(float speedT01)
    {
        if (footstepClips == null || footstepClips.Count == 0) return;

        AudioClip clip = footstepClips[Random.Range(0, footstepClips.Count)];
        float volume = scaleFootstepBySpeed
            ? Mathf.Lerp(0.6f, footstepBaseVolume, speedT01)
            : footstepBaseVolume;

        SoundManager.Instance?.PlaySFX(
            clip,
            feet ? feet.position : transform.position,
            volume,
            1f,
            0f
        );
    }

    // ===== JUMP =====

    private void HandleJump()
    {
        if (jumpClip)
        {
            SoundManager.Instance?.PlaySFX(
                jumpClip,
                feet ? feet.position : transform.position,
                1f,
                1f,
                0f
            );
        }

        SpawnAndPlay(jumpParticles, feet ? feet.position : transform.position, 0f);
    }

    // ===== LANDING =====
    private void HandleLanded(float fallDistanceInUnits, float minYVelocity)
    {
        // ===== EXTRA ANTI-BUG SAFETY =====
        // Cegah landing palsu (groundCheck terlalu besar atau scale player kecil)
        if (!controller.IsGroundedPublic())
            return;

        // Cegah landing palsu saat baru turun sedikit
        if (fallDistanceInUnits < 0.05f)
            return;

        // Cegah landing palsu bila kecepatan turun terlalu kecil
        if (Mathf.Abs(minYVelocity) < 0.1f)
            return;

        // Konversi jarak jatuh ke blok
        float blocks = (blockHeight > 0.0001f)
            ? (fallDistanceInUnits / blockHeight)
            : Mathf.Infinity;

        // ===== FILTER TAMBAHAN =====
        if (requireMinFallForVFX)
        {
            bool tooSmall = blocks < minVFXBlocks;
            bool tooSlow = Mathf.Abs(minYVelocity) < minVFXSpeedY;

            if (tooSmall || tooSlow)
                return;
        }

        // Posisi partikel harus sedikit di bawah player
        Vector3 pos = feet ? feet.position : transform.position;

        // ===== HARD LANDING =====
        if (blocks >= hardLandingBlocks)
        {
            if (landHardClip)
            {
                SoundManager.Instance?.PlaySFX(landHardClip, pos, 1f, 1f, 0f);
            }

            SpawnAndPlay(landHardParticles, pos, 0f);
            return;
        }

        // ===== SOFT LANDING =====
        if (blocks >= softLandingBlocks)
        {
            if (landSoftClip)
            {
                SoundManager.Instance?.PlaySFX(landSoftClip, pos, 0.9f, 1f, 0f);
            }

            SpawnAndPlay(landSoftParticles, pos, 0f);
        }
    }


    // ===== INTERNAL UTIL =====

    private bool IsGrounded()
    {
        // Prefer controller’s grounded—keeps FX in sync with gameplay logic.
        if (controller) return controller.IsGroundedPublic();

        // Fallback (rare): use our local FX groundCheckSize at feet/transform.
        Vector3 pos = feet ? feet.position : transform.position;
        return Physics2D.OverlapBox(pos, groundCheckSize, 0f);
    }

    private void SpawnAndPlay(ParticleSystem prefabOrInstance, Vector3 pos, float zRotationDeg = 0f)
    {
        if (!prefabOrInstance) return;

        Quaternion rot = Quaternion.Euler(0f, 0f, zRotationDeg);

        // If it's already in the scene → reuse instance
        if (prefabOrInstance.gameObject.scene.IsValid())
        {
            ParticleSystem ps = prefabOrInstance;
            Transform t = ps.transform;
            t.SetPositionAndRotation(pos, rot);

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(true);
            ps.Simulate(0f, true, true, true);
            ps.Play(true);
        }
        else
        {
            // If it's a prefab → instantiate temporary instance
            ParticleSystem ps = Instantiate(prefabOrInstance, pos, rot);
            ps.Play(true);
            Destroy(ps.gameObject, EstimateTotalLifetime(ps));
        }
    }

    private float EstimateTotalLifetime(ParticleSystem ps)
    {
        ParticleSystem.MainModule m = ps.main;

        float lifetimeMax = m.startLifetime.constantMax > 0f
            ? m.startLifetime.constantMax
            : 1.0f;

        float startDelay = m.startDelay.constantMax;
        return m.duration + lifetimeMax + startDelay + 0.5f;
    }

    // ===== PUBLIC API =====

    /// <summary>Optional: external abilities can change FX ground-check size.</summary>
    public void SetFxGroundCheckSize(Vector2 size)
    {
        groundCheckSize = new Vector2(
            Mathf.Max(0.02f, size.x),
            Mathf.Max(0.02f, size.y)
        );
    }

    /// <summary>Force a single meow SFX (bypasses cooldown).</summary>
    public void PlayMeowOneShot()
    {
        meowCooldownTimer = 0f; // bypass cooldown if you want; remove this line if not
        TryMeow();
    }
}
