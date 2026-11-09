using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// ─────────────────────────────────────────────────────────────────────────────
//  DATA: attacks, combos, boss phases (all editable in the inspector)
// ─────────────────────────────────────────────────────────────────────────────

public enum AttackKind { Melee, Projectile, Summon }

[System.Serializable]
public class EnemyAttack
{
    public string name = "Attack";

    [Tooltip("Melee = swings a weapon hitbox. Projectile = shoots something. Summon = calls things up out of the ground.")]
    public AttackKind kind = AttackKind.Melee;

    [Tooltip("Name of this attack's state in the Animator (e.g. Attack1, Shoot, Summon ...)")]
    public string animName = AnimId.Attack1;
    [Tooltip("Seconds to blend into this attack from whatever was playing")]
    public float blendTime = 0.1f;

    public float damage = 10f;

    [Header("When it can be used")]
    public float minRange = 0f;
    public float maxRange = 2.2f;
    [Tooltip("Higher = picked more often")]
    public float weight = 1f;
    [Range(0f, 1f), Tooltip("Enemy needs at least this much confidence to use it")]
    public float minConfidence = 0f;
    [Tooltip("Boss only: unlocked from this phase on (0 = always)")]
    public int minPhase = 0;

    [Header("Timing (seconds from the start of the animation)")]
    public float hitboxOnTime = 0.4f;
    public float hitboxOffTime = 0.7f;
    [Tooltip("Projectile / Summon only: the moment it fires or summons")]
    public float actionTime = 0.5f;
    public float duration = 1.0f;
    [Tooltip("Extra pause after this attack before the next decision")]
    public float extraRecovery = 0f;

    [Header("Hitbox / movement (Melee)")]
    [Tooltip("Which weapon hitbox to enable (index in the Weapons list). -1 = all of them")]
    public int hitboxIndex = 0;
    public bool trackTargetDuringWindup = true;
    [Tooltip("The clip itself moves the enemy (root motion). Turn off to use the lunge settings below instead.")]
    public bool useRootMotion = true;
    [Tooltip("Step forward during the attack (0 = stay put)")]
    public float lungeSpeed = 0f;
    public float lungeStart = 0.1f;
    public float lungeEnd = 0.4f;

    [Header("Projectile (kind = Projectile)")]
    [Tooltip("Optional. Empty = a built-in glowing orb with a trail.")]
    public GameObject projectilePrefab;
    [Tooltip("Optional. Where it comes out (a hand / staff tip). Empty = the enemy's default Muzzle.")]
    public Transform muzzle;
    public int projectileCount = 1;
    [Tooltip("Total fan angle when firing more than one")]
    public float spreadAngle = 20f;
    public float projectileSpeed = 12f;
    public float projectileLifetime = 5f;
    public float projectileSize = 0.35f;
    public Color projectileColor = new Color(1f, 0.45f, 0.15f, 1f);
    [Tooltip("Aims where the player is going, not where they are")]
    public bool predictTarget = true;
    public bool homing = false;
    [Tooltip("How sharply a homing shot turns (radians per second)")]
    public float homingStrength = 1.5f;
    [Tooltip("A well-timed parry sends it back at the shooter")]
    public bool parryReflects = true;

    [Header("Summon (kind = Summon)")]
    [Tooltip("The thing to summon (a prefab, usually an enemy with EnemyAI)")]
    public GameObject summonPrefab;
    public int summonCount = 2;
    [Tooltip("Won't summon while this many of its summons are alive")]
    public int maxAlive = 4;
    public float summonRadius = 3.5f;
    [Tooltip("Off = they appear around the summoner. On = they appear around the player.")]
    public bool summonAroundPlayer = false;
    [Tooltip("Delay between each summoned thing")]
    public float spawnStagger = 0.25f;
    [Tooltip("Smoke shows on the ground this long BEFORE the thing comes out")]
    public float smokeLeadTime = 1.2f;
    [Tooltip("How long it takes to come out of the smoke")]
    public float riseTime = 0.8f;
    public float smokeRadius = 1.1f;
    public Color smokeColor = new Color(0.30f, 0.26f, 0.34f, 0.75f);
    [Tooltip("Optional: your own smoke ParticleSystem. Empty = built-in smoke.")]
    public ParticleSystem smokePrefab;
}

[System.Serializable]
public class EnemyCombo
{
    public string name = "Combo";
    [Tooltip("Indexes into the Attacks list, in the order they are played. e.g. 0, 1, 2")]
    public int[] attackIndices;
    public float weight = 1f;
    [Range(0f, 1f)] public float minConfidence = 0f;
    public int minPhase = 0;
}

[System.Serializable]
public class BossPhase
{
    public string name = "Phase 2";
    [Range(0f, 1f), Tooltip("Phase starts when health falls to this fraction (0.66 = 66%)")]
    public float startsAtHealth = 0.66f;
    public float speedMult = 1.1f;
    public float damageMult = 1.15f;
    [Tooltip("Lower = attacks more often")]
    public float cooldownMult = 0.85f;
    [Range(0f, 1f)] public float confidenceBoost = 0.2f;
}

// ─────────────────────────────────────────────────────────────────────────────
//  ENEMY AI
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour, IDamageable, IParryable
{
    public enum State { Idle, Combat, Attack, Staggered, Hit, PhaseChange, Dead }
    public enum EnemyType { Melee, Ranged, Summoner }
    enum Mode { Approach, Circle, Retreat }

    // Every living enemy registers here so LockOnSystem can find them (no layers or colliders needed)
    public static readonly List<EnemyAI> All = new List<EnemyAI>();

    [Header("References")]
    public Transform target;                 // auto-finds the object tagged "Player" if empty
    public Animator animator;
    [Tooltip("Weapon hitboxes (e.g. right hand, left hand). Attacks choose one by index.")]
    public WeaponHitbox[] weapons;

    [Header("Enemy Type")]
    [Tooltip("Melee = fights up close. Ranged = keeps its distance and shoots. Summoner = stays back and calls things up.")]
    public EnemyType enemyType = EnemyType.Melee;
    [Tooltip("Ranged / Summoner: the distance it likes to stand at")]
    public float rangedPreferredDistance = 9f;
    [Tooltip("Ranged / Summoner: if the player gets closer than this, it backs away")]
    public float keepAwayDistance = 5f;
    [Tooltip("Default place projectiles come out of (a hand or staff tip). Empty = in front of the chest.")]
    public Transform muzzle;
    [Tooltip("How high above the player's feet it aims")]
    public float aimHeight = 1f;
    [Tooltip("When this enemy dies, the things it summoned die too")]
    public bool killMinionsOnDeath = true;
    [Tooltip("Optional: Animator state played when THIS enemy is summoned and comes out of the ground")]
    public string emergeAnim = "";

    [Header("Detection")]
    public float detectRange = 12f;
    public float loseRange = 18f;

    [Header("Movement")]
    public float chaseSpeed = 3.5f;
    public float circleSpeedMult = 0.55f;
    public float retreatSpeedMult = 0.6f;
    public float turnSpeed = 8f;
    public float repathInterval = 0.2f;
    [Tooltip("Play the Run animation (AnimId.Run) when approaching confidently from far away")]
    public bool useRunAnimation = false;
    public float runSpeedMult = 1.6f;
    public float runMinDistance = 6f;

    [Header("Root Motion")]
    [Tooltip("Attack and hit clips move the enemy. Locomotion still uses the NavMeshAgent.")]
    public bool useRootMotion = true;
    public float rootMotionMultiplier = 1f;
    public bool rootMotionOnHit = true;      // knockback / stagger / phase-change clips move the enemy
    public bool rootMotionRotation = false;  // also apply the clips' turning

    [Header("Animation Blend (seconds)")]
    public float locomotionBlend = 0.2f;     // idle / walk / run / strafe
    public float hitBlend = 0.05f;           // hit / stagger / phase change

    [Header("Spacing (how close it likes to stand)")]
    public float cautiousDistance = 5f;      // preferred distance when it has NO confidence
    public float boldDistance = 1.8f;        // preferred distance when it is FULL of confidence

    [Header("Attacks")]
    public List<EnemyAttack> attacks = new List<EnemyAttack>();
    [Tooltip("Optional series of attacks. Leave empty to just pick single attacks at random.")]
    public List<EnemyCombo> combos = new List<EnemyCombo>();
    public float baseAttackCooldown = 1.5f;
    public bool useAnimationEvents = false;  // true = call AnimEvent_HitboxOn / Off from the attack clips

    [Header("Confidence")]
    [Range(0f, 1f)] public float startConfidence = 0.5f;
    [Range(0f, 1f)] public float confidence = 0.5f;   // current value (watch it change while playing)
    public float hitGain = 0.15f;            // landed a hit on the player
    public float blockedGain = 0.03f;        // player blocked it
    public float whiffPenalty = 0.06f;       // player rolled through it
    public float parriedPenalty = 0.25f;     // got parried
    public float hurtPenalty = 0.08f;        // took damage
    public float recoverRate = 0.03f;        // per second, drifts back toward startConfidence
    [Range(0f, 1f)] public float cautiousThreshold = 0.3f;
    [Range(0f, 1f)] public float boldThreshold = 0.7f;
    public bool showConfidenceDebug = false; // draws confidence + mode over its head

    [Header("Health")]
    public float maxHealth = 60f;
    public float health;
    public float hitStun = 0.4f;
    public bool hitInterruptsAttack = true;  // false = "super armor" while attacking

    [Header("Parry")]
    public float parriedStagger = 1.5f;      // how long it is stunned when parried
    public float staggeredDamageMultiplier = 1.5f;

    [Header("BOSS")]
    public bool isBoss = false;
    public string bossName = "Boss";
    public bool showBossBar = true;
    public bool bossHyperArmor = true;       // attacks are not interrupted by hits
    [Range(0.1f, 1f)] public float bossStaggerMult = 0.6f;     // boss recovers faster from parry / hits
    [Range(0f, 1f)] public float bossConfidenceFloor = 0.4f;   // boss never gets scared below this
    public float phaseChangeDuration = 2f;
    public List<BossPhase> bossPhases = new List<BossPhase>();

    public enum BarAnchor { Top, Bottom }
    [Header("Boss Bar (leave the sprites empty to use the built-in bar)")]
    public BarAnchor bossBarAnchor = BarAnchor.Bottom;
    public Vector2 bossBarSize = new Vector2(700f, 22f);     // max width, height
    public float bossBarMargin = 50f;                        // distance from the screen edge
    public bool showBossTrail = true;                        // the pale "damage just taken" part
    [Tooltip("Colours the sprites below (white = unchanged)")]
    public Color bossBarTint = Color.white;
    public Sprite bossBarBackground;                         // behind the health
    public Sprite bossBarFill;                               // the health itself (shrinks from the right)
    public Sprite bossBarTrail;                              // optional: the delayed damage part
    public Sprite bossBarFrame;                              // drawn on top, a bit bigger than the bar
    public Vector2 bossBarFramePadding = new Vector2(10f, 8f);
    public Sprite bossBarNameplate;                          // optional: drawn behind the name
    public Vector2 bossNameplateSize = new Vector2(320f, 44f);
    public Font bossNameFont;
    public int bossNameSize = 20;
    public Color bossNameColor = Color.white;
    [Header("Built-in bar colours (used when no sprite is set)")]
    public Color bossFillColor = new Color(0.82f, 0.10f, 0.10f, 1f);
    public Color bossTrailColor = new Color(1f, 0.85f, 0.40f, 1f);
    public Color bossBackColor = new Color(0.25f, 0.05f, 0.05f, 1f);

    [Header("Head Health Bar (normal enemies)")]
    public bool showHeadBar = true;
    [Tooltip("On = always visible. Off = appears when the enemy notices you or is hurt, and fades after Hide Delay.")]
    public bool headBarAlwaysVisible = false;
    public float headBarHeight = 2.3f;       // how far above its feet the bar floats
    [Tooltip("Optional: an empty object above the head (or a head bone) to attach the bar to instead")]
    public Transform headBarAnchor;
    public Vector2 headBarSize = new Vector2(70f, 8f);
    public float headBarMaxDistance = 25f;   // not shown farther away than this
    public float headBarHideDelay = 4f;
    [Tooltip("Optional name shown above the bar (e.g. Skeleton)")]
    public string headBarName = "";
    [Tooltip("Sprites are optional: empty = the built-in bar")]
    public Sprite headBarBackground;
    public Sprite headBarFill;
    public Sprite headBarFrame;
    public Vector2 headBarFramePadding = new Vector2(3f, 3f);
    public Color headBarTint = Color.white;  // colours the sprites above
    public Color headBarFillColor = new Color(0.85f, 0.15f, 0.15f, 1f);
    public Color headBarBackColor = new Color(0f, 0f, 0f, 0.65f);
    public Color headBarTrailColor = new Color(1f, 0.85f, 0.40f, 1f);

    [Header("Victory Banner (boss only)")]
    public bool showVictoryBanner = true;
    public string victoryText = "ENEMY FELLED";
    public Font victoryFont;
    public int victoryFontSize = 72;
    public Color victoryColor = new Color(0.80f, 0.66f, 0.25f, 1f);
    [Tooltip("Optional: use an image instead of text (e.g. your own 'ENEMY FELLED' artwork)")]
    public Sprite victorySprite;
    public Vector2 victorySpriteSize = new Vector2(600f, 120f);
    public AudioClip victorySound;
    public float victoryDelay = 1.5f;      // wait after the death before it appears
    public float victoryFadeIn = 1.2f;
    public float victoryHold = 3f;
    public float victoryFadeOut = 1.5f;
    public float victoryBandHeight = 160f;
    [Range(0f, 1f)] public float victoryBandOpacity = 0.65f;
    public float victorySlowZoom = 1.08f;  // text slowly grows while visible (1 = no zoom)

    [Header("Lock-On")]
    public Transform lockPoint;              // optional: where the lock-on aims. Defaults to chest height.

    public State CurrentState { get; private set; } = State.Idle;
    public bool IsDead => CurrentState == State.Dead;
    public float Confidence => confidence;
    public int Phase => phase;
    public Vector3 LockPosition => lockPoint != null ? lockPoint.position : transform.position + Vector3.up * 1.2f;

    // ── internals ──
    NavMeshAgent agent;
    PlayerController playerCtrl;
    bool hasSpeedParam;
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    Mode mode = Mode.Approach;
    int circleDir = 1;
    float decisionTimer;
    bool wantsToAttack;
    float nextAttackTime;
    float nextRepathTime;
    float stateTimer;
    int phase;
    bool engaged;

    // current attack sequence
    EnemyAttack currentAttack;
    int[] sequence;
    int sequenceStep;
    int lastFirstAttack = -1;
    float attackTimer;
    bool landedThisAttack;
    bool hitboxesOn;
    bool actionFired;

    // aiming + summons
    Vector3 lastTargetPos;
    Vector3 targetVelocity;
    readonly List<GameObject> summoned = new List<GameObject>();
    int pendingSummons;

    class Option { public int[] seq; public float w; }
    readonly List<Option> options = new List<Option>();

    string currentAnim;
    GUIStyle bossLabelStyle, debugStyle;
    float bossBarAlpha;
    float headBarAlpha;
    GUIStyle headNameStyle;
    float trailFrac = 1f;
    float lastDamageTime = -99f;

    // ───────────────────────── SETUP ─────────────────────────

    void OnEnable()  { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = useRootMotion;   // moves are applied through the NavMeshAgent (see OnRootMotion)
            if (useRootMotion) RootMotionRelay.Attach(animator).onAnimatorMove = OnRootMotion;
            if (animator.runtimeAnimatorController != null)
                foreach (var p in animator.parameters)
                    if (p.nameHash == SpeedHash && p.type == AnimatorControllerParameterType.Float) hasSpeedParam = true;
        }
        else Debug.LogWarning("EnemyAI: no Animator found (movement still works, animation won't).", this);

        health = maxHealth;
        confidence = startConfidence;
        if (attacks.Count == 0) attacks.Add(new EnemyAttack());   // so it works out of the box

        // Weapons list left empty? Use any WeaponHitbox found on this enemy.
        if (weapons == null || weapons.Length == 0) weapons = GetComponentsInChildren<WeaponHitbox>(true);
        if (weapons.Length == 0 && enemyType == EnemyType.Melee)
            Debug.LogWarning("EnemyAI: no WeaponHitbox found. Add a WeaponHitbox to the weapon's collider, or the enemy can't damage anyone.", this);

        if (weapons != null)
            foreach (var w in weapons)
            {
                if (w == null) continue;
                w.owner = transform;
                w.OnHitResult += HandleWeaponResult;
            }

        agent.speed = chaseSpeed;
        agent.stoppingDistance = 0.2f;
        agent.autoBraking = true;
    }

    void OnDestroy()
    {
        if (weapons == null) return;
        foreach (var w in weapons) if (w != null) w.OnHitResult -= HandleWeaponResult;
    }

    void Start()
    {
        if (target == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) target = p.transform;
        }
        if (target == null)
            Debug.LogWarning("EnemyAI: no target. Tag your player 'Player' or assign Target in the inspector.", this);
        else
        {
            playerCtrl = target.GetComponentInParent<PlayerController>();
            lastTargetPos = target.position;
        }

        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            else
                Debug.LogWarning("EnemyAI: not on a baked NavMesh. Bake one (Window > AI > Navigation) under the enemy.", this);
        }
    }

    // ───────────────────────── MAIN LOOP ─────────────────────────

    void Update()
    {
        UpdateBossBarValues();   // health bar fades (also drives the head bar of normal enemies)

        if (IsDead) return;

        // How fast the player is moving (used to lead shots)
        if (target != null)
        {
            Vector3 v = (target.position - lastTargetPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            v = Vector3.ClampMagnitude(v, 12f);
            targetVelocity = Vector3.Lerp(targetVelocity, v, 0.2f);
            lastTargetPos = target.position;
        }

        if (animator != null && hasSpeedParam)
            animator.SetFloat(SpeedHash, agent.enabled ? agent.velocity.magnitude / Mathf.Max(agent.speed, 0.01f) : 0f, 0.1f, Time.deltaTime);

        if (target == null || !agent.enabled || !agent.isOnNavMesh) return;

        // Confidence slowly drifts back to its normal level
        confidence = Mathf.MoveTowards(confidence, startConfidence, recoverRate * Time.deltaTime);
        ClampConfidence();

        float dist = FlatDistance(target.position);

        switch (CurrentState)
        {
            case State.Idle:        UpdateIdle(dist);   break;
            case State.Combat:      UpdateCombat(dist); break;
            case State.Attack:      UpdateAttack(dist); break;
            case State.Staggered:
            case State.Hit:
            case State.PhaseChange: UpdateStun();       break;
        }
    }

    // ───────────────────────── IDLE ─────────────────────────

    void UpdateIdle(float dist)
    {
        PlayAnim(AnimId.Idle);
        if (dist <= detectRange) EnterCombat();
    }

    void EnterCombat()
    {
        engaged = true;
        CurrentState = State.Combat;
        agent.isStopped = false;
        decisionTimer = 0f;
        wantsToAttack = false;
        nextAttackTime = Mathf.Max(nextAttackTime, Time.time + 0.6f);
    }

    // Wakes the enemy up and sends it after the player (used for freshly summoned enemies)
    public void Aggro()
    {
        if (IsDead || CurrentState != State.Idle) return;
        EnterCombat();
    }

    // Kills it instantly (used when its summoner dies)
    public void Kill()
    {
        if (IsDead) return;
        health = 0f;
        Die();
    }

    // Lets projectiles report what happened (hit / blocked / dodged) so confidence still works
    public void ReportHitResult(HitResult r) { HandleWeaponResult(r); }

    // ───────────────────────── COMBAT (the "thinking") ─────────────────────────

    void UpdateCombat(float dist)
    {
        // Bosses never lose interest once the fight has started
        if (!isBoss && dist > loseRange)
        {
            agent.ResetPath();
            Halt();
            engaged = false;
            CurrentState = State.Idle;
            return;
        }

        decisionTimer -= Time.deltaTime;
        if (decisionTimer <= 0f) Decide(dist);

        // Don't swing at a player who is mid-roll: wait for the recovery
        bool playerRolling = playerCtrl != null && playerCtrl.CurrentState == PlayerController.State.Roll;

        if (wantsToAttack && Time.time >= nextAttackTime && !playerRolling && TryStartSequence(dist))
            return;

        float preferred = PreferredDistance();
        switch (mode)
        {
            case Mode.Approach: DoApproach(dist, wantsToAttack ? AttackStopDistance() : preferred); break;
            case Mode.Circle:   DoCircle(dist, preferred); break;
            case Mode.Retreat:  DoRetreat(); break;
        }
    }

    // Picks what to do for the next second or so, based on confidence
    void Decide(float dist)
    {
        float c = confidence;
        float preferred = PreferredDistance();

        // Confident enemies think faster
        decisionTimer = Mathf.Lerp(1.2f, 0.35f, c) * Random.Range(0.8f, 1.2f);

        bool ready = Time.time >= nextAttackTime;
        wantsToAttack = ready && Random.value < Mathf.Lerp(0.3f, 1f, c);   // low confidence = hesitates
        circleDir = Random.value < 0.5f ? -1 : 1;

        // Ranged and summoner enemies back away when the player gets too close
        if (enemyType != EnemyType.Melee && dist < keepAwayDistance && Random.value < 0.85f)
        {
            mode = Mode.Retreat;
            wantsToAttack = false;
            return;
        }

        if (wantsToAttack)                      mode = Mode.Approach;                       // go and hit them
        else if (dist > preferred + 1.5f)       mode = Mode.Approach;                       // too far, close in
        else if (c < cautiousThreshold)         mode = (dist < preferred && Random.value < 0.6f) ? Mode.Retreat : Mode.Circle;
        else if (c > boldThreshold)             mode = Random.value < 0.25f ? Mode.Circle : Mode.Approach;   // keeps the pressure on
        else                                    mode = Random.value < 0.5f ? Mode.Circle : Mode.Approach;
    }

    float PreferredDistance()
    {
        if (enemyType != EnemyType.Melee)
            return Mathf.Max(Mathf.Lerp(rangedPreferredDistance + 2f, rangedPreferredDistance - 1.5f, confidence), 3f);
        return Mathf.Max(Mathf.Lerp(cautiousDistance, boldDistance, confidence), 1.2f);
    }

    // How close it walks up when it wants to attack: melee goes right up to you, shooters stay at range
    float AttackStopDistance() => enemyType == EnemyType.Melee ? 0.8f : PreferredDistance();

    // ───────────────────────── MOVEMENT MODES ─────────────────────────

    void DoApproach(float dist, float stopDist)
    {
        agent.updateRotation = true;

        if (dist <= stopDist)
        {
            Halt();
            FaceTarget();
            PlayAnim(AnimId.Idle);
            return;
        }

        bool run = useRunAnimation && confidence > 0.6f && dist > runMinDistance;
        agent.isStopped = false;
        agent.speed = chaseSpeed * SpeedMult * Mathf.Lerp(0.85f, 1.2f, confidence) * (run ? runSpeedMult : 1f);
        Repath(target.position);
        PlayAnim(run ? AnimId.Run : AnimId.Walk);
    }

    void DoCircle(float dist, float preferred)
    {
        agent.updateRotation = false;

        Vector3 toTarget = FlatDir(target.position - transform.position);
        Vector3 tangent = Vector3.Cross(Vector3.up, toTarget) * circleDir;      // + = its right, - = its left
        float radial = Mathf.Clamp(dist - preferred, -1.5f, 1.5f);               // drift toward the preferred distance
        Vector3 dest = transform.position + tangent * 2.5f + toTarget * radial;

        agent.isStopped = false;
        agent.speed = chaseSpeed * circleSpeedMult * SpeedMult;
        RepathSampled(dest);
        FaceTarget();
        PlayAnim(circleDir > 0 ? AnimId.StrafeRight : AnimId.StrafeLeft);
    }

    void DoRetreat()
    {
        agent.updateRotation = false;

        Vector3 toTarget = FlatDir(target.position - transform.position);
        Vector3 dest = transform.position - toTarget * 3f;

        if (!NavMesh.SamplePosition(dest, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
        {
            mode = Mode.Circle;                 // backed into a wall: circle instead
            return;
        }

        agent.isStopped = false;
        agent.speed = chaseSpeed * retreatSpeedMult * SpeedMult;
        if (Time.time >= nextRepathTime) { agent.SetDestination(hit.position); nextRepathTime = Time.time + repathInterval; }
        FaceTarget();
        PlayAnim(AnimId.WalkBack);
    }

    void Halt()
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    void Repath(Vector3 pos)
    {
        if (Time.time < nextRepathTime) return;
        agent.SetDestination(pos);
        nextRepathTime = Time.time + repathInterval;
    }

    void RepathSampled(Vector3 pos)
    {
        if (Time.time < nextRepathTime) return;
        nextRepathTime = Time.time + repathInterval;
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 2f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
    }

    // ───────────────────────── ATTACKS ─────────────────────────

    bool ValidIndex(int i) => i >= 0 && i < attacks.Count;

    // Summoned things still alive (also forgets the ones that died or were destroyed)
    int AliveSummons
    {
        get
        {
            for (int i = summoned.Count - 1; i >= 0; i--)
            {
                GameObject go = summoned[i];
                if (go == null) { summoned.RemoveAt(i); continue; }
                var e = go.GetComponent<EnemyAI>();
                if (e != null && e.IsDead) summoned.RemoveAt(i);
            }
            return summoned.Count;
        }
    }

    bool AttackUsable(EnemyAttack a, float dist)
    {
        if (a == null || a.minPhase > phase || confidence < a.minConfidence) return false;
        if (dist < a.minRange || dist > a.maxRange) return false;

        // Don't summon more while the existing ones are still around
        if (a.kind == AttackKind.Summon && (a.summonPrefab == null || AliveSummons + pendingSummons >= a.maxAlive)) return false;

        return true;
    }

    // Picks a random usable attack (or combo) by weight and starts it
    bool TryStartSequence(float dist)
    {
        options.Clear();

        // Combos: confident enemies favour long chains, nervous ones prefer single strikes
        foreach (var cb in combos)
        {
            if (cb == null || cb.attackIndices == null || cb.attackIndices.Length == 0) continue;
            if (cb.minPhase > phase || confidence < cb.minConfidence) continue;

            int first = cb.attackIndices[0];
            if (!ValidIndex(first) || !AttackUsable(attacks[first], dist)) continue;

            float w = cb.weight * (cb.attackIndices.Length > 1 ? Mathf.Lerp(0.4f, 1.6f, confidence)
                                                               : Mathf.Lerp(1.4f, 0.8f, confidence));
            if (first == lastFirstAttack) w *= 0.4f;   // variety: don't repeat the same opener
            options.Add(new Option { seq = cb.attackIndices, w = w });
        }

        // Single attacks (also the fallback when no combo fits)
        if (options.Count == 0)
        {
            for (int i = 0; i < attacks.Count; i++)
            {
                if (!AttackUsable(attacks[i], dist)) continue;
                float w = attacks[i].weight;
                if (i == lastFirstAttack) w *= 0.4f;
                options.Add(new Option { seq = new[] { i }, w = w });
            }
        }

        if (options.Count == 0) return false;

        float total = 0f;
        foreach (var o in options) total += Mathf.Max(0.0001f, o.w);
        float roll = Random.value * total;
        Option chosen = options[options.Count - 1];
        foreach (var o in options)
        {
            roll -= Mathf.Max(0.0001f, o.w);
            if (roll <= 0f) { chosen = o; break; }
        }

        sequence = chosen.seq;
        sequenceStep = 0;
        lastFirstAttack = sequence[0];
        StartAttackStep();
        return true;
    }

    void StartAttackStep()
    {
        int idx = sequence[sequenceStep];
        if (!ValidIndex(idx)) { FinishSequence(); return; }

        currentAttack = attacks[idx];
        CurrentState = State.Attack;
        attackTimer = 0f;
        landedThisAttack = false;
        actionFired = false;

        Halt();
        agent.updateRotation = false;

        if (weapons != null && currentAttack.kind == AttackKind.Melee)
            foreach (var w in weapons)
                if (w != null) w.damage = currentAttack.damage * DamageMult;

        PlayAnim(currentAttack.animName, currentAttack.blendTime, true);   // restart = true: the same attack can blend into itself
    }

    void UpdateAttack(float dist)
    {
        var a = currentAttack;
        if (a == null) { FinishSequence(); return; }

        attackTimer += Time.deltaTime;

        bool melee = a.kind == AttackKind.Melee;
        float windupEnd = melee ? a.hitboxOnTime : a.actionTime;
        if (a.trackTargetDuringWindup && attackTimer < windupEnd) FaceTarget();

        if (melee)
        {
            if (a.lungeSpeed > 0f && !(useRootMotion && a.useRootMotion) && attackTimer >= a.lungeStart && attackTimer <= a.lungeEnd && dist > 1f)
                agent.Move(transform.forward * a.lungeSpeed * Time.deltaTime);

            if (!useAnimationEvents)
                SetHitboxes(attackTimer >= a.hitboxOnTime && attackTimer < a.hitboxOffTime);
        }
        else if (!actionFired && attackTimer >= a.actionTime)
        {
            actionFired = true;
            PerformAction(a);
        }

        if (attackTimer >= a.duration) EndAttackStep(dist);
    }

    void EndAttackStep(float dist)
    {
        SetHitboxes(false);

        // Continue the combo? Landing the hit makes it much more likely.
        if (sequence != null && sequenceStep + 1 < sequence.Length)
        {
            int nextIdx = sequence[sequenceStep + 1];
            if (ValidIndex(nextIdx))
            {
                float chance = landedThisAttack ? Mathf.Lerp(0.65f, 1f, confidence)
                                                : Mathf.Lerp(0.1f, 0.75f, confidence);
                bool playerRolling = playerCtrl != null && playerCtrl.CurrentState == PlayerController.State.Roll;

                if (!playerRolling && dist <= attacks[nextIdx].maxRange * 1.6f && Random.value < chance)
                {
                    sequenceStep++;
                    StartAttackStep();
                    return;
                }
            }
        }

        FinishSequence();
    }

    void FinishSequence()
    {
        SetHitboxes(false);

        float extra = currentAttack != null ? currentAttack.extraRecovery : 0f;
        // Confident = short pause between attacks, nervous = long pause
        nextAttackTime = Time.time + baseAttackCooldown * Mathf.Lerp(1.7f, 0.5f, confidence)
                                     * CooldownMult * Random.Range(0.85f, 1.15f) + extra;

        sequence = null;
        currentAttack = null;
        wantsToAttack = false;
        decisionTimer = 0.1f;
        CurrentState = State.Combat;
        agent.isStopped = false;
    }

    void SetHitboxes(bool on)
    {
        if (weapons == null || hitboxesOn == on) return;
        hitboxesOn = on;

        int idx = currentAttack != null ? currentAttack.hitboxIndex : -1;
        for (int i = 0; i < weapons.Length; i++)
        {
            var w = weapons[i];
            if (w == null) continue;
            if (!on || idx < 0 || idx == i) { if (on) w.Enable(); else w.Disable(); }
        }
    }

    // Optional Animation Event hooks (tick Use Animation Events)
    public void AnimEvent_HitboxOn()  { SetHitboxes(true); }
    public void AnimEvent_HitboxOff() { SetHitboxes(false); }

    // Optional Animation Event: fire / summon at an exact frame instead of Action Time
    public void AnimEvent_Action()
    {
        if (currentAttack == null || actionFired || currentAttack.kind == AttackKind.Melee) return;
        actionFired = true;
        PerformAction(currentAttack);
    }

    // ───────────────────────── SHOOTING + SUMMONING ─────────────────────────

    void PerformAction(EnemyAttack a)
    {
        if (a.kind == AttackKind.Projectile) FireProjectiles(a);
        else if (a.kind == AttackKind.Summon) StartSummon(a);
    }

    void FireProjectiles(EnemyAttack a)
    {
        if (target == null) return;

        Transform m = a.muzzle != null ? a.muzzle : muzzle;
        Vector3 origin = m != null ? m.position : transform.position + Vector3.up * 1.3f + transform.forward * 0.5f;

        // Aim at the player's chest, leading the shot if they are moving
        Vector3 aim = target.position + Vector3.up * aimHeight;
        if (a.predictTarget)
        {
            float travel = Mathf.Clamp(Vector3.Distance(origin, aim) / Mathf.Max(a.projectileSpeed, 0.1f), 0f, 1.2f);
            aim += new Vector3(targetVelocity.x, 0f, targetVelocity.z) * travel * 0.8f;
        }

        Vector3 baseDir = (aim - origin).normalized;
        int count = Mathf.Max(1, a.projectileCount);

        for (int i = 0; i < count; i++)
        {
            float angle = count > 1 ? Mathf.Lerp(-a.spreadAngle * 0.5f, a.spreadAngle * 0.5f, i / (float)(count - 1)) : 0f;
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * baseDir;

            Projectile p;
            if (a.projectilePrefab != null)
            {
                GameObject go = Instantiate(a.projectilePrefab, origin, Quaternion.LookRotation(dir));
                p = go.GetComponent<Projectile>();
                if (p == null) p = go.AddComponent<Projectile>();
            }
            else p = Projectile.CreateDefault(origin, a.projectileSize, a.projectileColor);

            p.Launch(this, dir, a.projectileSpeed, a.damage * DamageMult, a.projectileLifetime,
                     a.homing, a.homingStrength, a.parryReflects, target);
        }
    }

    void StartSummon(EnemyAttack a)
    {
        if (a.summonPrefab == null)
        {
            Debug.LogWarning("EnemyAI: this Summon attack has no Summon Prefab.", this);
            return;
        }

        int room = Mathf.Max(0, a.maxAlive - AliveSummons - pendingSummons);
        int count = Mathf.Min(a.summonCount, room);
        for (int i = 0; i < count; i++) StartCoroutine(SummonRoutine(a, i));
    }

    // Smoke appears on the ground first, then the thing comes up out of it
    IEnumerator SummonRoutine(EnemyAttack a, int index)
    {
        pendingSummons++;

        yield return new WaitForSeconds(index * a.spawnStagger);
        if (IsDead) { pendingSummons--; yield break; }

        Vector3 center = (a.summonAroundPlayer && target != null) ? target.position : transform.position;
        float minDist = a.summonAroundPlayer ? 1.8f : 1.5f;
        Vector3 point = PickSummonPoint(center, a.summonRadius, minDist);

        // 1) smoke below, before anything shows up
        SmokeFX.Telegraph(point, a.smokeLeadTime, a.smokeRadius, a.smokeColor, a.smokePrefab);
        yield return new WaitForSeconds(a.smokeLeadTime);

        pendingSummons--;
        if (IsDead) yield break;

        // 2) a puff, then it comes out
        SmokeFX.Burst(point, a.smokeRadius, a.smokeColor, 16, a.smokePrefab);

        Vector3 look = target != null ? FlatDir(target.position - point) : transform.forward;
        GameObject thing = Instantiate(a.summonPrefab, point, Quaternion.LookRotation(look));
        SummonRise.Begin(thing, a.riseTime);
        summoned.Add(thing);
    }

    Vector3 PickSummonPoint(Vector3 center, float radius, float minDist)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector2 c = Random.insideUnitCircle.normalized * Random.Range(minDist, Mathf.Max(minDist, radius));
            Vector3 p = center + new Vector3(c.x, 0f, c.y);
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas)) return hit.position;
        }
        return center;
    }

    void KillSummons()
    {
        foreach (var go in summoned)
        {
            if (go == null) continue;
            var e = go.GetComponent<EnemyAI>();
            if (e != null) e.Kill();
            else Destroy(go);
        }
        summoned.Clear();
    }

    // ───────────────────────── CONFIDENCE ─────────────────────────

    void HandleWeaponResult(HitResult r)
    {
        switch (r)
        {
            case HitResult.Hit:     AddConfidence(hitGain); landedThisAttack = true; break;   // "I hit them!"
            case HitResult.Blocked: AddConfidence(blockedGain); break;
            case HitResult.Dodged:  AddConfidence(-whiffPenalty); break;                       // swung at air
            // Parried is handled in OnParried
        }
    }

    float ConfidenceFloor => isBoss ? bossConfidenceFloor : 0f;

    void AddConfidence(float amount)
    {
        confidence += amount;
        ClampConfidence();
    }

    void ClampConfidence() => confidence = Mathf.Clamp(confidence, ConfidenceFloor, 1f);

    // ───────────────────────── STUN / DAMAGE ─────────────────────────

    void UpdateStun()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f) ReturnToCombat();
    }

    void EnterStun(State state, float duration, string animName)
    {
        SetHitboxes(false);
        sequence = null;
        currentAttack = null;
        CurrentState = state;
        stateTimer = duration;
        Halt();
        agent.updateRotation = false;
        PlayAnim(animName, hitBlend, true);
    }

    void ReturnToCombat()
    {
        CurrentState = State.Combat;
        agent.isStopped = false;
        wantsToAttack = false;
        decisionTimer = 0f;       // re-think right away (a shaken enemy will back off or circle)
    }

    // Called by the player's parry
    public void OnParried()
    {
        if (IsDead || CurrentState == State.PhaseChange) return;

        AddConfidence(-parriedPenalty);
        nextAttackTime = Time.time + 0.5f;
        EnterStun(State.Staggered, parriedStagger * (isBoss ? bossStaggerMult : 1f), AnimId.Stagger);
    }

    public void TakeDamage(DamageInfo info)
    {
        if (IsDead || CurrentState == State.PhaseChange) return;

        info.onResult?.Invoke(HitResult.Hit);

        float dmg = info.amount;
        if (CurrentState == State.Staggered) dmg *= staggeredDamageMultiplier;
        health -= dmg;
        lastDamageTime = Time.time;

        if (health <= 0f) { Die(); return; }

        AddConfidence(-hurtPenalty);
        if (CurrentState == State.Idle) EnterCombat();   // getting hit wakes it up

        // Boss phase change
        if (isBoss)
        {
            int newPhase = ComputePhase();
            if (newPhase > phase) { StartPhaseChange(newPhase); return; }
        }

        if (CurrentState == State.Staggered) return;

        bool armored = CurrentState == State.Attack && (!hitInterruptsAttack || (isBoss && bossHyperArmor));
        if (armored) return;

        EnterStun(State.Hit, hitStun * (isBoss ? bossStaggerMult : 1f), AnimId.Hit);
    }

    // ───────────────────────── BOSS ─────────────────────────

    int ComputePhase()
    {
        float pct = health / maxHealth;
        int p = 0;
        for (int i = 0; i < bossPhases.Count; i++)
            if (pct <= bossPhases[i].startsAtHealth) p = i + 1;
        return p;
    }

    void StartPhaseChange(int newPhase)
    {
        phase = newPhase;
        AddConfidence(bossPhases[phase - 1].confidenceBoost);
        EnterStun(State.PhaseChange, phaseChangeDuration, AnimId.BossPhase);   // invulnerable during the transition
    }

    BossPhase CurrentPhaseData => (phase > 0 && phase <= bossPhases.Count) ? bossPhases[phase - 1] : null;
    float SpeedMult    => CurrentPhaseData != null ? CurrentPhaseData.speedMult : 1f;
    float DamageMult   => CurrentPhaseData != null ? CurrentPhaseData.damageMult : 1f;
    float CooldownMult => CurrentPhaseData != null ? CurrentPhaseData.cooldownMult : 1f;

    // ───────────────────────── DEATH ─────────────────────────

    void Die()
    {
        SetHitboxes(false);
        CurrentState = State.Dead;
        agent.isStopped = true;
        agent.enabled = false;
        PlayAnim(AnimId.Die, hitBlend, true);

        if (killMinionsOnDeath) KillSummons();

        if (isBoss && showVictoryBanner) VictoryBanner.Show(this);   // "ENEMY FELLED"

        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        Destroy(gameObject, 5f);
    }

    // ───────────────────────── ROOT MOTION ─────────────────────────

    // Called by Unity (through RootMotionRelay) every frame the Animator moves
    void OnRootMotion()
    {
        if (!useRootMotion || IsDead || animator == null || !agent.enabled || !agent.isOnNavMesh) return;

        bool apply;
        if (CurrentState == State.Attack) apply = currentAttack != null && currentAttack.useRootMotion;
        else if (CurrentState == State.Staggered || CurrentState == State.Hit || CurrentState == State.PhaseChange) apply = rootMotionOnHit;
        else apply = false;                       // walking / strafing is driven by the NavMeshAgent
        if (!apply) return;

        Vector3 delta = animator.deltaPosition * rootMotionMultiplier;
        delta.y = 0f;
        if (delta.sqrMagnitude > 0f) agent.Move(delta);   // the agent keeps it on the NavMesh

        if (rootMotionRotation) transform.rotation = animator.deltaRotation * transform.rotation;
    }

    // ───────────────────────── HELPERS ─────────────────────────

    // Plays an Animator state by name with a cross-fade. restart = true lets it blend into itself.
    void PlayAnim(string animName, float blend = -1f, bool restart = false)
    {
        if (animator == null) return;
        if (!restart && currentAnim == animName) return;
        currentAnim = animName;
        AnimPlay.CrossFade(animator, animName, blend < 0f ? locomotionBlend : blend);
    }

    float FlatDistance(Vector3 p)
    {
        Vector3 d = p - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    Vector3 FlatDir(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : transform.forward;
    }

    void FaceTarget()
    {
        Vector3 dir = target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
    }

    // ───────────────────────── UI / DEBUG ─────────────────────────

    void OnGUI()
    {
        if (isBoss && showBossBar && bossBarAlpha > 0.01f) DrawBossBar();
        if (!isBoss && headBarAlpha > 0.01f) DrawHeadBar();
        if (showConfidenceDebug && !IsDead) DrawDebug();
    }

    void UpdateBossBarValues()
    {
        float frac = Mathf.Clamp01(health / maxHealth);
        bool show = engaged && CurrentState != State.Dead;

        bossBarAlpha = Mathf.MoveTowards(bossBarAlpha, (isBoss && show) ? 1f : 0f, Time.unscaledDeltaTime * 3f);

        bool showHead = !isBoss && showHeadBar && CurrentState != State.Dead
                        && (headBarAlwaysVisible || engaged || Time.time - lastDamageTime < headBarHideDelay);
        headBarAlpha = Mathf.MoveTowards(headBarAlpha, showHead ? 1f : 0f, Time.unscaledDeltaTime * 4f);

        if (frac >= trailFrac) trailFrac = frac;                                   // healing / start
        else if (Time.time - lastDamageTime > 0.45f)                               // pause, then drain
            trailFrac = Mathf.MoveTowards(trailFrac, frac, 0.6f * Time.deltaTime);
    }

    // Draws a sprite into a GUI rect. fraction < 1 crops it from the right (for a shrinking health bar).
    static void DrawSprite(Sprite s, Rect r, float fraction, Color tint)
    {
        if (s == null || s.texture == null || fraction <= 0f) return;

        Texture2D tex = s.texture;
        Rect tr = s.textureRect;
        Rect uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);

        if (fraction < 1f)
        {
            uv.width *= fraction;
            r.width *= fraction;
        }

        GUI.color = tint;
        GUI.DrawTextureWithTexCoords(r, tex, uv);
    }

    static void DrawFlat(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
    }

    void DrawBossBar()
    {
        if (Event.current.type != EventType.Repaint) return;

        float a = bossBarAlpha;
        float frac = Mathf.Clamp01(health / maxHealth);

        float w = Mathf.Min(bossBarSize.x, Screen.width * 0.8f);
        float h = bossBarSize.y;
        float x = (Screen.width - w) * 0.5f;
        float y = bossBarAnchor == BarAnchor.Top ? bossBarMargin : Screen.height - bossBarMargin - h;
        Rect bar = new Rect(x, y, w, h);

        Color prev = GUI.color;
        Color spriteTint = bossBarTint; spriteTint.a *= a;
        Color Fade(Color c) { c.a *= a; return c; }

        // 1) background
        if (bossBarBackground != null) DrawSprite(bossBarBackground, bar, 1f, spriteTint);
        else
        {
            DrawFlat(new Rect(x - 3f, y - 3f, w + 6f, h + 6f), Fade(new Color(0f, 0f, 0f, 0.75f)));
            DrawFlat(bar, Fade(bossBackColor));
        }

        // 2) trail: the pale part that drains after damage
        if (showBossTrail && trailFrac > frac)
        {
            if (bossBarTrail != null) DrawSprite(bossBarTrail, bar, trailFrac, spriteTint);
            else if (bossBarFill != null) DrawSprite(bossBarFill, bar, trailFrac, new Color(1f, 1f, 1f, 0.45f * a));
            else DrawFlat(new Rect(x, y, w * trailFrac, h), Fade(bossTrailColor));
        }

        // 3) health
        if (bossBarFill != null) DrawSprite(bossBarFill, bar, frac, spriteTint);
        else DrawFlat(new Rect(x, y, w * frac, h), Fade(bossFillColor));

        // 4) frame
        if (bossBarFrame != null)
        {
            Rect fr = new Rect(x - bossBarFramePadding.x, y - bossBarFramePadding.y,
                               w + bossBarFramePadding.x * 2f, h + bossBarFramePadding.y * 2f);
            DrawSprite(bossBarFrame, fr, 1f, spriteTint);
        }

        // 5) name (with optional nameplate behind it)
        float nameY = y - bossBarFramePadding.y - 34f;
        if (bossBarNameplate != null)
        {
            Rect np = new Rect((Screen.width - bossNameplateSize.x) * 0.5f, nameY - (bossNameplateSize.y - 30f) * 0.5f,
                               bossNameplateSize.x, bossNameplateSize.y);
            DrawSprite(bossBarNameplate, np, 1f, spriteTint);
        }

        if (bossLabelStyle == null)
            bossLabelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
        bossLabelStyle.fontSize = bossNameSize;
        if (bossNameFont != null) bossLabelStyle.font = bossNameFont;
        bossLabelStyle.normal.textColor = Fade(bossNameColor);

        GUI.color = Color.white;   // the text colour is already faded above
        GUI.Label(new Rect(x, nameY, w, 30f), bossName, bossLabelStyle);

        GUI.color = prev;
    }

    // A small health bar floating over a normal enemy's head
    void DrawHeadBar()
    {
        if (Event.current.type != EventType.Repaint) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 world = headBarAnchor != null ? headBarAnchor.position + Vector3.up * 0.3f
                                              : transform.position + Vector3.up * headBarHeight;
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f || sp.z > headBarMaxDistance) return;         // behind the camera or too far

        float fade = Mathf.Clamp01((headBarMaxDistance - sp.z) / (headBarMaxDistance * 0.3f));
        float a = headBarAlpha * fade;
        float scale = Mathf.Clamp(10f / Mathf.Max(sp.z, 1f), 0.65f, 1.15f);   // a little smaller when far away

        float w = headBarSize.x * scale;
        float h = headBarSize.y * scale;
        float x = sp.x - w * 0.5f;
        float y = Screen.height - sp.y - h * 0.5f;
        Rect bar = new Rect(x, y, w, h);

        float frac = Mathf.Clamp01(health / maxHealth);

        Color prev = GUI.color;
        Color spriteTint = headBarTint; spriteTint.a *= a;
        Color Fade(Color c) { c.a *= a; return c; }

        // background
        if (headBarBackground != null) DrawSprite(headBarBackground, bar, 1f, spriteTint);
        else DrawFlat(new Rect(x - 1f, y - 1f, w + 2f, h + 2f), Fade(headBarBackColor));

        // the pale part that drains after a hit
        if (trailFrac > frac)
        {
            if (headBarFill != null) DrawSprite(headBarFill, bar, trailFrac, new Color(1f, 1f, 1f, 0.45f * a));
            else DrawFlat(new Rect(x, y, w * trailFrac, h), Fade(headBarTrailColor));
        }

        // health
        if (headBarFill != null) DrawSprite(headBarFill, bar, frac, spriteTint);
        else DrawFlat(new Rect(x, y, w * frac, h), Fade(headBarFillColor));

        // frame
        if (headBarFrame != null)
        {
            Rect fr = new Rect(x - headBarFramePadding.x, y - headBarFramePadding.y,
                               w + headBarFramePadding.x * 2f, h + headBarFramePadding.y * 2f);
            DrawSprite(headBarFrame, fr, 1f, spriteTint);
        }

        // name
        if (!string.IsNullOrEmpty(headBarName))
        {
            if (headNameStyle == null)
                headNameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false };
            headNameStyle.normal.textColor = Fade(Color.white);
            GUI.color = Color.white;
            GUI.Label(new Rect(x - 40f, y - 24f * scale, w + 80f, 22f * scale), headBarName, headNameStyle);
        }

        GUI.color = prev;
    }

    void DrawDebug()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.4f);
        if (sp.z <= 0f) return;

        if (debugStyle == null)
        {
            debugStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, fontStyle = FontStyle.Bold };
            debugStyle.normal.textColor = Color.yellow;
        }
        string txt = "conf " + confidence.ToString("0.00") + "  " + CurrentState + (CurrentState == State.Combat ? "/" + mode : "");
        GUI.Label(new Rect(sp.x - 120f, Screen.height - sp.y - 12f, 240f, 24f), txt, debugStyle);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, detectRange);
        Gizmos.color = Color.red;
        foreach (var a in attacks) if (a != null) Gizmos.DrawWireSphere(transform.position, a.maxRange);

        if (enemyType != EnemyType.Melee)
        {
            Gizmos.color = Color.cyan;   Gizmos.DrawWireSphere(transform.position, keepAwayDistance);          // backs away inside this
            Gizmos.color = Color.green;  Gizmos.DrawWireSphere(transform.position, rangedPreferredDistance);   // likes to stand here
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  VICTORY BANNER ("ENEMY FELLED")
//  Lives on its own object so it survives the boss being destroyed.
// ─────────────────────────────────────────────────────────────────────────────

public class VictoryBanner : MonoBehaviour
{
    string text;
    Font font;
    int fontSize;
    Color color;
    Sprite sprite;
    Vector2 spriteSize;
    float delay, fadeIn, hold, fadeOut, bandHeight, bandOpacity, zoom;

    float startTime;
    Texture2D gradient;
    GUIStyle style, shadowStyle;

    // Copies everything it needs, so it survives the boss being destroyed
    public static void Show(EnemyAI e)
    {
        var go = new GameObject("VictoryBanner");
        var b = go.AddComponent<VictoryBanner>();
        b.text = e.victoryText;
        b.font = e.victoryFont;
        b.fontSize = e.victoryFontSize;
        b.color = e.victoryColor;
        b.sprite = e.victorySprite;
        b.spriteSize = e.victorySpriteSize;
        b.delay = e.victoryDelay;
        b.fadeIn = Mathf.Max(0.01f, e.victoryFadeIn);
        b.hold = e.victoryHold;
        b.fadeOut = Mathf.Max(0.01f, e.victoryFadeOut);
        b.bandHeight = e.victoryBandHeight;
        b.bandOpacity = e.victoryBandOpacity;
        b.zoom = e.victorySlowZoom;
        b.startTime = Time.unscaledTime;

        if (e.victorySound != null)
        {
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;   // 2D, plays at full volume regardless of position
            src.clip = e.victorySound;
            src.PlayDelayed(e.victoryDelay);
        }

        Destroy(go, e.victoryDelay + b.fadeIn + e.victoryHold + b.fadeOut + 0.5f);
    }

    void Awake()
    {
        // Vertical gradient: transparent edges, dark middle
        gradient = new Texture2D(1, 64, TextureFormat.RGBA32, false);
        gradient.wrapMode = TextureWrapMode.Clamp;
        for (int i = 0; i < 64; i++)
        {
            float t = i / 63f;
            float a = Mathf.Sin(t * Mathf.PI);        // 0 → 1 → 0
            gradient.SetPixel(0, i, new Color(0f, 0f, 0f, a));
        }
        gradient.Apply();
    }

    void OnDestroy() { if (gradient != null) Destroy(gradient); }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;

        float t = Time.unscaledTime - startTime - delay;
        if (t < 0f) return;

        float total = fadeIn + hold + fadeOut;
        float alpha = t < fadeIn ? t / fadeIn
                    : t < fadeIn + hold ? 1f
                    : 1f - (t - fadeIn - hold) / fadeOut;
        alpha = Mathf.Clamp01(alpha);
        if (alpha <= 0.001f) return;

        float scale = Mathf.Lerp(1f, zoom, Mathf.Clamp01(t / total));
        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;

        Color prev = GUI.color;
        Matrix4x4 prevMatrix = GUI.matrix;

        // dark band across the whole screen
        GUI.color = new Color(1f, 1f, 1f, alpha * bandOpacity);
        GUI.DrawTexture(new Rect(0f, cy - bandHeight * 0.5f, Screen.width, bandHeight), gradient);

        // slow zoom around the centre of the screen
        GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), new Vector2(cx, cy));

        if (sprite != null && sprite.texture != null)
        {
            Rect tr = sprite.textureRect;
            Texture2D tex = sprite.texture;
            Rect uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(new Rect(cx - spriteSize.x * 0.5f, cy - spriteSize.y * 0.5f, spriteSize.x, spriteSize.y), tex, uv);
        }
        else
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
                shadowStyle = new GUIStyle(style);
            }
            if (font != null) { style.font = font; shadowStyle.font = font; }
            style.fontSize = shadowStyle.fontSize = fontSize;

            Color c = color; c.a *= alpha;
            style.normal.textColor = c;
            shadowStyle.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.9f);

            Rect r = new Rect(0f, cy - fontSize, Screen.width, fontSize * 2f);
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 3f, r.y + 3f, r.width, r.height), text, shadowStyle);
            GUI.Label(r, text, style);
        }

        GUI.matrix = prevMatrix;
        GUI.color = prev;
    }
}
