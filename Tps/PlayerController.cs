using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour, IDamageable
{
    public enum State { Idle, Move, Run, Guard, GuardWalk, Aim, AimWalk, Equip, Roll, Airborne, Attack, Parry, Hit }

    [Header("References")]
    public Animator animator;
    public Transform cameraTransform;
    public WeaponHitbox sword;               // the sword's hitbox (set up on the blade collider)
    public LockOnSystem lockOn;              // optional: lock-on targeting (auto-found on this object)
    public BowController bow;                // optional: bow + weapon switching (auto-found on this object)

    [Header("Input")]
    public KeyCode attackKey = KeyCode.Mouse0;
    public KeyCode guardKey  = KeyCode.Mouse1;
    public KeyCode parryKey  = KeyCode.Q;
    public KeyCode runKey    = KeyCode.LeftShift;
    public KeyCode rollKey   = KeyCode.Space;
    public KeyCode aimKey    = KeyCode.Mouse1;   // with the bow out: hold to aim (the shield is on your back, so no guard)

    [Header("Movement")]
    public float moveSpeed = 3.5f;           // walk
    public float runSpeed = 6.5f;
    public float guardWalkSpeed = 2.5f;
    public bool canWalkWhileGuarding = true;   // false = holding guard roots you in place

    [Header("Bow Aim Movement")]
    public float aimWalkSpeed = 2.2f;          // walking speed while aiming
    public float aimRotationSpeed = 20f;       // how fast you turn to face where the camera points
    public bool moveWhileDrawing = false;      // false = you stand still while pulling the string

    [Header("Lock-On Movement")]
    [Range(0.2f, 1f)] public float lockedSideSpeedMult = 0.85f;   // strafing left/right while locked on
    [Range(0.2f, 1f)] public float lockedBackSpeedMult = 0.7f;    // moving backwards while locked on
    public float rotationSpeed = 12f;

    [Header("Roll")]
    public float rollSpeed = 7f;
    public float rollDuration = 0.7f;        // match your roll clip length
    public float rollInvulnStart = 0.05f;    // i-frames: seconds into the roll where you can't be hit
    public float rollInvulnEnd = 0.4f;
    public float rollCooldown = 0.3f;

    [Header("Back Jump (roll key while standing still)")]
    public float backstepSpeed = 5f;
    public float backstepDuration = 0.55f;   // match your back jump clip length
    public float backstepHopHeight = 0.25f;  // small hop; 0 = flat slide back
    public float backstepInvulnStart = 0.05f;
    public float backstepInvulnEnd = 0.3f;

    [Header("Air / Falling")]
    public float gravity = -20f;
    public float airAcceleration = 12f;      // steering while falling
    public float ledgeGraceTime = 0.15f;     // time off the ground before the fall animation starts

    [Header("Animation Blend (seconds)")]
    public float locomotionBlend = 0.2f;     // idle / walk / run / guard / strafe
    public float actionBlend = 0.08f;        // attack / parry / roll / back jump / die
    public float hitBlend = 0.05f;           // getting hit

    [Header("Root Motion")]
    [Tooltip("The animation clips move the character (rolls, attacks, hit knockback...). Needs root motion in the clips.")]
    public bool useRootMotion = true;
    public float rootMotionMultiplier = 1f;      // scale how far the clips move you
    public bool rootMotionAttack = true;
    public bool rootMotionParry = true;
    public bool rootMotionRoll = true;           // roll + back jump
    public bool rootMotionHit = true;            // knockback in hit clips
    [Tooltip("Walk / run / guard clips move you instead of the speed numbers. Leave off unless your clips have matching root motion.")]
    public bool rootMotionLocomotion = false;
    [Tooltip("Also apply the clips' turning. Usually leave off (the script turns you).")]
    public bool rootMotionRotation = false;

    [Header("Health")]
    public float maxHealth = 100f;
    public float health;

    [System.Serializable]
    public class AttackStep
    {
        [Tooltip("Name of this attack's state in the Animator")]
        public string animName = AnimId.Attack1;
        public float damage = 10f;
        [Tooltip("Seconds to blend into this attack")]
        public float blend = 0.08f;

        [Header("Timing: 0 to 1 = how far through the clip")]
        [Range(0f, 1f)] public float hitStart = 0.30f;     // sword hitbox turns on
        [Range(0f, 1f)] public float hitEnd = 0.55f;       // sword hitbox turns off
        [Range(0f, 1f), Tooltip("From here on, pressing attack again starts the next hit")]
        public float chainFrom = 0.50f;
        [Range(0f, 1.5f), Tooltip("The attack ends here if you did not chain")]
        public float endAt = 0.90f;

        [Header("Backup timing in seconds (used only if the clip can't be read)")]
        public float duration = 0.8f;
        public float hitOnTime = 0.25f;
        public float hitOffTime = 0.5f;
    }

    [Header("Attack Combo")]
    [Tooltip("One entry per hit, in order. Pressing attack again chains into the next one.")]
    public AttackStep[] attackCombo =
    {
        new AttackStep { animName = AnimId.Attack1, damage = 10f },
        new AttackStep { animName = AnimId.Attack2, damage = 12f },
        new AttackStep { animName = AnimId.Attack3, damage = 18f, hitStart = 0.35f, hitEnd = 0.60f, chainFrom = 0.9f, endAt = 0.95f, duration = 1.1f }
    };
    [Tooltip("If you wait longer than this after a hit, the next attack starts from hit 1 again")]
    public float comboResetTime = 0.6f;
    [Tooltip("Pressing attack early is remembered and used as soon as the chain window opens")]
    public bool bufferAttackInput = true;
    public float attackMoveSpeed = 0f;       // 0 = can't walk while attacking

    public enum HitboxTiming { AnimationProgress, Seconds, AnimationEvents }
    [Tooltip("AnimationProgress: hitbox follows the real attack clip (best). Seconds: uses each step's backup seconds. AnimationEvents: call AnimEvent_HitboxOn / Off from the clips.")]
    public HitboxTiming hitboxTiming = HitboxTiming.AnimationProgress;
    public float attackMaxDuration = 2.5f;   // safety: one hit can never last longer than this
    public bool debugAttack = false;         // logs sword / combo info in the Console

    [Header("Parry")]
    public float parryDuration = 0.5f;       // total length of the parry animation
    public float parryWindow = 0.25f;        // active frames where an incoming hit gets parried
    public float parryCooldown = 0.6f;

    [Header("Guard")]
    [Range(0f, 1f)] public float guardDamageMultiplier = 0.2f; // 0.2 = take 20% damage while blocking
    public float guardAngle = 120f;          // front arc that counts as "blocked"
    public float guardHitStun = 0.25f;

    [Header("Hit")]
    public float hitStun = 0.45f;

    public State CurrentState { get; private set; } = State.Idle;
    public bool IsDead { get; private set; }
    public int CurrentAttackIndex => comboIndex;      // 0 = first hit of the combo, 1 = second, 2 = third
    public bool SwordActive => swordOn;               // true while the sword hitbox is on (the swing itself)
    public bool IsBackJumping { get; private set; }   // true during the back jump (so effects can tell it from a roll)

    CharacterController cc;
    float stateTimer;
    int comboIndex;
    float lastAttackEndTime = -99f;
    bool attackQueued;
    float lastParryTime = -99f;
    float verticalVelocity;
    Vector3 horizontalMove;

    // ground / air
    Vector3 lastGroundMove;
    Vector3 airVelocity;
    float airMaxSpeed;
    float airTimer;
    float offGroundTime;

    // roll
    Vector3 rollDirection;
    float lastRollTime = -99f;
    float dodgeSpeed, dodgeDuration, dodgeInvulnStart, dodgeInvulnEnd;   // set per dodge (roll or back jump)

    // Animator float parameters (drive the Walk / GuardWalk blend trees)
    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int MoveXHash = Animator.StringToHash("MoveX");
    static readonly int MoveYHash = Animator.StringToHash("MoveY");

    // Animations are played by Animator STATE NAME (see AnimId.cs) with a cross-fade
    string currentAnim;

    // blend < 0 uses locomotionBlend. restart = true lets the same animation blend into itself (smooth re-trigger).
    void PlayAnim(string animName, float blend = -1f, bool restart = false)
    {
        if (animator == null) return;
        if (!restart && currentAnim == animName) return;
        currentAnim = animName;
        AnimPlay.CrossFade(animator, animName, blend < 0f ? locomotionBlend : blend);
    }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = useRootMotion;
            if (useRootMotion) RootMotionRelay.Attach(animator).onAnimatorMove = OnRootMotion;   // controller applies it
        }
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        if (lockOn == null) lockOn = GetComponent<LockOnSystem>();
        if (bow == null) bow = GetComponent<BowController>();
        if (sword == null) sword = GetComponentInChildren<WeaponHitbox>(true);
        if (sword != null) sword.owner = transform;
        else Debug.LogWarning("PlayerController: no sword WeaponHitbox found. Add a WeaponHitbox to the sword's collider and drag it into Sword.", this);
        health = maxHealth;
        if (attackCombo == null || attackCombo.Length == 0) attackCombo = new[] { new AttackStep() };
    }

    void Update()
    {
        if (IsDead) return;

        horizontalMove = Vector3.zero;

        switch (CurrentState)
        {
            case State.Idle:
            case State.Move:
            case State.Run:
            case State.Guard:
            case State.GuardWalk:
                UpdateFree();
                break;
            case State.Aim:
            case State.AimWalk:  UpdateAim();      break;
            case State.Equip:    UpdateEquip();    break;
            case State.Roll:     UpdateRoll();     break;
            case State.Airborne: UpdateAirborne(); break;
            case State.Attack:   UpdateAttack();   break;
            case State.Parry:    UpdateParry();    break;
            case State.Hit:      UpdateHit();      break;
        }

        // Gravity + movement in one call
        if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;
        cc.Move((horizontalMove + Vector3.up * verticalVelocity) * Time.deltaTime);
    }

    // ───────────────────────── FREE STATES (idle / walk / run / guard / guard walk) ─────────────────────────

    void UpdateFree()
    {
        bool bowMode = bow != null && bow.BowEquipped;       // bow out: sword + shield are on the back
        bool guarding = !bowMode && Input.GetKey(guardKey);

        // Parry (works whether or not guard is held)
        if (!bowMode && Input.GetKeyDown(parryKey) && Time.time >= lastParryTime + parryCooldown)
        {
            StartParry();
            return;
        }

        // Attack (can't attack while guarding)
        if (!bowMode && !guarding && Input.GetKeyDown(attackKey))
        {
            StartAttack();
            return;
        }

        // Roll (also works out of guard)
        if (Input.GetKeyDown(rollKey) && cc.isGrounded && Time.time >= lastRollTime + rollCooldown)
        {
            StartRoll();
            return;
        }

        // Bow: hold aim to raise the bow
        if (bowMode && Input.GetKey(aimKey) && cc.isGrounded)
        {
            EnterAim();
            return;
        }

        // Walked off a ledge -> fall
        if (cc.isGrounded) offGroundTime = 0f;
        else offGroundTime += Time.deltaTime;
        if (offGroundTime > ledgeGraceTime)
        {
            EnterAirborne();
            return;
        }

        // Movement input, relative to camera
        Vector2 input = GetMoveInput();
        bool moving = input.sqrMagnitude > 0.01f;
        Vector3 moveDir = MoveDirFromInput(input);

        bool running = !guarding && moving && Input.GetKey(runKey);
        bool locked = lockOn != null && lockOn.HasTarget;

        // While locked on, pick forward / strafe / backward animations relative to the target
        string lockedAnim = AnimId.Idle;
        float dirMult = 1f;
        if (locked && moving && !guarding)
            lockedAnim = LockedLocomotionAnim(moveDir, running, out dirMult);

        float speed = guarding ? (canWalkWhileGuarding ? guardWalkSpeed : 0f) : (running ? runSpeed : moveSpeed);
        speed *= dirMult;
        horizontalMove = moveDir * speed;
        lastGroundMove = horizontalMove;
        if (useRootMotion && rootMotionLocomotion) horizontalMove = Vector3.zero;   // the clips move the character

        // Rotation: locked on = always face the target; guarding = face the camera; otherwise face movement
        if (lockOn != null && lockOn.HasTarget) RotateTowards(lockOn.FlatDirectionFrom(transform.position));
        else if (guarding) RotateTowards(CamForward());
        else if (moving) RotateTowards(moveDir);

        // State
        if (guarding)      SetState(moving && canWalkWhileGuarding ? State.GuardWalk : State.Guard);
        else if (!moving)  SetState(State.Idle);
        else               SetState(running ? State.Run : State.Move);

        // Animation
        switch (CurrentState)
        {
            case State.GuardWalk: PlayAnim(AnimId.GuardWalk); break;
            case State.Guard:     PlayAnim(AnimId.Guard);     break;
            case State.Run:       PlayAnim(locked ? lockedAnim : AnimId.Run);  break;
            case State.Move:      PlayAnim(locked ? lockedAnim : AnimId.Walk); break;
            default:              PlayAnim(AnimId.Idle);      break;
        }

        animator.SetFloat(SpeedHash, input.magnitude, 0.1f, Time.deltaTime);
        animator.SetFloat(MoveXHash, input.x, 0.1f, Time.deltaTime);   // for strafing guard-walk blend tree
        animator.SetFloat(MoveYHash, input.y, 0.1f, Time.deltaTime);
    }

    // ───────────────────────── BOW: AIM + SWITCHING WEAPONS ─────────────────────────

    // Can the player change weapons right now? (standing on the ground, not mid-action)
    public bool CanSwitchWeapon
    {
        get
        {
            if (IsDead || cc == null || !cc.isGrounded) return false;
            return CurrentState == State.Idle || CurrentState == State.Move || CurrentState == State.Run
                || CurrentState == State.Guard || CurrentState == State.GuardWalk;
        }
    }

    // Called by BowController when changing weapons. animName may be empty (no animation).
    public void StartEquip(float duration, string animName)
    {
        SetSword(false);
        SetState(State.Equip);
        stateTimer = duration;
        attackQueued = false;
        animator.SetFloat(SpeedHash, 0f);
        if (!string.IsNullOrEmpty(animName)) PlayAnim(animName, actionBlend, true);
    }

    void UpdateEquip()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f) SetState(State.Idle);
    }

    void EnterAim()
    {
        SetState(State.Aim);
        animator.SetFloat(SpeedHash, 0f);
    }

    void UpdateAim()
    {
        // Stop aiming when the button is let go (a shot in the middle of its release animation finishes first)
        bool stillAiming = bow != null && bow.BowEquipped && Input.GetKey(aimKey);
        if (!stillAiming && (bow == null || !bow.IsShooting))
        {
            if (bow != null) bow.CancelDraw();
            SetState(State.Idle);
            return;
        }

        // Roll out of the aim
        if (Input.GetKeyDown(rollKey) && cc.isGrounded && Time.time >= lastRollTime + rollCooldown)
        {
            bow.CancelDraw();
            StartRoll();
            return;
        }

        // Walked off a ledge -> fall
        if (cc.isGrounded) offGroundTime = 0f;
        else offGroundTime += Time.deltaTime;
        if (offGroundTime > ledgeGraceTime)
        {
            bow.CancelDraw();
            EnterAirborne();
            return;
        }

        // Slow strafing, always facing where the camera points
        Vector2 input = GetMoveInput();
        bool canMove = !(bow.IsDrawing && !moveWhileDrawing) && !bow.IsShooting;
        bool moving = canMove && input.sqrMagnitude > 0.01f;

        horizontalMove = moving ? MoveDirFromInput(input) * aimWalkSpeed : Vector3.zero;
        lastGroundMove = horizontalMove;
        if (useRootMotion && rootMotionLocomotion) horizontalMove = Vector3.zero;

        Vector3 face = CamForward();
        if (face.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(face, Vector3.up), aimRotationSpeed * Time.deltaTime);

        SetState(moving ? State.AimWalk : State.Aim);

        // Animation: release > draw > aim idle / aim walk
        if (bow.IsShooting)      PlayAnim(AnimId.BowShoot, actionBlend);
        else if (bow.IsDrawing)  PlayAnim(AnimId.BowDraw, actionBlend);
        else                     PlayAnim(moving ? AnimId.AimWalk : AnimId.AimIdle);

        animator.SetFloat(SpeedHash, input.magnitude, 0.1f, Time.deltaTime);
        animator.SetFloat(MoveXHash, moving ? input.x : 0f, 0.1f, Time.deltaTime);   // for the strafing aim-walk blend tree
        animator.SetFloat(MoveYHash, moving ? input.y : 0f, 0.1f, Time.deltaTime);
    }

    // ───────────────────────── ROLL / FALL ─────────────────────────

    // Roll key: moving = roll that way, standing still = hop backwards
    void StartRoll()
    {
        Vector2 input = GetMoveInput();

        if (input.sqrMagnitude > 0.01f)
        {
            // ROLL
            Vector3 dir = MoveDirFromInput(input).normalized;
            dir.y = 0f;
            rollDirection = dir;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

            dodgeSpeed = rollSpeed;
            dodgeDuration = rollDuration;
            dodgeInvulnStart = rollInvulnStart;
            dodgeInvulnEnd = rollInvulnEnd;
            IsBackJumping = false;
            PlayAnim(AnimId.Roll, actionBlend, true);
        }
        else
        {
            // BACK JUMP: move backwards but keep facing forward
            rollDirection = -transform.forward;

            dodgeSpeed = backstepSpeed;
            dodgeDuration = backstepDuration;
            dodgeInvulnStart = backstepInvulnStart;
            dodgeInvulnEnd = backstepInvulnEnd;
            if (backstepHopHeight > 0f) verticalVelocity = Mathf.Sqrt(backstepHopHeight * -2f * gravity);
            IsBackJumping = true;
            PlayAnim(AnimId.BackJump, actionBlend, true);
        }

        SetState(State.Roll);
        stateTimer = 0f;
        lastRollTime = Time.time;
        animator.SetFloat(SpeedHash, 0f);
    }

    void UpdateRoll()
    {
        stateTimer += Time.deltaTime;

        // Slightly fast at the start, slower toward the end
        float t = Mathf.Clamp01(stateTimer / dodgeDuration);
        if (!(useRootMotion && rootMotionRoll))   // with root motion the roll clip moves you
            horizontalMove = rollDirection * dodgeSpeed * Mathf.Lerp(1.2f, 0.6f, t);

        if (stateTimer >= dodgeDuration) SetState(State.Idle);
    }

    bool IsInvulnerable =>
        CurrentState == State.Roll && stateTimer >= dodgeInvulnStart && stateTimer <= dodgeInvulnEnd;

    void EnterAirborne()
    {
        SetState(State.Airborne);
        airTimer = 0f;
        airVelocity = lastGroundMove;                                   // keep your momentum off the ledge
        airMaxSpeed = Mathf.Max(moveSpeed, lastGroundMove.magnitude);
        PlayAnim(AnimId.Fall);
    }

    void UpdateAirborne()
    {
        airTimer += Time.deltaTime;

        // Air steering
        Vector2 input = GetMoveInput();
        Vector3 moveDir = MoveDirFromInput(input);
        airVelocity = Vector3.MoveTowards(airVelocity, moveDir * airMaxSpeed, airAcceleration * Time.deltaTime);
        horizontalMove = airVelocity;
        if (moveDir.sqrMagnitude > 0.01f) RotateTowards(moveDir);

        PlayAnim(AnimId.Fall);

        // Landed
        if (cc.isGrounded && verticalVelocity <= 0f && airTimer > 0.1f)
        {
            offGroundTime = 0f;
            SetState(State.Idle);
        }
    }

    // ───────────────────────── ATTACK ─────────────────────────

    void StartAttack()
    {
        // Attacking again soon after a hit continues the combo, otherwise start over from hit 1
        if (Time.time - lastAttackEndTime > comboResetTime) comboIndex = 0;
        BeginAttackStep(comboIndex);
    }

    void BeginAttackStep(int index)
    {
        comboIndex = Mathf.Clamp(index, 0, attackCombo.Length - 1);
        AttackStep step = attackCombo[comboIndex];

        if (debugAttack)
            Debug.Log("Attack " + (comboIndex + 1) + "/" + attackCombo.Length + " (" + step.animName + ") | sword: "
                      + (sword != null ? sword.name : "NOT ASSIGNED") + " | timing: " + hitboxTiming, this);

        // Face the lock-on target, or steer toward the stick/keys between hits
        if (lockOn != null && lockOn.HasTarget) SnapToLockTarget();
        else
        {
            Vector2 input = GetMoveInput();
            if (input.sqrMagnitude > 0.01f)
            {
                Vector3 d = MoveDirFromInput(input);
                d.y = 0f;
                if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(d, Vector3.up);
            }
        }

        SetState(State.Attack);
        stateTimer = 0f;
        attackQueued = false;
        animator.SetFloat(SpeedHash, 0f);
        if (sword != null) sword.damage = step.damage;
        PlayAnim(step.animName, step.blend, true);
    }

    void UpdateAttack()
    {
        stateTimer += Time.deltaTime;
        AttackStep step = attackCombo[comboIndex];

        // Walking while attacking is off by default (attackMoveSpeed = 0)
        if (attackMoveSpeed > 0f)
            horizontalMove = MoveDirFromInput(GetMoveInput()) * attackMoveSpeed;

        // Where are we in this hit? Follow the real clip when possible, otherwise fall back to seconds.
        float progress = hitboxTiming == HitboxTiming.AnimationProgress ? GetAnimProgress(step.animName) : -1f;
        bool useProgress = progress >= 0f;

        // Sword hitbox window
        if (hitboxTiming != HitboxTiming.AnimationEvents)
        {
            bool inWindow = useProgress
                ? (progress >= step.hitStart && progress < step.hitEnd)
                : (stateTimer >= step.hitOnTime && stateTimer < step.hitOffTime);
            SetSword(inWindow);
        }

        // Combo input: remember presses, chain once the chain window is open
        bool hasNext = comboIndex + 1 < attackCombo.Length;
        bool chainOpen = useProgress ? progress >= step.chainFrom : stateTimer >= step.duration * step.chainFrom;

        if (Input.GetKeyDown(attackKey) && (bufferAttackInput || chainOpen)) attackQueued = true;

        if (attackQueued && hasNext && chainOpen)
        {
            SetSword(false);
            BeginAttackStep(comboIndex + 1);
            return;
        }

        // End of this hit
        bool finished = useProgress ? progress >= step.endAt : stateTimer >= step.duration;
        if (stateTimer >= Mathf.Max(step.duration, attackMaxDuration)) finished = true;   // safety net

        if (finished)
        {
            SetSword(false);
            lastAttackEndTime = Time.time;

            if (attackQueued) BeginAttackStep(hasNext ? comboIndex + 1 : 0);   // pressed during recovery: keep going
            else
            {
                comboIndex = hasNext ? comboIndex + 1 : 0;                       // tap again soon to continue the chain
                SetState(State.Idle);
            }
        }
    }

    // Optional: call these from Animation Events on the attack clip (and set Hitbox Timing to AnimationEvents)
    public void AnimEvent_HitboxOn()  { SetSword(true); }
    public void AnimEvent_HitboxOff() { SetSword(false); }

    // ───────────────────────── PARRY ─────────────────────────

    void StartParry()
    {
        SnapToLockTarget();
        SetState(State.Parry);
        stateTimer = 0f;
        lastParryTime = Time.time;
        animator.SetFloat(SpeedHash, 0f);
        PlayAnim(AnimId.Parry, actionBlend, true);
    }

    void UpdateParry()
    {
        stateTimer += Time.deltaTime;
        if (stateTimer >= parryDuration) SetState(State.Idle);
    }

    bool IsParryActive => CurrentState == State.Parry && stateTimer <= parryWindow;

    // ───────────────────────── HIT ─────────────────────────

    void UpdateHit()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f) SetState(State.Idle);
    }

    public void TakeDamage(DamageInfo info)
    {
        if (IsDead) return;
        if (IsInvulnerable) { info.onResult?.Invoke(HitResult.Dodged); return; }   // rolling i-frames

        bool attackerInFront = IsInFront(info.source);

        // 1) PARRY: negates damage and staggers the attacker
        if (IsParryActive && attackerInFront)
        {
            var parryable = info.source != null ? info.source.GetComponentInParent<IParryable>() : null;
            info.onResult?.Invoke(HitResult.Parried);
            parryable?.OnParried();
            return;
        }

        // 2) GUARD: reduced damage + hit reaction
        bool guarding = CurrentState == State.Guard || CurrentState == State.GuardWalk;
        if (guarding && attackerInFront)
        {
            info.onResult?.Invoke(HitResult.Blocked);
            health -= info.amount * guardDamageMultiplier;
            PlayAnim(AnimId.HitReaction, hitBlend, true);
            EnterHit(guardHitStun);
            if (health <= 0f) Die();
            return;
        }

        // 3) CLEAN HIT
        info.onResult?.Invoke(HitResult.Hit);
        health -= info.amount;
        PlayAnim(AnimId.Hit, hitBlend, true);
        EnterHit(hitStun);
        if (health <= 0f) Die();
    }

    void EnterHit(float stun)
    {
        if (sword != null) sword.Disable();      // getting hit cancels your swing
        comboIndex = 0;
        attackQueued = false;
        if (bow != null) bow.CancelDraw();
        SetState(State.Hit);
        stateTimer = stun;
        animator.SetFloat(SpeedHash, 0f);
    }

    void Die()
    {
        IsDead = true;
        if (bow != null) bow.CancelDraw();
        if (sword != null) sword.Disable();
        PlayAnim(AnimId.Die, actionBlend, true);
        cc.enabled = false;
    }

    // ───────────────────────── HELPERS ─────────────────────────

    Vector2 GetMoveInput()
    {
        return Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
    }

    Vector3 CamForward() => Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
    Vector3 CamRight()   => Vector3.ProjectOnPlane(cameraTransform.right,   Vector3.up).normalized;

    Vector3 MoveDirFromInput(Vector2 input) => CamForward() * input.y + CamRight() * input.x;

    bool IsInFront(Transform source)
    {
        if (source == null) return false;
        Vector3 to = Vector3.ProjectOnPlane(source.position - transform.position, Vector3.up);
        if (to.sqrMagnitude < 0.0001f) return true;
        return Vector3.Angle(transform.forward, to) <= guardAngle * 0.5f;
    }

    // Which animation to play while locked on: forward, strafe left/right, or backwards,
    // judged against the direction to the target (the player always faces the target).
    string LockedLocomotionAnim(Vector3 moveDir, bool running, out float speedMult)
    {
        speedMult = 1f;

        Vector3 fwd = lockOn.FlatDirectionFrom(transform.position);
        if (fwd.sqrMagnitude < 0.0001f) return running ? AnimId.Run : AnimId.Walk;

        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float f = Vector3.Dot(moveDir, fwd);     // + toward the target, - away
        float r = Vector3.Dot(moveDir, right);   // + right, - left

        // Forward/back wins on diagonals
        if (Mathf.Abs(f) >= Mathf.Abs(r) - 0.01f)
        {
            if (f >= 0f) return running ? AnimId.Run : AnimId.Walk;
            speedMult = lockedBackSpeedMult;
            return running ? AnimId.RunBack : AnimId.WalkBack;
        }

        speedMult = lockedSideSpeedMult;
        if (r > 0f) return running ? AnimId.RunRight : AnimId.StrafeRight;
        return running ? AnimId.RunLeft : AnimId.StrafeLeft;
    }

    // ───────────────────────── ROOT MOTION ─────────────────────────

    // Called by Unity (through RootMotionRelay) every frame the Animator moves
    void OnRootMotion()
    {
        if (!useRootMotion || IsDead || animator == null || !cc.enabled) return;
        if (!StateUsesRootMotion()) return;

        Vector3 delta = animator.deltaPosition * rootMotionMultiplier;
        delta.y = 0f;                                   // gravity and hops are handled by the script
        if (delta.sqrMagnitude > 0f) cc.Move(delta);

        if (rootMotionRotation) transform.rotation = animator.deltaRotation * transform.rotation;
    }

    bool StateUsesRootMotion()
    {
        switch (CurrentState)
        {
            case State.Attack: return rootMotionAttack;
            case State.Parry:  return rootMotionParry;
            case State.Roll:   return rootMotionRoll;
            case State.Hit:    return rootMotionHit;
            case State.Idle:
            case State.Move:
            case State.Run:
            case State.Guard:
            case State.GuardWalk: return rootMotionLocomotion;
            default: return false;   // airborne
        }
    }

    // How far through the named animation state we are (0 to 1, can pass 1). -1 = that state isn't playing yet.
    float GetAnimProgress(string stateName)
    {
        if (animator == null) return -1f;
        int hash = Animator.StringToHash(stateName);

        if (animator.IsInTransition(0))
        {
            var next = animator.GetNextAnimatorStateInfo(0);
            return next.shortNameHash == hash ? next.normalizedTime : -1f;
        }

        var cur = animator.GetCurrentAnimatorStateInfo(0);
        return cur.shortNameHash == hash ? cur.normalizedTime : -1f;
    }

    bool swordOn;
    void SetSword(bool on)
    {
        if (sword == null) return;
        if (on) sword.Enable(); else sword.Disable();
        if (debugAttack && on != swordOn) Debug.Log("Player sword hitbox " + (on ? "ON" : "OFF") + " (" + sword.name + ")", this);
        swordOn = on;
    }

    // Instantly face the locked target (used when starting an attack or parry)
    void SnapToLockTarget()
    {
        if (lockOn == null || !lockOn.HasTarget) return;
        Vector3 d = lockOn.FlatDirectionFrom(transform.position);
        if (d.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(d, Vector3.up);
    }

    void RotateTowards(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, rotationSpeed * Time.deltaTime);
    }

    void SetState(State s) => CurrentState = s;
}
