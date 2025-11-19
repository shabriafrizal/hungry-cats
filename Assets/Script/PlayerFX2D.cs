using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Game/FX/Player FX 2D (Footsteps, Jump, Landing + Meow)")]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerFX2D : MonoBehaviour
{
    [Header("References")]
    public PlayerController2D controller;   // auto-found if null (still used for grounded state + events)
    public Transform feet;                  // default = controller.groundCheck (fallback = transform)

    [Header("FX Ground Check (Local to FX)")]
    [Tooltip("Local ground check size used by FX (gizmos and fallback tests). World units.")]
    public Vector2 groundCheckSize = new Vector2(0.6f, 0.1f);
    [Tooltip("Draw gizmo at feet/groundCheck using this FX size.")]
    public bool drawGroundGizmo = true;

    [Header("Footsteps")]
    public List<AudioClip> footstepClips = new();  // choose 1 random per step
    [Tooltip("Min horizontal speed to start playing footsteps.")]
    public float minFootstepSpeed = 0.2f;
    [Tooltip("Interval when walking slowly.")]
    public float stepIntervalSlow = 0.5f;
    [Tooltip("Interval at full moveSpeed.")]
    public float stepIntervalFast = 0.24f;
    [Tooltip("Scale step volume by speed (0..1).")]
    public bool scaleFootstepBySpeed = true;
    public float footstepBaseVolume = 0.9f;

    [Header("Meow (Footstep-style)")]
    [Tooltip("Pool of meow clips to trigger at step cadence.")]
    public List<AudioClip> meowClips = new();
    [Tooltip("Chance (0..1) to meow on a given step tick.")]
    [Range(0f, 1f)] public float meowChancePerStep = 0.15f;
    [Tooltip("If true, meow replaces the footstep on that tick. If false, it layers with footstep.")]
    public bool meowReplaceFootstep = true;
    [Tooltip("Meow volume (linear 0..1).")]
    [Range(0f, 1f)] public float meowVolume = 1f;
    [Tooltip("Random pitch range for meow (min..max).")]
    public Vector2 meowPitchRange = new Vector2(0.96f, 1.06f);

    [Header("Jump")]
    public AudioClip jumpClip;
    public ParticleSystem jumpParticles;    // prefab or scene object

    [Header("Landing")]
    public AudioClip landSoftClip;
    public AudioClip landHardClip;
    public ParticleSystem landSoftParticles;
    public ParticleSystem landHardParticles;

    [Header("Landing Block Thresholds")]
    [Tooltip("Size of one 'block' in world units (e.g., 1u = 1 tile).")]
    public float blockHeight = 1f;
    [Tooltip("Soft landing triggers when fall distance >= this many blocks.")]
    public float softLandingBlocks = 2f;
    [Tooltip("Hard landing triggers when fall distance >= this many blocks.")]
    public float hardLandingBlocks = 5f;

    [Header("Landing Filters (Optional)")]
    [Tooltip("Ignore tiny landings unless these minimums are met.")]
    public bool requireMinFallForVFX = true;
    [Tooltip("Minimum fall distance in blocks for any VFX/SFX to play.")]
    public float minVFXBlocks = 0.25f;
    [Tooltip("Minimum downward speed (|vy|) for any VFX/SFX to play.")]
    public float minVFXSpeedY = 2.0f;

    // --- Internals ---
    Rigidbody2D _rb;
    float _stepTimer;
    float _meowCdTimer;

    void Reset()
    {
        controller = GetComponent<PlayerController2D>();
        _rb = GetComponent<Rigidbody2D>();
    }

    void Awake()
    {
        if (!controller) controller = GetComponent<PlayerController2D>();
        _rb = GetComponent<Rigidbody2D>();

        // feet spawn defaults to controller.groundCheck → fallback to transform
        if (!feet)
            feet = (controller && controller.groundCheck) ? controller.groundCheck : transform;
    }

    void OnEnable()
    {
        if (controller)
        {
            controller.OnJumped += HandleJump;
            controller.OnLanded += HandleLanded;
        }
    }

    void OnDisable()
    {
        if (controller)
        {
            controller.OnJumped -= HandleJump;
            controller.OnLanded -= HandleLanded;
        }
    }

    void Update()
    {
        // Keep feet following controller.groundCheck live if it changes
        if (controller && controller.groundCheck && feet != controller.groundCheck)
            feet = controller.groundCheck;

        _meowCdTimer -= Time.deltaTime;
        HandleFootsteps(Time.deltaTime);
    }

    // ---------- FOOTSTEPS & MEOW ----------
    void HandleFootsteps(float dt)
    {
        if (!IsGrounded()) { _stepTimer = 0f; return; }

        float speed = Mathf.Abs(_rb.velocity.x);
        if (speed < minFootstepSpeed) { _stepTimer = 0f; return; }

        // Interval blends from slow -> fast based on speed/moveSpeed
        float t = (controller && controller.moveSpeed > 0f) ? Mathf.Clamp01(speed / controller.moveSpeed) : 1f;
        float interval = Mathf.Lerp(stepIntervalSlow, stepIntervalFast, t);
        _stepTimer -= dt;

        if (_stepTimer <= 0f)
        {
            _stepTimer = interval;

            bool didMeow = TryMeow();
            if (!didMeow || !meowReplaceFootstep)
                PlayFootstep(t);
        }
    }

    bool TryMeow()
    {
        if (_meowCdTimer > 0f) return false;
        if (meowClips == null || meowClips.Count == 0) return false;
        if (Random.value > meowChancePerStep) return false;

        var clip = meowClips[Random.Range(0, meowClips.Count)];
        float pitch = Mathf.Lerp(meowPitchRange.x, meowPitchRange.y, Random.value);
        SoundManager.Instance?.PlaySFX(clip, feet ? feet.position : transform.position, meowVolume, pitch, 0f);

        // Randomized cooldown window keeps it cute and non-repetitive
        _meowCdTimer = Random.Range(5.0f, 20.0f);
        return true;
    }

    void PlayFootstep(float speedT01)
    {
        if (footstepClips == null || footstepClips.Count == 0) return;
        var clip = footstepClips[Random.Range(0, footstepClips.Count)];
        float vol = scaleFootstepBySpeed ? Mathf.Lerp(0.6f, footstepBaseVolume, speedT01) : footstepBaseVolume;
        SoundManager.Instance?.PlaySFX(clip, feet ? feet.position : transform.position, vol, 1f, 0f);
    }

    // ---------- JUMP ----------
    void HandleJump()
    {
        if (jumpClip) SoundManager.Instance?.PlaySFX(jumpClip, feet ? feet.position : transform.position, 1f, 1f, 0f);
        SpawnAndPlay(jumpParticles, feet ? feet.position : transform.position, 0f);
    }

    // ---------- LANDING ----------
    void HandleLanded(float fallDistanceInUnits, float minYVelocity)
    {
        float blocks = (blockHeight > 0.0001f) ? (fallDistanceInUnits / blockHeight) : Mathf.Infinity;

        if (requireMinFallForVFX)
        {
            if (blocks < minVFXBlocks && Mathf.Abs(minYVelocity) < minVFXSpeedY)
                return;
        }

        if (blocks >= hardLandingBlocks)
        {
            if (landHardClip) SoundManager.Instance?.PlaySFX(landHardClip, feet ? feet.position : transform.position, 1f, 1f, 0f);
            SpawnAndPlay(landHardParticles, feet ? feet.position : transform.position, 0f);
        }
        else if (blocks >= softLandingBlocks)
        {
            if (landSoftClip) SoundManager.Instance?.PlaySFX(landSoftClip, feet ? feet.position : transform.position, 0.9f, 1f, 0f);
            SpawnAndPlay(landSoftParticles, feet ? feet.position : transform.position, 0f);
        }
    }

    // ---------- UTIL ----------
    bool IsGrounded()
    {
        // Prefer controller’s grounded—keeps FX in sync with gameplay logic.
        if (controller) return controller.IsGroundedPublic();

        // Fallback (rare): use our local FX groundCheckSize at feet/transform.
        var pos = feet ? feet.position : transform.position;
        return Physics2D.OverlapBox(pos, groundCheckSize, 0f);
    }

    void SpawnAndPlay(ParticleSystem prefabOrInstance, Vector3 pos, float zRotationDeg = 0f)
    {
        if (!prefabOrInstance) return;

        Quaternion rot = Quaternion.Euler(0f, 0f, zRotationDeg);

        if (prefabOrInstance.gameObject.scene.IsValid())
        {
            var ps = prefabOrInstance;
            var t = ps.transform;
            t.SetPositionAndRotation(pos, rot);

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(true);
            ps.Simulate(0f, true, true, true);
            ps.Play(true);
        }
        else
        {
            var ps = Instantiate(prefabOrInstance, pos, rot);
            ps.Play(true);
            Destroy(ps.gameObject, EstimateTotalLifetime(ps));
        }
    }

    float EstimateTotalLifetime(ParticleSystem ps)
    {
        var m = ps.main;
        float lifetimeMax = m.startLifetime.constantMax > 0f ? m.startLifetime.constantMax : 1.0f;
        float startDelay = m.startDelay.constantMax;
        return m.duration + lifetimeMax + startDelay + 0.5f;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGroundGizmo) return;

        // Draw using this FX's own groundCheckSize, at the same feet position used by FX
        Transform p = feet;
        if (!p && controller) p = controller.groundCheck;
        if (!p) p = transform;

        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.25f);
        Gizmos.DrawCube(p.position, (Vector3)groundCheckSize);
    }

    // ---- Optional public hooks if an ability wants to edit FX’s size ----
    public void SetFxGroundCheckSize(Vector2 size)
    {
        groundCheckSize = new Vector2(Mathf.Max(0.02f, size.x), Mathf.Max(0.02f, size.y));
    }

    public void PlayMeowOneShot()
    {
        _meowCdTimer = 0f; // bypass cooldown if you want, remove this line if not
        TryMeow();
    }
}
