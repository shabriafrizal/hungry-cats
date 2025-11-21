using UnityEngine;

[DisallowMultipleComponent]
public class PlayerSizeController2D : MonoBehaviour
{
    [System.Serializable]
    public struct SizePreset
    {
        public string name;
        public Vector2 scaleMultiplier;
    }

    [Header("Presets")]
    public SizePreset smallPreset;
    public SizePreset normalPreset;
    public SizePreset bigPreset;

    // target controller
    private PlayerController2D controller;

    // original values
    private Vector2 originalScale;
    private float originalMoveSpeed;
    private float originalAcceleration;
    private float originalDeceleration;
    private float originalJumpVelocity;
    private float originalFallGravityMultiplier;

    private void Awake()
    {
        controller = GetComponent<PlayerController2D>();

        originalScale = transform.localScale;
    }

    // ======= API PUBLIK =======
    public void ApplyPreset(SizePreset preset)
    {
        // scale transform
        transform.localScale = new Vector3(
            originalScale.x * preset.scaleMultiplier.x,
            originalScale.y * preset.scaleMultiplier.y,
            transform.localScale.z
        );
    }

    public void SetSmall() => ApplyPreset(smallPreset);
    public void SetNormal() => ApplyPreset(normalPreset);
    public void SetBig() => ApplyPreset(bigPreset);
}
