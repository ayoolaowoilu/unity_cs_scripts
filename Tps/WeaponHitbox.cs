using System.Collections.Generic;
using UnityEngine;

public enum HitResult
{
    Hit,       // damage went through
    Blocked,   // target guarded it
    Parried,   // target parried it
    Dodged     // target was invulnerable (rolling)
}

public struct DamageInfo
{
    public float amount;
    public Transform source;                       // who is attacking (used for guard/parry direction checks)
    public System.Action<HitResult> onResult;      // the target calls this to report what happened
}

public interface IDamageable
{
    void TakeDamage(DamageInfo info);
}

public interface IParryable
{
    // Called on the attacker when the defender parries successfully.
    void OnParried();
}

/// <summary>
/// Put this on the weapon's collider object (sword blade, etc.).
/// The collider is kept disabled until Enable() is called (attack active frames).
/// Each target is only hit once per swing.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WeaponHitbox : MonoBehaviour
{
    [Header("Setup")]
    public Transform owner;                 // the character holding this weapon
    public LayerMask targetLayers = ~0;     // layers this weapon can damage
    public float damage = 10f;

    [Header("Debug")]
    public bool debugLog = false;                   // tick this to see in the Console why a hit does or doesn't land
    public bool showGizmo = true;                   // draws the hitbox in the Scene view while it is active

    // Fired when this weapon's hit was resolved by the target (hit / blocked / parried / dodged)
    public event System.Action<HitResult> OnHitResult;

    Collider col;
    readonly HashSet<IDamageable> alreadyHit = new HashSet<IDamageable>();

    void Awake() { Init(); }

    void Init()
    {
        if (col != null) return;
        col = GetComponent<Collider>();
        col.isTrigger = true;
        col.enabled = false;

        // Trigger events need at least one Rigidbody involved.
        if (!TryGetComponent<Rigidbody>(out var rb))
            rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        if (owner == null) owner = transform.root;
    }

    public void Enable()
    {
        Init();
        if (col.enabled) return;     // already active: don't reset the hit list mid-swing
        alreadyHit.Clear();
        col.enabled = true;
        if (debugLog) Debug.Log("[" + name + "] hitbox ON (owner: " + owner.name + ")", this);
    }

    public void Disable()
    {
        Init();
        col.enabled = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!col.enabled) return;

        if (other.transform.IsChildOf(owner))
        {
            return;   // don't hit yourself (not logged: it happens every swing)
        }
        if ((targetLayers.value & (1 << other.gameObject.layer)) == 0)
        {
            if (debugLog) Debug.Log("[" + name + "] touched " + other.name + " but its layer is not in Target Layers", this);
            return;
        }

        var target = other.GetComponentInParent<IDamageable>();
        if (target == null)
        {
            if (debugLog) Debug.Log("[" + name + "] touched " + other.name + " but nothing on it (or its parents) can take damage", this);
            return;
        }
        if (alreadyHit.Contains(target)) return;

        alreadyHit.Add(target);
        if (debugLog) Debug.Log("[" + name + "] HIT " + other.name + " for " + damage, this);

        target.TakeDamage(new DamageInfo
        {
            amount = damage,
            source = owner,
            onResult = r => OnHitResult?.Invoke(r)
        });
    }

    void OnDrawGizmos()
    {
        if (!showGizmo || !Application.isPlaying) return;
        var c = GetComponent<Collider>();
        if (c == null || !c.enabled) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
    }
}