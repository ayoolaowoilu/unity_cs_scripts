using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  A weapon that can be held in the hand or carried on the back
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class HolsterItem
{
    [Tooltip("The weapon object (sword, shield or bow)")]
    public Transform item;
    [Tooltip("Where it sits when held. Leave empty to keep it where it is now (works for a sword / shield already in the hand).")]
    public Transform handSocket;
    [Tooltip("Where it sits on the back (an empty object placed on the back / spine bone). Leave empty to just hide it.")]
    public Transform backSocket;
    [Tooltip("Tick if it starts in the hand (sword, shield). Leave off for the bow.")]
    public bool startsInHand = true;

    // pose captured from the scene when no hand socket is given
    Transform capturedParent;
    Vector3 capturedPos, capturedScale;
    Quaternion capturedRot;
    bool captured;

    public void Init()
    {
        if (item == null || captured || handSocket != null || !startsInHand) return;
        capturedParent = item.parent;
        capturedPos = item.localPosition;
        capturedRot = item.localRotation;
        capturedScale = item.localScale;
        captured = true;
    }

    public void SetHeld(bool held)
    {
        if (item == null) return;

        if (held)
        {
            item.gameObject.SetActive(true);
            if (handSocket != null) Attach(handSocket);
            else if (captured)
            {
                item.SetParent(capturedParent, false);
                item.localPosition = capturedPos;
                item.localRotation = capturedRot;
                item.localScale = capturedScale;
            }
        }
        else
        {
            if (backSocket != null)
            {
                item.gameObject.SetActive(true);
                Attach(backSocket);
            }
            else item.gameObject.SetActive(false);
        }
    }

    void Attach(Transform socket)
    {
        item.SetParent(socket, false);
        item.localPosition = Vector3.zero;
        item.localRotation = Quaternion.identity;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  BOW + WEAPON SWITCHING
//  Put this on the Player.
//    R (or 1 / 2)  = switch between sword + shield and the bow
//    hold Right Mouse = aim (the camera zooms in)
//    hold Left Mouse = draw, release = shoot
// ─────────────────────────────────────────────────────────────────────────────

public class BowController : MonoBehaviour
{
    [Header("References (auto-found)")]
    public PlayerController player;
    public Transform cameraTransform;

    [Header("Input")]
    public KeyCode switchKey = KeyCode.R;            // toggles sword <-> bow
    public KeyCode swordKey = KeyCode.Alpha1;
    public KeyCode bowKey = KeyCode.Alpha2;
    public KeyCode fireKey = KeyCode.Mouse0;         // hold to draw, release to shoot
    // Aiming uses PlayerController's "Aim Key" (right mouse by default)

    [Header("Weapons (drag the objects in)")]
    public HolsterItem sword = new HolsterItem { startsInHand = true };
    public HolsterItem shield = new HolsterItem { startsInHand = true };
    public HolsterItem bow = new HolsterItem { startsInHand = false };
    public bool startWithBow = false;

    [Header("Switching")]
    [Tooltip("How long the player is busy changing weapons")]
    public float equipDuration = 0.9f;
    [Tooltip("When, during that time, the weapons change hands / go on the back")]
    public float swapTime = 0.45f;
    [Tooltip("On = wait for an Animation Event calling AnimEvent_SwapWeapons instead of using Swap Time")]
    public bool swapOnAnimationEvent = false;
    [Tooltip("Animator state played when taking out the bow. Empty = no animation.")]
    public string equipBowAnim = AnimId.EquipBow;
    [Tooltip("Animator state played when taking out the sword. Empty = no animation.")]
    public string equipSwordAnim = AnimId.EquipSword;

    [Header("Bow")]
    [Tooltip("Where arrows come from (the nock / the hand). Empty = in front of the chest.")]
    public Transform arrowSpawn;
    [Tooltip("Optional. Empty = a simple built-in arrow.")]
    public GameObject arrowPrefab;
    public float drawTime = 0.8f;                    // seconds to reach a full draw
    [Range(0f, 1f), Tooltip("Letting go before this much draw cancels the shot")]
    public float minCharge = 0.25f;
    public float minDamage = 8f;                     // at the lowest draw
    public float maxDamage = 28f;                    // at a full draw
    public float minSpeed = 22f;
    public float maxSpeed = 48f;
    public float arrowGravity = 7f;
    public bool compensateDrop = true;               // aims a little high so the arrow lands on the crosshair
    public float shotCooldown = 0.35f;
    public float shootAnimTime = 0.4f;               // how long the release animation lasts
    public bool autoReleaseAtFull = false;
    public float arrowLifetime = 8f;
    public float stickTime = 8f;                     // arrows stay stuck in things this long

    [Header("Ammo")]
    public bool unlimitedArrows = true;
    public int arrows = 20;
    public int maxArrows = 30;

    [Header("Aim")]
    public float aimMaxDistance = 120f;
    public LayerMask aimMask = ~0;
    [Tooltip("An enemy this close to the crosshair (degrees) pulls the shot onto it. 0 = off.")]
    public float aimAssistAngle = 2.5f;
    public float aimAssistRange = 40f;

    [Header("Crosshair (leave the sprite empty for the built-in one)")]
    public bool showCrosshair = true;
    public Sprite crosshairSprite;
    public float crosshairSize = 32f;
    public Color crosshairColor = new Color(1f, 1f, 1f, 0.9f);
    public Color crosshairEnemyColor = new Color(1f, 0.25f, 0.2f, 1f);
    public bool showChargeBar = true;
    public bool showAmmoCount = true;

    [Header("Sounds (optional)")]
    public AudioClip drawSound;
    public AudioClip shootSound;

    // ── state other scripts read ──
    public bool BowEquipped { get; private set; }
    public bool IsDrawing { get; private set; }
    public bool IsShooting { get; private set; }
    public float Charge { get; private set; }        // 0 to 1
    public bool Switching => switching;
    public bool IsAiming
    {
        get
        {
            if (!BowEquipped || player == null) return false;
            var s = player.CurrentState;
            return s == PlayerController.State.Aim || s == PlayerController.State.AimWalk;
        }
    }
    public bool HasArrows => unlimitedArrows || arrows > 0;
    public Vector3 AimPoint { get; private set; }     // the spot in the world the crosshair is on (while aiming)

    bool switching;
    bool swapDone;
    bool swapTarget;
    float drawTimer;
    float shootTimer;
    float lastShotTime = -99f;
    EnemyAI hoveredEnemy;
    GUIStyle ammoStyle;

    // ───────────────────────── Setup ─────────────────────────

    void Awake()
    {
        if (player == null) player = GetComponent<PlayerController>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

        sword.Init();
        shield.Init();
        bow.Init();
    }

    void Start()
    {
        ApplyMode(startWithBow);
    }

    // Puts the right weapons in the hands and on the back, with no animation
    void ApplyMode(bool bowMode)
    {
        BowEquipped = bowMode;
        sword.SetHeld(!bowMode);
        shield.SetHeld(!bowMode);
        bow.SetHeld(bowMode);
    }

    // ───────────────────────── Main loop ─────────────────────────

    void Update()
    {
        if (player == null || player.IsDead)
        {
            CancelDraw();
            return;
        }

        HandleSwitchInput();

        if (!IsAiming)
        {
            hoveredEnemy = null;
            if (!IsShooting) CancelDraw();
            return;
        }

        AimPoint = ComputeAimPoint(out hoveredEnemy);   // keeps the crosshair colour and the spine aim up to date

        // Release animation running
        if (IsShooting)
        {
            shootTimer -= Time.deltaTime;
            if (shootTimer <= 0f) IsShooting = false;
            return;
        }

        if (!IsDrawing)
        {
            if (Input.GetKeyDown(fireKey) && Time.time >= lastShotTime + shotCooldown && HasArrows)
                BeginDraw();
        }
        else
        {
            drawTimer += Time.deltaTime;
            Charge = Mathf.Clamp01(drawTimer / Mathf.Max(0.05f, drawTime));

            if (!Input.GetKey(fireKey)) Release();
            else if (autoReleaseAtFull && Charge >= 1f) Release();
        }
    }

    // ───────────────────────── Switching weapons ─────────────────────────

    void HandleSwitchInput()
    {
        if (switching || !player.CanSwitchWeapon) return;

        bool wantBow = BowEquipped;
        if (Input.GetKeyDown(bowKey)) wantBow = true;
        else if (Input.GetKeyDown(swordKey)) wantBow = false;
        else if (Input.GetKeyDown(switchKey)) wantBow = !BowEquipped;

        if (wantBow != BowEquipped) StartCoroutine(SwitchRoutine(wantBow));
    }

    IEnumerator SwitchRoutine(bool toBow)
    {
        switching = true;
        swapDone = false;
        swapTarget = toBow;
        CancelDraw();

        player.StartEquip(equipDuration, toBow ? equipBowAnim : equipSwordAnim);

        float t = 0f;
        while (t < equipDuration)
        {
            t += Time.deltaTime;
            if (!swapDone && !swapOnAnimationEvent && t >= swapTime) DoSwap();
            yield return null;
        }

        if (!swapDone) DoSwap();   // safety: never leave it half done
        switching = false;
    }

    // Optional: call this from an Animation Event on your equip clips (tick "Swap On Animation Event")
    public void AnimEvent_SwapWeapons()
    {
        if (switching && !swapDone) DoSwap();
    }

    void DoSwap()
    {
        swapDone = true;
        ApplyMode(swapTarget);
    }

    // ───────────────────────── Drawing and shooting ─────────────────────────

    void BeginDraw()
    {
        IsDrawing = true;
        drawTimer = 0f;
        Charge = 0f;
        if (drawSound != null) AudioSource.PlayClipAtPoint(drawSound, transform.position);
    }

    // Called when you let go of the fire button
    void Release()
    {
        IsDrawing = false;

        if (Charge < minCharge)
        {
            Charge = 0f;      // let go too early: the shot is cancelled
            return;
        }

        Fire(Charge);
        Charge = 0f;
        IsShooting = true;
        shootTimer = shootAnimTime;
        lastShotTime = Time.time;
    }

    public void CancelDraw()
    {
        if (IsShooting) return;
        IsDrawing = false;
        Charge = 0f;
    }

    void Fire(float charge)
    {
        Vector3 aimPoint = ComputeAimPoint(out _);

        Transform pt = player.transform;
        Vector3 origin = arrowSpawn != null
            ? arrowSpawn.position
            : pt.position + Vector3.up * 1.4f + pt.forward * 0.6f + pt.right * 0.15f;

        float speed = Mathf.Lerp(minSpeed, maxSpeed, charge);
        float damage = Mathf.Lerp(minDamage, maxDamage, charge);

        // Aim a little high so gravity brings the arrow down onto the crosshair
        if (compensateDrop)
        {
            float travel = Vector3.Distance(origin, aimPoint) / Mathf.Max(speed, 0.1f);
            aimPoint += Vector3.up * 0.5f * arrowGravity * travel * travel;
        }

        Vector3 dir = aimPoint - origin;
        if (dir.sqrMagnitude < 4f) dir = cameraTransform.forward;   // aiming at something right in front of you
        dir.Normalize();

        Arrow arrow;
        if (arrowPrefab != null)
        {
            GameObject go = Instantiate(arrowPrefab, origin, Quaternion.LookRotation(dir));
            arrow = go.GetComponent<Arrow>();
            if (arrow == null) arrow = go.AddComponent<Arrow>();
        }
        else arrow = Arrow.CreateDefault(origin, dir);

        arrow.Launch(pt, dir * speed, damage, arrowGravity, arrowLifetime, stickTime);

        if (!unlimitedArrows) arrows = Mathf.Max(0, arrows - 1);
        if (shootSound != null) AudioSource.PlayClipAtPoint(shootSound, origin);
    }

    public void AddArrows(int amount)
    {
        arrows = Mathf.Clamp(arrows + amount, 0, maxArrows);
    }

    // The point in the world the crosshair is on (an enemy near the crosshair is favoured)
    Vector3 ComputeAimPoint(out EnemyAI assist)
    {
        assist = null;
        Transform cam = cameraTransform != null ? cameraTransform : transform;
        Vector3 camPos = cam.position;
        Vector3 camFwd = cam.forward;

        // What the crosshair is pointing at (ignoring the player's own body)
        Vector3 point = camPos + camFwd * aimMaxDistance;
        RaycastHit[] hits = Physics.RaycastAll(camPos, camFwd, aimMaxDistance, aimMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(player.transform)) continue;
            if (h.distance < best) { best = h.distance; point = h.point; }
        }

        // Aim assist
        if (aimAssistAngle > 0f)
        {
            float bestAngle = aimAssistAngle;
            foreach (var e in EnemyAI.All)
            {
                if (e == null || e.IsDead) continue;
                Vector3 to = e.LockPosition - camPos;
                if (to.magnitude > aimAssistRange) continue;

                float angle = Vector3.Angle(camFwd, to);
                if (angle < bestAngle) { bestAngle = angle; assist = e; }
            }
            if (assist != null) point = assist.LockPosition;
        }

        return point;
    }

    // ───────────────────────── Crosshair + HUD ─────────────────────────

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || player == null) return;

        if (BowEquipped && !unlimitedArrows && showAmmoCount) DrawAmmo();
        if (!IsAiming || !showCrosshair) return;

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        Color c = hoveredEnemy != null ? crosshairEnemyColor : crosshairColor;
        Color prev = GUI.color;
        GUI.color = c;

        if (crosshairSprite != null && crosshairSprite.texture != null)
        {
            float size = crosshairSize * Mathf.Lerp(1.4f, 1f, Charge);    // closes in as you draw
            Texture2D tex = crosshairSprite.texture;
            Rect tr = crosshairSprite.textureRect;
            Rect uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            GUI.DrawTextureWithTexCoords(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), tex, uv);
        }
        else
        {
            float gap = Mathf.Lerp(14f, 4f, Charge);     // the ticks close in as you draw
            float len = 9f;
            float th = 2f;
            Texture2D white = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(cx - th * 0.5f, cy - gap - len, th, len), white);    // top
            GUI.DrawTexture(new Rect(cx - th * 0.5f, cy + gap, th, len), white);          // bottom
            GUI.DrawTexture(new Rect(cx - gap - len, cy - th * 0.5f, len, th), white);    // left
            GUI.DrawTexture(new Rect(cx + gap, cy - th * 0.5f, len, th), white);          // right
            GUI.DrawTexture(new Rect(cx - 1.5f, cy - 1.5f, 3f, 3f), white);               // centre dot
        }

        // Draw strength
        if (showChargeBar && IsDrawing)
        {
            float w = 70f, h = 4f;
            float y = cy + 38f;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(cx - w * 0.5f - 1f, y - 1f, w + 2f, h + 2f), Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.2f), Charge);
            GUI.DrawTexture(new Rect(cx - w * 0.5f, y, w * Charge, h), Texture2D.whiteTexture);
        }

        GUI.color = prev;
    }

    void DrawAmmo()
    {
        if (ammoStyle == null)
        {
            ammoStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontSize = 22, fontStyle = FontStyle.Bold };
            ammoStyle.normal.textColor = Color.white;
        }
        Color prev = GUI.color;
        GUI.color = arrows > 0 ? Color.white : new Color(1f, 0.4f, 0.35f);
        GUI.Label(new Rect(Screen.width - 220f, Screen.height - 60f, 200f, 40f), "Arrows  " + arrows, ammoStyle);
        GUI.color = prev;
    }
}