using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put this on the Player. Tab locks onto an enemy (and unlocks). Scroll wheel or Left/Right arrows switch targets.
/// A red "." is drawn on the locked enemy. No layers, markers or colliders needed:
/// it finds every EnemyAI in the scene by itself.
/// </summary>
public class LockOnSystem : MonoBehaviour
{
    [Header("References (auto-found)")]
    public PlayerController player;
    public Transform cameraTransform;

    [Header("Input")]
    public KeyCode lockKey = KeyCode.Tab;
    public KeyCode switchLeftKey = KeyCode.LeftArrow;
    public KeyCode switchRightKey = KeyCode.RightArrow;
    public float scrollThreshold = 0.05f;

    [Header("Targeting")]
    public float lockRange = 15f;                   // how far you can lock onto something
    public float breakRange = 20f;                  // lock drops if the target gets farther than this
    public float viewAngle = 70f;                   // preferred: enemies inside this angle of the camera
    public bool requireLineOfSight = true;          // can't lock through walls
    public LayerMask obstacleLayers = ~0;           // what blocks line of sight
    public float eyeHeight = 1.5f;
    public float lostSightTime = 1.5f;              // lock drops after this long without line of sight
    public bool autoSwitchOnKill = true;            // jump to the nearest enemy when the target dies

    [Header("Indicator (red dot on the target)")]
    public bool showIndicator = true;
    public string indicatorText = ".";
    public Color indicatorColor = Color.red;
    public int indicatorSize = 90;
    public Vector3 indicatorOffset = new Vector3(0f, 0.3f, 0f);   // world offset from the lock point

    public EnemyAI CurrentTarget { get; private set; }
    public bool HasTarget => CurrentTarget != null;
    public Vector3 TargetPoint => CurrentTarget != null ? CurrentTarget.LockPosition : transform.position + transform.forward;

    float lostSightTimer;
    Camera cam;
    GUIStyle dotStyle;
    readonly List<EnemyAI> candidates = new List<EnemyAI>();

    void Awake()
    {
        if (player == null) player = GetComponent<PlayerController>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        if (cameraTransform != null) cam = cameraTransform.GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        if (player != null && player.IsDead) { Release(); return; }

        if (Input.GetKeyDown(lockKey))
        {
            if (HasTarget) Release();
            else Acquire();
        }

        if (!HasTarget) return;

        // Target died or was destroyed
        if (CurrentTarget == null || CurrentTarget.IsDead)
        {
            Release();
            if (autoSwitchOnKill) Acquire();
            return;
        }

        // Too far away
        if (Vector3.Distance(transform.position, CurrentTarget.transform.position) > breakRange)
        {
            Release();
            return;
        }

        // Lost line of sight for too long
        if (!CanSee(CurrentTarget))
        {
            lostSightTimer += Time.deltaTime;
            if (lostSightTimer > lostSightTime) { Release(); return; }
        }
        else lostSightTimer = 0f;

        // Switch targets
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Input.GetKeyDown(switchRightKey) || scroll > scrollThreshold) Switch(+1);
        else if (Input.GetKeyDown(switchLeftKey) || scroll < -scrollThreshold) Switch(-1);
    }

    // ───────────────────────── Indicator ─────────────────────────

    void OnGUI()
    {
        if (!showIndicator || !HasTarget || cam == null) return;

        Vector3 sp = cam.WorldToScreenPoint(TargetPoint + indicatorOffset);
        if (sp.z <= 0f) return;   // behind the camera

        if (dotStyle == null)
        {
            dotStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
        }
        dotStyle.fontSize = indicatorSize;
        dotStyle.normal.textColor = indicatorColor;

        // A "." sits low in its text box, so lift it a little to centre it on the point
        float lift = indicatorSize * 0.3f;
        Rect r = new Rect(sp.x - 100f, Screen.height - sp.y - 100f - lift, 200f, 200f);
        GUI.Label(r, indicatorText, dotStyle);
    }

    // ───────────────────────── Targeting ─────────────────────────

    void Acquire()
    {
        GatherCandidates();

        if (EnemyAI.All.Count == 0)
        {
            Debug.Log("LockOn: no EnemyAI found in the scene.");
            return;
        }
        if (candidates.Count == 0)
        {
            Debug.Log("LockOn: no enemy within lock range (" + lockRange + "m).");
            return;
        }

        EnemyAI best = null;
        float bestScore = float.MaxValue;
        bool anyVisible = false;

        // Pass 1: enemy closest to the centre of the screen (inside view angle)
        foreach (var e in candidates)
        {
            if (!CanSee(e)) continue;
            anyVisible = true;
            if (cameraTransform == null) continue;

            float angle = Vector3.Angle(cameraTransform.forward, e.LockPosition - cameraTransform.position);
            if (angle <= viewAngle && angle < bestScore) { bestScore = angle; best = e; }
        }

        // Pass 2: nothing in view, so take the nearest visible enemy
        if (best == null)
        {
            bestScore = float.MaxValue;
            foreach (var e in candidates)
            {
                if (!CanSee(e)) continue;
                float d = Vector3.Distance(transform.position, e.transform.position);
                if (d < bestScore) { bestScore = d; best = e; }
            }
        }

        if (best != null) SetTarget(best);
        else if (!anyVisible) Debug.Log("LockOn: enemies are in range but something blocks line of sight (turn off Require Line Of Sight to test).");
    }

    void Switch(int direction)   // +1 = right, -1 = left (relative to the camera)
    {
        GatherCandidates();

        float currentAngle = SignedAngle(CurrentTarget);
        EnemyAI best = null;
        float bestDiff = float.MaxValue;

        foreach (var e in candidates)
        {
            if (e == CurrentTarget || !CanSee(e)) continue;

            float diff = (SignedAngle(e) - currentAngle) * direction;
            if (diff <= 0.1f) continue;                 // must be on the requested side
            if (diff < bestDiff) { bestDiff = diff; best = e; }
        }

        if (best != null) SetTarget(best);
    }

    void GatherCandidates()
    {
        candidates.Clear();
        foreach (var e in EnemyAI.All)
        {
            if (e == null || e.IsDead) continue;
            if (Vector3.Distance(transform.position, e.transform.position) > lockRange) continue;
            candidates.Add(e);
        }
    }

    float SignedAngle(EnemyAI e)
    {
        Transform c = cameraTransform != null ? cameraTransform : transform;
        Vector3 fwd = Vector3.ProjectOnPlane(c.forward, Vector3.up);
        Vector3 to  = Vector3.ProjectOnPlane(e.LockPosition - c.position, Vector3.up);
        return Vector3.SignedAngle(fwd, to, Vector3.up);
    }

    bool CanSee(EnemyAI e)
    {
        if (!requireLineOfSight) return true;

        Vector3 from = transform.position + Vector3.up * eyeHeight;
        if (Physics.Linecast(from, e.LockPosition, out RaycastHit hit, obstacleLayers, QueryTriggerInteraction.Ignore))
        {
            // Hitting an enemy's own collider means the way is clear
            return hit.collider.GetComponentInParent<EnemyAI>() != null;
        }
        return true;
    }

    void SetTarget(EnemyAI e)
    {
        CurrentTarget = e;
        lostSightTimer = 0f;
    }

    public void Release()
    {
        CurrentTarget = null;
        lostSightTimer = 0f;
    }

    // Flat (horizontal) direction from a position to the locked target
    public Vector3 FlatDirectionFrom(Vector3 from)
    {
        Vector3 d = TargetPoint - from;
        d.y = 0f;
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.zero;
    }
}