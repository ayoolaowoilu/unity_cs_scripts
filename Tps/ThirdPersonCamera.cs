using UnityEngine;

/// <summary>
/// Third-person orbit camera. Put this on the Main Camera (NOT parented to the player).
/// PlayerController already moves relative to the camera's yaw, so no changes needed there.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;                         // the player
    public PlayerController player;                  // optional: enables guard zoom + stops look on death
    public LockOnSystem lockOn;                      // optional: camera follows the locked target
    public Vector3 pivotOffset = new Vector3(0f, 1.6f, 0f);   // orbit point (roughly head/shoulder height)

    [Header("Distance")]
    public float distance = 4.2f;
    public float guardDistance = 3.2f;               // camera pulls in a bit while guarding
    public Vector3 shoulderOffset = new Vector3(0.6f, 0f, 0f); // over-the-shoulder shift (x = right)
    public float zoomSmooth = 8f;

    [Header("Look")]
    public float sensitivityX = 2.5f;
    public float sensitivityY = 2.0f;
    public float minPitch = -25f;
    public float maxPitch = 60f;
    public bool invertY = false;
    public bool lockCursor = true;

    [Header("Lock-On")]
    public float lockFollowSpeed = 8f;               // how quickly the camera swings toward the target
    public float lockPitch = 15f;                    // camera pitch while locked on

    [Header("Bow Aim (zoom)")]
    public BowController bow;                        // auto-found on the player
    public float aimDistance = 2.1f;                 // camera moves in close while aiming
    public Vector3 aimShoulderOffset = new Vector3(0.75f, 0.1f, 0f);
    public float aimFov = 40f;                       // narrower field of view = zoomed in
    public float drawZoom = 6f;                      // extra zoom at a full draw (degrees of FOV)
    public float aimSensitivityMult = 0.6f;          // slower turning while aiming, for precision
    public float fovSmooth = 10f;

    [Header("Follow")]
    public float followSmoothTime = 0.05f;           // small = tight follow, larger = floaty

    [Header("Collision")]
    public LayerMask collisionMask = ~0;             // untick the Player (and enemy) layers here
    public float collisionRadius = 0.25f;
    public float collisionPadding = 0.1f;

    float yaw;
    float pitch = 15f;
    Camera cam;
    float baseFov = 60f;
    Vector3 currentShoulder;
    float currentDistance;
    Vector3 pivotVelocity;
    Vector3 smoothedPivot;

    void Start()
    {
        if (target == null && player != null) target = player.transform;
        if (player == null && target != null) player = target.GetComponent<PlayerController>();
        if (lockOn == null && target != null) lockOn = target.GetComponent<LockOnSystem>();
        if (bow == null && target != null) bow = target.GetComponent<BowController>();

        cam = GetComponent<Camera>();
        if (cam != null) baseFov = cam.fieldOfView;
        currentShoulder = shoulderOffset;

        Vector3 e = transform.eulerAngles;
        yaw = e.y;
        currentDistance = distance;
        if (target != null) smoothedPivot = target.position + pivotOffset;

        SetCursor(lockCursor);
    }

    void Update()
    {
        // Esc frees the cursor, click locks it again
        if (Input.GetKeyDown(KeyCode.Escape)) SetCursor(false);
        else if (lockCursor && Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) SetCursor(true);
    }

    void LateUpdate()
    {
        if (target == null) return;

        bool canLook = Cursor.lockState == CursorLockMode.Locked && (player == null || !player.IsDead);
        bool aiming = bow != null && bow.IsAiming;
        float lookMult = aiming ? aimSensitivityMult : 1f;
        bool locked = lockOn != null && lockOn.HasTarget && !aiming;   // free aim wins over lock-on
        if (locked)
        {
            // Swing behind the player so the target stays in front
            Vector3 flat = lockOn.TargetPoint - target.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f)
            {
                float wantedYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                float k = 1f - Mathf.Exp(-lockFollowSpeed * Time.deltaTime);
                yaw   = Mathf.LerpAngle(yaw, wantedYaw, k);
                pitch = Mathf.Lerp(pitch, lockPitch, k);
            }
        }
        else if (canLook)
        {
            yaw   += Input.GetAxis("Mouse X") * sensitivityX * lookMult;
            pitch += Input.GetAxis("Mouse Y") * sensitivityY * lookMult * (invertY ? 1f : -1f);
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        // Smooth pivot follow
        Vector3 pivotTarget = target.position + pivotOffset;
        smoothedPivot = Vector3.SmoothDamp(smoothedPivot, pivotTarget, ref pivotVelocity, followSmoothTime);

        // Guard zoom
        bool guarding = player != null &&
                        (player.CurrentState == PlayerController.State.Guard ||
                         player.CurrentState == PlayerController.State.GuardWalk);
        float wantedDistance = aiming ? aimDistance : (guarding ? guardDistance : distance);
        currentDistance = Mathf.Lerp(currentDistance, wantedDistance, zoomSmooth * Time.deltaTime);

        // Slide over the shoulder and zoom the lens while aiming
        currentShoulder = Vector3.Lerp(currentShoulder, aiming ? aimShoulderOffset : shoulderOffset, zoomSmooth * Time.deltaTime);
        if (cam != null)
        {
            float wantedFov = aiming ? aimFov - drawZoom * bow.Charge : baseFov;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, wantedFov, fovSmooth * Time.deltaTime);
        }

        // Desired position
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 local = new Vector3(currentShoulder.x, currentShoulder.y, -currentDistance);
        Vector3 desiredPos = smoothedPivot + rot * local;

        // Collision: pull the camera in if something is between the pivot and the camera
        Vector3 dir = desiredPos - smoothedPivot;
        float dist = dir.magnitude;
        if (dist > 0.001f &&
            Physics.SphereCast(smoothedPivot, collisionRadius, dir / dist, out RaycastHit hit, dist,
                               collisionMask, QueryTriggerInteraction.Ignore))
        {
            desiredPos = smoothedPivot + (dir / dist) * Mathf.Max(hit.distance - collisionPadding, 0.2f);
        }

        transform.SetPositionAndRotation(desiredPos, rot);
    }

    void SetCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}