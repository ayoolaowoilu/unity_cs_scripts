using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An arrow shot by the player (BowController creates and launches it). Flies in an arc,
/// damages whatever it hits that can take damage, then sticks in place for a while.
/// Works with no setup: with no Arrow Prefab a simple arrow is built automatically.
/// </summary>
public class Arrow : MonoBehaviour
{
    Transform owner;
    Vector3 velocity;
    float damage = 10f;
    float gravity = 7f;
    float life = 8f;
    float stickTime = 8f;
    float radius = 0.06f;
    bool stuck;
    float stuckTimer;

    readonly HashSet<IDamageable> alreadyHit = new HashSet<IDamageable>();

    // ───────────────────────── Creating ─────────────────────────

    // A plain arrow: a wooden shaft with a metal tip
    public static Arrow CreateDefault(Vector3 position, Vector3 direction)
    {
        var root = new GameObject("Arrow (auto)");
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));

        Shader shader = Shader.Find("Sprites/Default");     // unlit, works in every render pipeline

        // Shaft (a cylinder lying along the forward direction)
        var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(shaft.GetComponent<Collider>());
        shaft.transform.SetParent(root.transform, false);
        shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        shaft.transform.localScale = new Vector3(0.03f, 0.35f, 0.03f);
        shaft.transform.localPosition = new Vector3(0f, 0f, -0.25f);
        Paint(shaft, shader, new Color(0.58f, 0.42f, 0.26f));

        // Tip
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(head.GetComponent<Collider>());
        head.transform.SetParent(root.transform, false);
        head.transform.localScale = new Vector3(0.07f, 0.07f, 0.17f);
        head.transform.localPosition = new Vector3(0f, 0f, 0.14f);
        Paint(head, shader, new Color(0.78f, 0.78f, 0.84f));

        return root.AddComponent<Arrow>();
    }

    static void Paint(GameObject go, Shader shader, Color color)
    {
        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (shader != null) r.sharedMaterial = new Material(shader) { color = color };
    }

    public void Launch(Transform shooter, Vector3 startVelocity, float arrowDamage, float arrowGravity, float lifetime, float stayStuckFor)
    {
        owner = shooter;
        velocity = startVelocity;
        damage = arrowDamage;
        gravity = arrowGravity;
        life = lifetime;
        stickTime = stayStuckFor;
        if (velocity.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(velocity);
    }

    // ───────────────────────── Flying ─────────────────────────

    void Update()
    {
        if (stuck)
        {
            stuckTimer -= Time.deltaTime;
            if (stuckTimer <= 0f) Destroy(gameObject);
            return;
        }

        life -= Time.deltaTime;
        if (life <= 0f) { Destroy(gameObject); return; }

        velocity += Vector3.down * gravity * Time.deltaTime;

        float step = velocity.magnitude * Time.deltaTime;
        Vector3 dir = velocity.normalized;

        RaycastHit[] hits = Physics.SphereCastAll(transform.position, radius, dir, step, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length > 1) System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var h in hits)
            if (HandleHit(h, dir)) return;

        transform.position += velocity * Time.deltaTime;
        transform.rotation = Quaternion.LookRotation(velocity);   // nose follows the arc
    }

    bool HandleHit(RaycastHit h, Vector3 dir)
    {
        Collider c = h.collider;
        if (c == null) return false;
        if (owner != null && c.transform.IsChildOf(owner)) return false;     // never hits the shooter
        if (c.GetComponentInParent<Arrow>() != null) return false;

        Vector3 point = h.distance <= 0.0001f ? transform.position : h.point;

        IDamageable victim = c.GetComponentInParent<IDamageable>();
        if (victim != null)
        {
            if (alreadyHit.Contains(victim)) return false;
            alreadyHit.Add(victim);
            victim.TakeDamage(new DamageInfo { amount = damage, source = owner });
        }

        Stick(point, dir, victim != null ? c.transform : null);
        return true;
    }

    // Stops and stays stuck in whatever it hit
    void Stick(Vector3 point, Vector3 dir, Transform followThis)
    {
        stuck = true;
        stuckTimer = stickTime;
        transform.position = point - dir * 0.12f;                             // slightly buried
        transform.rotation = Quaternion.LookRotation(dir);

        if (followThis != null) transform.SetParent(followThis, true);         // moves with an enemy

        SmokeFX.Burst(point, 0.2f, new Color(0.7f, 0.65f, 0.58f, 0.5f), 4, null);
    }
}
