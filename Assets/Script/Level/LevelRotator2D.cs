using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class LevelRotator2D : MonoBehaviour
{
    public enum FocusMode { ActiveIndex, ClosestToCameraCenter, AverageOfAllPlayers, FirstInList }
    public enum GroundRule { AllPlayersMustBeGrounded, OnlyPivotMustBeGrounded }

    [Header("Level Root")]
    public Transform levelRoot;

    [Header("Rotate Input")]
    public KeyCode rotateCCWKey = KeyCode.Q;
    public KeyCode rotateCWKey = KeyCode.E;

    [Header("Step & Animation")]
    [Tooltip("Degrees per step (usually 90).")]
    public float stepDegrees = 90f;
    [Tooltip("Unscaled time duration for the rotation tween.")]
    public float rotateDuration = 0.4f;
    public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Small cooldown after rotation before controls resume.")]
    public float postCooldown = 0.05f;

    [Header("Players (Auto)")]
    public string playerTag = "Player";
    public bool autoFindPlayers = true;
    public List<Rigidbody2D> players = new();

    [Header("Pivot & Rules")]
    public FocusMode focusMode = FocusMode.ClosestToCameraCenter;
    public int activePlayerIndex = 0;
    public GroundRule groundRule = GroundRule.OnlyPivotMustBeGrounded;

    [Header("Rotation When No Players")]
    [Tooltip("If true, allow rotation even when there are no players (pivot = levelRoot.position).")]
    public bool allowRotationWhenNoPlayers = true;

    [Header("Disable While Rotating (Optional / Legacy)")]
    [Tooltip("If assigned, these are toggled off during rotation. (Not required thanks to Auto Disable below.)")]
    public MonoBehaviour[] disableWhileRotatingShared;

    [Header("Auto Disable (Players)")]
    [Tooltip("Automatically disable all Behaviour components under each player during rotation.")]
    public bool autoDisablePlayerBehaviours = true;
    [Tooltip("Search disabled children too when collecting Behaviours.")]
    public bool includeInactiveChildren = false;
    [Tooltip("Skip specific types from auto-disable (e.g., Animator). Leave empty for none.")]
    public bool skipAnimatorDisable = false;

    [Header("Keep Players Upright")]
    [Tooltip("If true, players are counter-rotated so they don't visually spin while the world rotates.")]
    public bool keepPlayersUpright = true;

    [Header("Ground Check (Auto)")]
    public Vector2 groundCheckSize = new(0.6f, 0.1f);
    public LayerMask groundMask = ~0;
    [Tooltip("How far to raycast down when snapping to ground after rotation.")]
    public float snapRayDistance = 2f;
    [Tooltip("Skin offset above hit point when snapping.")]
    public float snapSkin = 0.02f;
    public bool autoSnapToGround = true;
    public bool snapAllPlayers = true;

    [Header("Bounds (to keep pivot sane)")]
    public bool autoDetectLevelBounds = true;
    public Transform worldRoot; // Ideally the same root your camera/world uses
    public Rect levelBounds = new Rect(0, 0, 0, 0);

    [Header("Sound")]
    public AudioClip rotateSFX;              // optional SFX when rotating

    // ===== Runtime =====
    readonly List<Transform> _groundChecks = new();
    readonly List<Transform> _synthHelpers = new(); // created when groundCheck missing
    readonly List<(Behaviour behaviour, bool wasEnabled)> _disabledBehaviours = new();

    bool _isRotating;

    void Reset()
    {
        ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
        groundMask = LayerMask.GetMask("Default", "Ground");
    }

    void Start()
    {
        if (autoFindPlayers) AutoFindPlayers();
        RebuildGroundChecksFromPlayers();
        if (autoDetectLevelBounds) TryAutoBounds();
    }

    void Update()
    {
        // Keep players list clean (handles destroyed players mid-game)
        bool changed = PruneMissingPlayers();
        if (changed) RebuildGroundChecksFromPlayers();
        if (players.Count == 0 && autoFindPlayers) AutoFindPlayers(); // if all gone, try to find new ones
        if (players.Count == 0 && !allowRotationWhenNoPlayers) return;

        if (_isRotating || levelRoot == null) return;

        int dir = 0;
        if (Input.GetKeyDown(rotateCCWKey)) dir = +1;
        else if (Input.GetKeyDown(rotateCWKey)) dir = -1;
        if (dir == 0) return;

        int pivotIdx = ChoosePivotIndex();
        // If no players and allowed, pivotIdx will be -1; that's okay, we’ll pivot around levelRoot.
        if (pivotIdx >= 0 && !PassesGroundRule(pivotIdx)) return;

        StartCoroutine(RotateRoutine(dir, pivotIdx));
    }

    // ---------- Public register helpers (optional for other systems) ----------
    public void RegisterPlayer(Rigidbody2D rb)
    {
        if (!rb || players.Contains(rb)) return;
        players.Add(rb);
        RebuildGroundChecksFromPlayers();
    }
    public void UnregisterPlayer(Rigidbody2D rb)
    {
        if (!rb) return;
        int idx = players.IndexOf(rb);
        if (idx >= 0) players.RemoveAt(idx);
        RebuildGroundChecksFromPlayers();
    }

    // ---------- Auto Players / GroundChecks / Bounds ----------

    public void AutoFindPlayers()
    {
        players.RemoveAll(p => p == null);
        var gos = GameObject.FindGameObjectsWithTag(playerTag);
        foreach (var go in gos)
        {
            var rb = go.GetComponent<Rigidbody2D>();
            if (rb && !players.Contains(rb)) players.Add(rb);
        }
        RebuildGroundChecksFromPlayers();
    }

    void RebuildGroundChecksFromPlayers()
    {
        // Clean old synth helpers
        foreach (var t in _synthHelpers) if (t) Destroy(t.gameObject);
        _synthHelpers.Clear();

        _groundChecks.Clear();
        for (int i = 0; i < players.Count; i++)
        {
            var rb = players[i];
            if (!rb) { _groundChecks.Add(null); continue; }
            Transform gc = FindGroundCheckChild(rb.transform);
            if (!gc) gc = CreateSyntheticGroundProbe(rb);
            _groundChecks.Add(gc);
        }
    }

    bool PruneMissingPlayers()
    {
        bool removed = false;
        for (int i = players.Count - 1; i >= 0; i--)
        {
            if (players[i] == null)
            {
                players.RemoveAt(i);
                removed = true;
            }
        }
        return removed;
    }

    Transform FindGroundCheckChild(Transform root)
    {
        if (!root) return null;
        var t = root.Find("GroundCheck");
        if (t) return t;
        t = root.Find("groundCheck");
        if (t) return t;
        t = root.Find("Groundcheck");
        return t;
    }

    Transform CreateSyntheticGroundProbe(Rigidbody2D rb)
    {
        if (!rb) return null;

        // Find lowest collider point to place probe slightly below center
        float lowestY = rb.transform.position.y;
        var cols = rb.GetComponentsInChildren<Collider2D>();
        if (cols.Length > 0)
        {
            Bounds b = cols[0].bounds;
            foreach (var c in cols) b.Encapsulate(c.bounds);
            lowestY = b.min.y;
        }

        var go = new GameObject("[GC]_Synth");
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(rb.transform, worldPositionStays: false);
        go.transform.localPosition = new Vector3(0f, (lowestY - rb.transform.position.y) - 0.05f, 0f);
        _synthHelpers.Add(go.transform);
        return go.transform;
    }

    public void TryAutoBounds()
    {
        if (!worldRoot) return;

        bool has = false;
        Bounds bb = new Bounds(worldRoot.position, Vector3.zero);

        foreach (var comp in worldRoot.GetComponentsInChildren<CompositeCollider2D>())
        { if (!has) { bb = comp.bounds; has = true; } else bb.Encapsulate(comp.bounds); }

        foreach (var tr in worldRoot.GetComponentsInChildren<TilemapRenderer>())
        { if (!has) { bb = tr.bounds; has = true; } else bb.Encapsulate(tr.bounds); }

        foreach (var r in worldRoot.GetComponentsInChildren<Renderer>())
        { if (r is TilemapRenderer) continue; if (!has) { bb = r.bounds; has = true; } else bb.Encapsulate(r.bounds); }

        foreach (var c in worldRoot.GetComponentsInChildren<Collider2D>())
        { if (!has) { bb = c.bounds; has = true; } else bb.Encapsulate(c.bounds); }

        if (has)
            levelBounds = new Rect(bb.min.x, bb.min.y, bb.size.x, bb.size.y);
    }

    // ---------- Rotation ----------

    int ChoosePivotIndex()
    {
        if (players.Count == 0) return -1;

        switch (focusMode)
        {
            case FocusMode.ActiveIndex:
                return Mathf.Clamp(activePlayerIndex, 0, Mathf.Max(0, players.Count - 1));

            case FocusMode.FirstInList:
                return players.Count > 0 ? 0 : -1;

            case FocusMode.ClosestToCameraCenter:
                {
                    if (Camera.main == null || players.Count == 0) return 0;
                    Vector2 sc = new Vector2(0.5f, 0.5f);
                    float best = float.MaxValue; int bestIdx = 0;
                    for (int i = 0; i < players.Count; i++)
                    {
                        var rb = players[i]; if (!rb) continue;
                        Vector3 vp = Camera.main.WorldToViewportPoint(rb.worldCenterOfMass);
                        float d = ((Vector2)vp - sc).sqrMagnitude;
                        if (d < best) { best = d; bestIdx = i; }
                    }
                    return bestIdx;
                }

            case FocusMode.AverageOfAllPlayers:
                {
                    Vector2 avg = Vector2.zero; int cnt = 0;
                    foreach (var rb in players) { if (!rb) continue; avg += rb.worldCenterOfMass; cnt++; }
                    if (cnt == 0) return -1;
                    avg /= cnt;

                    float best = float.MaxValue; int bestIdx = 0;
                    for (int i = 0; i < players.Count; i++)
                    {
                        var rb = players[i]; if (!rb) continue;
                        float d = (rb.worldCenterOfMass - avg).sqrMagnitude;
                        if (d < best) { best = d; bestIdx = i; }
                    }
                    return bestIdx;
                }
        }
        return -1;
    }

    bool PassesGroundRule(int pivotIdx)
    {
        if (players.Count == 0) return allowRotationWhenNoPlayers;

        switch (groundRule)
        {
            case GroundRule.AllPlayersMustBeGrounded:
                for (int i = 0; i < players.Count; i++) if (!IsGrounded(i)) return false;
                return true;
            case GroundRule.OnlyPivotMustBeGrounded:
                if (pivotIdx < 0) return allowRotationWhenNoPlayers;
                return IsGrounded(pivotIdx);
        }
        return true;
    }

    IEnumerator RotateRoutine(int dir, int pivotIdx)
    {
        _isRotating = true;
        if (rotateSFX) SoundManager.Instance?.PlayUI(rotateSFX, 1f, 1f);

        // ===== SNAPSHOT lists to be robust against mid-rotation despawns =====
        var playerSnapshot = new List<Rigidbody2D>(players);
        var groundCheckSnapshot = new List<Transform>(_groundChecks);

        // ====== LOCK PHASE ======
        var preVel = new Vector2[playerSnapshot.Count];
        var preSim = new bool[playerSnapshot.Count];

        // 1) Disable explicitly shared scripts (legacy/optional)
        ToggleSharedScripts(false);

        // 2) Auto-disable all Behaviours on player hierarchies (optional)
        if (autoDisablePlayerBehaviours)
        {
            _disabledBehaviours.Clear();
            for (int i = 0; i < playerSnapshot.Count; i++)
            {
                var rb = playerSnapshot[i]; if (!rb) continue;
                var behaviours = rb.GetComponentsInChildren<Behaviour>(includeInactiveChildren);
                foreach (var b in behaviours)
                {
                    if (!b) continue;
                    if (skipAnimatorDisable && b is Animator) continue;

                    bool wasEnabled = b.enabled;
                    if (wasEnabled) b.enabled = false;
                    _disabledBehaviours.Add((b, wasEnabled));
                }
            }
        }

        // 3) Freeze physics
        for (int i = 0; i < playerSnapshot.Count; i++)
        {
            var rb = playerSnapshot[i]; if (!rb) continue;
            preVel[i] = rb.velocity;
            preSim[i] = rb.simulated;
            rb.velocity = Vector2.zero;
            rb.simulated = false;
        }

        // ====== PIVOT SETUP ======
        Vector3 pivotPoint = GetPivotPointFromSnapshot(playerSnapshot, pivotIdx);
        if (levelBounds.width > 0f && levelBounds.height > 0f)
        {
            pivotPoint.x = Mathf.Clamp(pivotPoint.x, levelBounds.xMin, levelBounds.xMax);
            pivotPoint.y = Mathf.Clamp(pivotPoint.y, levelBounds.yMin, levelBounds.yMax);
        }

        Quaternion startRot = levelRoot.rotation;
        Quaternion targetRot = startRot * Quaternion.Euler(0f, 0f, dir * stepDegrees);

        // ====== ROTATE WITH PLAYER COUNTER-ROTATION ======
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, rotateDuration);
            float k = ease.Evaluate(Mathf.Clamp01(t));

            Quaternion rot = Quaternion.Slerp(startRot, targetRot, k);
            Quaternion delta = rot * Quaternion.Inverse(levelRoot.rotation);

            Vector3 offset = levelRoot.position - pivotPoint;
            Vector3 newPos = pivotPoint + delta * offset;
            levelRoot.rotation = rot;
            levelRoot.position = newPos;

            if (keepPlayersUpright)
            {
                Quaternion invDelta = Quaternion.Inverse(delta);
                for (int i = 0; i < playerSnapshot.Count; i++)
                {
                    var rb = playerSnapshot[i]; if (!rb) continue;
                    var tr = rb.transform;
                    tr.rotation = invDelta * tr.rotation;
                }
            }

            yield return null;
        }

        levelRoot.rotation = targetRot;

        // ====== SNAP DOWN (optional) ======
        if (autoSnapToGround)
        {
            if (snapAllPlayers) for (int i = 0; i < playerSnapshot.Count; i++) SnapPlayerDownFromSnapshot(playerSnapshot, i);
            else SnapPlayerDownFromSnapshot(playerSnapshot, Mathf.Clamp(pivotIdx, -1, playerSnapshot.Count - 1));
        }

        // ====== UNLOCK PHASE ======
        for (int i = 0; i < playerSnapshot.Count; i++)
        {
            var rb = playerSnapshot[i]; if (!rb) continue;
            rb.simulated = preSim[i];
            rb.velocity = Vector2.zero; // start from rest after rotation
        }

        if (autoDisablePlayerBehaviours)
        {
            foreach (var (beh, wasEnabled) in _disabledBehaviours)
            {
                if (!beh) continue;
                beh.enabled = wasEnabled;
            }
            _disabledBehaviours.Clear();
        }

        ToggleSharedScripts(true);

        if (postCooldown > 0f) yield return new WaitForSecondsRealtime(postCooldown);
        _isRotating = false;
    }

    Vector3 GetPivotPointFromSnapshot(List<Rigidbody2D> snap, int pivotIdx)
    {
        if (snap == null || snap.Count == 0 || pivotIdx < 0)
            return levelRoot ? levelRoot.position : Vector3.zero;

        if (focusMode == FocusMode.AverageOfAllPlayers)
        {
            Vector2 avg = Vector2.zero; int cnt = 0;
            foreach (var rb in snap) { if (!rb) continue; avg += rb.worldCenterOfMass; cnt++; }
            if (cnt > 0) return avg / cnt;
        }

        var prb = (pivotIdx >= 0 && pivotIdx < snap.Count) ? snap[pivotIdx] : null;
        return prb ? (Vector3)prb.worldCenterOfMass : (levelRoot ? levelRoot.position : Vector3.zero);
    }

    Vector3 GetPivotPoint(int pivotIdx)
    {
        if (players.Count == 0 || pivotIdx < 0)
            return levelRoot ? levelRoot.position : Vector3.zero;

        if (focusMode == FocusMode.AverageOfAllPlayers)
        {
            Vector2 avg = Vector2.zero; int cnt = 0;
            foreach (var rb in players) { if (!rb) continue; avg += rb.worldCenterOfMass; cnt++; }
            if (cnt > 0) return avg / cnt;
        }
        var prb = (pivotIdx >= 0 && pivotIdx < players.Count) ? players[pivotIdx] : null;
        return prb ? (Vector3)prb.worldCenterOfMass : levelRoot.position;
    }

    void ToggleSharedScripts(bool enable)
    {
        if (disableWhileRotatingShared == null) return;
        foreach (var m in disableWhileRotatingShared) if (m) m.enabled = enable;
    }

    bool IsGrounded(int idx)
    {
        if (idx < 0 || idx >= players.Count) return false;
        var rb = players[idx]; if (!rb) return false;

        Vector3 probePos;
        if (idx < _groundChecks.Count && _groundChecks[idx] != null)
            probePos = _groundChecks[idx].position;
        else
            probePos = rb.transform.position + Vector3.down * 0.1f;

        return Physics2D.OverlapBox(probePos, groundCheckSize, 0f, groundMask);
    }

    void SnapPlayerDownFromSnapshot(List<Rigidbody2D> snap, int idx)
    {
        if (idx < 0 || idx >= snap.Count) return;
        var rb = snap[idx]; if (!rb) return;

        Vector2 origin = rb.transform.position;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, snapRayDistance, groundMask);
        if (hit.collider != null)
        {
            Vector3 p = rb.transform.position;
            p.y = hit.point.y + snapSkin;
            rb.transform.position = p;
        }
    }

    void SnapPlayerDown(int idx)
    {
        if (idx < 0 || idx >= players.Count) return;
        var rb = players[idx]; if (!rb) return;

        Vector2 origin = rb.transform.position;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, snapRayDistance, groundMask);
        if (hit.collider != null)
        {
            Vector3 p = rb.transform.position;
            p.y = hit.point.y + snapSkin;
            rb.transform.position = p;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.35f);
        foreach (var t in _groundChecks)
        {
            if (!t) continue;
            Gizmos.DrawCube(t.position, (Vector3)groundCheckSize);
        }

        if (levelBounds.width > 0f && levelBounds.height > 0f)
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.35f);
            Vector3 c = new Vector3(levelBounds.center.x, levelBounds.center.y, 0f);
            Vector3 s = new Vector3(levelBounds.size.x, levelBounds.size.y, 0f);
            Gizmos.DrawWireCube(c, s);
        }
    }
#endif
}
