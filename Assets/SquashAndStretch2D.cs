using UnityEngine;

[DisallowMultipleComponent]
public class SquashAndStretch2D : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Root visual yang hanya memuat SpriteRenderer/Animator.")]
    [SerializeField] private Transform visualRoot;

    [Tooltip("Controller untuk listen event Jumped/Landed.")]
    [SerializeField] private PlayerController2D controller;

    [Header("Squash & Stretch Settings")]
    [Tooltip("Multiplier stretch berdasarkan kecepatan.")]
    public float stretchVelocityMultiplier = 0.05f;

    [Tooltip("Berapa besar squash saat landing keras.")]
    public float landingSquashAmount = 0.3f;

    [Tooltip("Durasi kembali ke normal scale setelah squash/strecth.")]
    public float returnSpeed = 8f;

    [Tooltip("Clamp maksimum stretch X/Y.")]
    public float maxStretch = 0.35f;

    private Vector3 originalScale;
    private Rigidbody2D rb;
    private bool landingSquashActive = false;
    private float squashTimer = 0f;
    private float squashDuration = 0.12f;

    private void Awake()
    {
        if (!controller) controller = GetComponent<PlayerController2D>();
        rb = GetComponent<Rigidbody2D>();

        if (!visualRoot)
        {
            // Auto-find child with SpriteRenderer
            SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
            if (sr) visualRoot = sr.transform;
        }

        originalScale = visualRoot.localScale;
    }

    private void OnEnable()
    {
        if (controller)
        {
            controller.OnJumped += HandleJump;
            controller.OnLanded += HandleLanded;
        }
    }

    private void OnDisable()
    {
        if (controller)
        {
            controller.OnJumped -= HandleJump;
            controller.OnLanded -= HandleLanded;
        }
    }

    private void Update()
    {
        if (!visualRoot)
            return;

        if (!landingSquashActive)
            ApplyVelocityStretch();
        else
            ApplyLandingSquash();

        // Smooth return to original scale
        visualRoot.localScale = Vector3.Lerp(
            visualRoot.localScale,
            originalScale,
            Time.deltaTime * returnSpeed
        );
    }

    // ===== Velocity-based Stretch =====
    private void ApplyVelocityStretch()
    {
        Vector2 vel = rb.velocity;
        float speed = vel.magnitude;

        float stretch = Mathf.Clamp(speed * stretchVelocityMultiplier, 0f, maxStretch);

        Vector3 newScale = new Vector3(
            originalScale.x + stretch,
            originalScale.y - stretch,
            originalScale.z
        );

        visualRoot.localScale = Vector3.Lerp(visualRoot.localScale, newScale, Time.deltaTime * 10f);
    }

    // ===== Landing Squash =====
    private void ApplyLandingSquash()
    {
        squashTimer += Time.deltaTime;

        float t = squashTimer / squashDuration;
        t = Mathf.Clamp01(t);

        float squash = landingSquashAmount * (1f - t);

        Vector3 targetScale = new Vector3(
            originalScale.x + squash,
            originalScale.y - squash * 2f,
            originalScale.z
        );

        visualRoot.localScale = targetScale;

        if (t >= 1f)
            landingSquashActive = false;
    }

    // ===== Event Handlers =====
    private void HandleJump()
    {
        // Small upward stretch on jump
        Vector3 jumpScale = new Vector3(
            originalScale.x - 0.1f,
            originalScale.y + 0.15f,
            originalScale.z
        );

        visualRoot.localScale = jumpScale;
    }

    private void HandleLanded(float fallDist, float minY)
    {
        landingSquashActive = true;
        squashTimer = 0f;
    }
}
