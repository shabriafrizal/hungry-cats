using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class ButtonSystems : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Target")]
    public TextMeshProUGUI targetText;
    public Transform scaleTarget;

    [Header("Colors")]
    public Color normalColor = Color.white;
    public Color hoverColor = new Color(1f, 1f, 1f, 1f);
    public Color pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);

    [Header("Scale")]
    public bool scaleOnHover = true;
    public float hoverScale = 1.05f;
    public float pressScale = 0.98f;

    [Header("Sound")]
    public AudioClip hoverClip;
    public AudioClip clickClip;
    public float uiVolume = 1f;
    public float uiPitch = 1f;


    Color _initialColor;
    Vector3 _initialScale;

    void Awake()
    {
        if (!targetText) targetText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (!scaleTarget) scaleTarget = transform;

        if (targetText)
        {
            _initialColor = targetText.color;
            if (normalColor == default) normalColor = _initialColor;
        }
        _initialScale = scaleTarget.localScale;
    }

    void OnEnable()
    {
        if (targetText) targetText.color = normalColor;
        scaleTarget.localScale = _initialScale;
    }

    void OnDisable()
    {
        if (targetText) targetText.color = normalColor;
        scaleTarget.localScale = _initialScale;
    }

    public void OnPointerEnter(PointerEventData _)
    {
        SetState(hoverColor, scaleOnHover ? hoverScale : 1f);
        if (hoverClip && SoundManager.Instance) SoundManager.Instance.PlayUI(hoverClip, uiVolume, uiPitch);
    }
    public void OnPointerExit(PointerEventData _) { SetState(normalColor, 1f); }
    public void OnPointerDown(PointerEventData _)
    {
        SetState(pressedColor, scaleOnHover ? pressScale : 1f);
        if (clickClip && SoundManager.Instance) SoundManager.Instance.PlayUI(clickClip, uiVolume, uiPitch);
    }
    public void OnPointerUp(PointerEventData e)
    {
        bool stillOver = e.pointerCurrentRaycast.gameObject &&
                         (e.pointerCurrentRaycast.gameObject == gameObject ||
                          e.pointerCurrentRaycast.gameObject.transform.IsChildOf(transform));
        SetState(stillOver ? hoverColor : normalColor,
                 scaleOnHover ? (stillOver ? hoverScale : 1f) : 1f);
    }

    void SetState(Color c, float scaleMul)
    {
        if (targetText) targetText.color = c;
        scaleTarget.localScale = _initialScale * scaleMul;
    }
}
