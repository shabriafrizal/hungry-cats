using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Camera))]
[AddComponentMenu("Game/Camera/Multi Target Camera 2D (Auto Bounds)")]
public class MultiTargetCamera2D : MonoBehaviour
{
    [Header("Auto Find Players")]
    public string playerTag = "Player";
    [Min(0f)] public float refindInterval = 1.0f;

    [Header("Follow")]
    public Vector3 followOffset = new Vector3(0f, 1f, -10f);
    [Min(0f)] public float followSmoothTime = 0.15f;
    [Min(0f)] public float deadzoneRadius = 0.0f;

    [Header("Zoom (Orthographic)")]
    public Vector2 padding = new Vector2(2f, 1f);
    [Min(0.01f)] public float minOrthoSize = 4f;
    [Min(0.01f)] public float maxOrthoSize = 16f;
    [Min(0f)] public float zoomSmoothTime = 0.2f;

    [Header("World Bounds")]
    public bool autoDetectWorldBounds = true;
    [Tooltip("Root containing tilemaps/colliders/sprites to compute camera bounds.")]
    public Transform worldRoot;
    [Tooltip("If auto-detect fails, you can set this manually.")]
    public Rect worldBounds = new Rect(0, 0, 0, 0);
    public bool clampToWorldBounds = true;

    [Header("Filtering")]
    public bool ignoreInvisible = true;

    readonly List<Transform> _targets = new();
    Camera _cam;
    Vector3 _velFollow;
    float _velZoom;
    float _nextRefindTime;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        if (!_cam.orthographic)
        {
            Debug.LogWarning("[MultiTargetCamera2D] Switching to Orthographic.");
            _cam.orthographic = true;
        }
    }

    void OnEnable()
    {
        RefindPlayers();
        if (autoDetectWorldBounds) TryAutoBounds();
        _nextRefindTime = Time.time + Mathf.Max(0.01f, refindInterval);
    }

    void Update()
    {
        if (refindInterval > 0f && Time.time >= _nextRefindTime)
        {
            RefindPlayers();
            if (autoDetectWorldBounds) TryAutoBounds();
            _nextRefindTime = Time.time + refindInterval;
        }
    }

    void LateUpdate()
    {
        if (_targets.Count == 0) return;

        // Build bounds around valid targets
        bool hasAny = false;
        Bounds b = new Bounds();
        for (int i = 0; i < _targets.Count; i++)
        {
            var t = _targets[i];
            if (!t) continue;

            if (ignoreInvisible)
            {
                var r = t.GetComponentInChildren<Renderer>();
                if (r && !r.enabled) continue;
                var sr = t.GetComponentInChildren<SpriteRenderer>();
                if (sr && !sr.enabled) continue;
            }

            if (!hasAny) { b = new Bounds(t.position, Vector3.zero); hasAny = true; }
            else b.Encapsulate(t.position);
        }
        if (!hasAny) return;

        // Add padding
        b.Expand(new Vector3(padding.x * 2f, padding.y * 2f, 0f));

        // Target center
        Vector3 desiredCenter = b.center;

        // Deadzone
        Vector3 currentPos = transform.position;
        Vector2 d = (Vector2)(desiredCenter - currentPos);
        if (deadzoneRadius > 0f)
        {
            float m = d.magnitude;
            if (m < deadzoneRadius) desiredCenter = currentPos; // stay put inside deadzone
        }

        // Required ortho size
        float aspect = Mathf.Max(0.0001f, _cam.aspect);
        float needHalfH = Mathf.Max(b.extents.y, b.extents.x / aspect);
        float desiredSize = Mathf.Clamp(needHalfH, minOrthoSize, maxOrthoSize);

        // Smooth follow
        Vector3 goal = new Vector3(desiredCenter.x, desiredCenter.y, 0f) + followOffset;
        Vector3 smoothPos = Vector3.SmoothDamp(currentPos, goal, ref _velFollow, followSmoothTime);

        // Clamp to bounds
        if (clampToWorldBounds && worldBounds.width > 0f && worldBounds.height > 0f)
        {
            float halfH = desiredSize;
            float halfW = halfH * aspect;

            float minX = worldBounds.xMin + halfW;
            float maxX = worldBounds.xMax - halfW;
            float minY = worldBounds.yMin + halfH;
            float maxY = worldBounds.yMax - halfH;

            smoothPos.x = Mathf.Clamp(smoothPos.x, minX, maxX);
            smoothPos.y = Mathf.Clamp(smoothPos.y, minY, maxY);
        }

        transform.position = smoothPos;

        // Smooth zoom
        _cam.orthographicSize = Mathf.SmoothDamp(_cam.orthographicSize, desiredSize, ref _velZoom, zoomSmoothTime);
    }

    public void RefindPlayers()
    {
        _targets.Clear();
        var gos = GameObject.FindGameObjectsWithTag(playerTag);
        for (int i = 0; i < gos.Length; i++)
        {
            var t = gos[i].transform;
            if (t && !_targets.Contains(t)) _targets.Add(t);
        }
    }

    public void TryAutoBounds()
    {
        if (!worldRoot) return;

        bool has = false;
        Bounds bb = new Bounds(worldRoot.position, Vector3.zero);

        // CompositeCollider2D first (best)
        foreach (var comp in worldRoot.GetComponentsInChildren<CompositeCollider2D>())
        {
            var b = comp.bounds;
            if (!has) { bb = b; has = true; } else bb.Encapsulate(b);
        }

        // Tilemap bounds via renderers or grids
        foreach (var tr in worldRoot.GetComponentsInChildren<TilemapRenderer>())
        {
            var b = tr.bounds;
            if (!has) { bb = b; has = true; } else bb.Encapsulate(b);
        }

        // Fallback: any Renderer
        foreach (var r in worldRoot.GetComponentsInChildren<Renderer>())
        {
            if (r is TilemapRenderer) continue; // already added
            var b = r.bounds;
            if (!has) { bb = b; has = true; } else bb.Encapsulate(b);
        }

        // Last fallback: Colliders
        foreach (var c in worldRoot.GetComponentsInChildren<Collider2D>())
        {
            var b = c.bounds;
            if (!has) { bb = b; has = true; } else bb.Encapsulate(b);
        }

        if (has)
        {
            worldBounds = new Rect(
                bb.min.x, bb.min.y,
                bb.size.x, bb.size.y
            );
        }
    }

    public void RegisterTarget(Transform t)
    {
        if (t && !_targets.Contains(t)) _targets.Add(t);
    }

    public void UnregisterTarget(Transform t)
    {
        if (t) _targets.Remove(t);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (deadzoneRadius > 0f)
        {
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, deadzoneRadius);
        }
        if (clampToWorldBounds && worldBounds.width > 0f && worldBounds.height > 0f)
        {
            Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.35f);
            Vector3 c = new Vector3(worldBounds.center.x, worldBounds.center.y, 0f);
            Vector3 s = new Vector3(worldBounds.size.x, worldBounds.size.y, 0f);
            Gizmos.DrawWireCube(c, s);
        }
    }
#endif
}
