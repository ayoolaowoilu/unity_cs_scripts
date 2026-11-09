using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A shot fired by an enemy (EnemyAI creates and launches it). Works with no setup:
/// if an attack has no Projectile Prefab, a glowing orb with a trail is built automatically.
/// To use your own, give your prefab any visuals (a Collider is not needed). This script is added for you.
///
/// It damages whatever it hits that can take damage, so the player's guard, parry and roll all work on it:
///   - guard   = blocked, the shot is used up
///   - roll    = the shot passes straight through you
///   - parry   = the shot is sent back at the enemy that fired it (if Parry Reflects is on)
/// </summary>
public class Projectile : MonoBehaviour, IParryable
{
    EnemyAI owner;
    Transform homingTarget;
    Vector3 dir = Vector3.forward;
    float speed = 12f;
    float damage = 10f;
    float life = 5f;
    float radius = 0.2f;
    float homingStrength = 1.5f;
    bool homing;
    bool reflectable = true;
    bool reflected;
    Color impactColor = new Color(1f, 0.6f, 0.3f, 0.6f);

    readonly HashSet<IDamageable> alreadyHit = new HashSet<IDamageable>();

    // ───────────────────────── Creating ─────────────────────────

    // The built-in orb: a small glowing sphere with a fading trail
    public static Projectile CreateDefault(Vector3 position, float size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Projectile (auto)";
        Destroy(go.GetComponent<Collider>());                 // hits are detected by script instead
        go.transform.position = position;
        go.transform.localScale = Vector3.one * size;

        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        Shader shader = Shader.Find("Sprites/Default");       // unlit + transparent, works in every pipeline
        Material mat = null;
        if (shader != null)
        {
            mat = new Material(shader) { color = color };
            mr.sharedMaterial = mat;
        }

        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.startWidth = size * 0.8f;
        trail.endWidth = 0f;
        trail.startColor = color;
        trail.endColor = new Color(color.r, color.g, color.b, 0f);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (mat != null) trail.sharedMaterial = mat;

        var p = go.AddComponent<Projectile>();
        p.radius = Mathf.Max(0.1f, size * 0.5f);
        p.impactColor = new Color(color.r, color.g, color.b, 0.6f);
        return p;
    }

    public void Launch(EnemyAI shooter, Vector3 direction, float projectileSpeed, float projectileDamage,
                       float lifetime, bool home, float homeStrength, bool canReflect, Transform homeTarget)
    {
        owner = shooter;
        dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        speed = projectileSpeed;
        damage = projectileDamage;
        life = lifetime;
        homing = home;
        homingStrength = homeStrength;
        reflectable = canReflect;
        homingTarget = homeTarget;
        transform.rotation = Quaternion.LookRotation(dir);
    }

    // ───────────────────────── Flying ─────────────────────────

    void Update()
    {
        life -= Time.deltaTime;
        if (life <= 0f) { Explode(transform.position); return; }

        // Homing (also used to send a reflected shot back to its shooter)
        if (homing && homingTarget != null)
        {
            Vector3 to = (homingTarget.position + Vector3.up * 1.1f) - transform.position;
            if (to.sqrMagnitude > 0.0001f)
                dir = Vector3.RotateTowards(dir, to.normalized, homingStrength * Time.deltaTime, 0f).normalized;
        }

        float step = speed * Time.deltaTime;
        RaycastHit[] hits = Physics.SphereCastAll(transform.position, radius, dir, step, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length > 1) System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var h in hits)
            if (HandleHit(h)) return;

        transform.position += dir * step;
        transform.rotation = Quaternion.LookRotation(dir);
    }

    // true = stop for this frame (the shot ended or was reflected)
    bool HandleHit(RaycastHit h)
    {
        Collider c = h.collider;
        if (c == null) return false;

        // Never hits its own shooter (until it has been reflected back at them)
        if (!reflected && owner != null && c.transform.IsChildOf(owner.transform)) return false;
        if (c.GetComponentInParent<Projectile>() != null) return false;

        // Enemies don't shoot each other
        EnemyAI enemy = c.GetComponentInParent<EnemyAI>();
        if (enemy != null && !reflected) return false;

        Vector3 point = h.distance <= 0.0001f ? transform.position : h.point;

        IDamageable victim = c.GetComponentInParent<IDamageable>();
        if (victim != null)
        {
            if (alreadyHit.Contains(victim)) return false;
            alreadyHit.Add(victim);

            bool wasReflected = reflected;
            bool passedThrough = false;

            victim.TakeDamage(new DamageInfo
            {
                amount = damage,
                source = transform,
                onResult = r =>
                {
                    if (r == HitResult.Dodged) passedThrough = true;                 // rolled through it
                    if (!reflected && owner != null) owner.ReportHitResult(r);       // confidence for the shooter
                }
            });

            if (!wasReflected && reflected) return true;   // just got parried: keep flying, now toward the shooter
            if (passedThrough) return false;               // keep going
        }

        // Hit the player, or a wall / the floor
        Explode(point);
        return true;
    }

    // ───────────────────────── Parry ─────────────────────────

    // Called by the player's parry
    public void OnParried()
    {
        if (!reflectable || reflected) return;

        reflected = true;
        alreadyHit.Clear();

        if (owner != null)
        {
            homingTarget = owner.transform;
            dir = (owner.LockPosition - transform.position).normalized;
            homing = true;
            homingStrength = 6f;
        }
        else dir = -dir;

        speed *= 1.4f;
        damage *= 2f;
        life = Mathf.Max(life, 4f);
    }

    // ───────────────────────── Ending ─────────────────────────

    void Explode(Vector3 point)
    {
        SmokeFX.Burst(point, 0.4f, impactColor, 6, null);
        Destroy(gameObject);
    }
}
