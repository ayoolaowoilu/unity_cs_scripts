using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Added automatically to something an enemy summons. While the smoke is still thick the thing is
/// small, can't be hit and does nothing; it pops up out of the ground, then wakes up and goes for the player.
/// </summary>
public class SummonRise : MonoBehaviour
{
    EnemyAI ai;
    readonly List<Collider> pausedColliders = new List<Collider>();
    Vector3 baseScale = Vector3.one;
    float duration = 0.8f;
    float timer;
    bool running;

    public static SummonRise Begin(GameObject summoned, float riseTime)
    {
        var rise = summoned.AddComponent<SummonRise>();
        rise.Init(riseTime);
        return rise;
    }

    void Init(float riseTime)
    {
        duration = Mathf.Max(0.01f, riseTime);

        // Switch the enemy brain off until it is fully out
        ai = GetComponent<EnemyAI>();
        if (ai != null)
        {
            ai.enabled = false;
            if (!string.IsNullOrEmpty(ai.emergeAnim) && ai.animator != null)
                AnimPlay.CrossFade(ai.animator, ai.emergeAnim, 0.05f);   // your own "coming out of the ground" clip
        }

        // Can't be hit while rising (weapon hitboxes manage themselves, so leave those alone)
        foreach (var c in GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.GetComponent<WeaponHitbox>() != null) continue;
            c.enabled = false;
            pausedColliders.Add(c);
        }

        // Starts small and flat, like it is still under the ground
        baseScale = transform.localScale;
        transform.localScale = new Vector3(baseScale.x * 0.6f, baseScale.y * 0.05f, baseScale.z * 0.6f);
        running = true;
    }

    void Update()
    {
        if (!running) return;

        timer += Time.deltaTime;
        float k = Mathf.Clamp01(timer / duration);
        float ease = 1f - (1f - k) * (1f - k) * (1f - k);        // fast at first, settles at the end

        transform.localScale = new Vector3(
            baseScale.x * Mathf.Lerp(0.6f, 1f, ease),
            baseScale.y * Mathf.Lerp(0.05f, 1f, ease),
            baseScale.z * Mathf.Lerp(0.6f, 1f, ease));

        if (k >= 1f) Finish();
    }

    void Finish()
    {
        running = false;
        transform.localScale = baseScale;

        bool dead = ai != null && ai.IsDead;
        if (!dead)
            foreach (var c in pausedColliders)
                if (c != null) c.enabled = true;

        if (ai != null && !dead)
        {
            ai.enabled = true;
            ai.Aggro();                                           // straight into the fight
        }

        Destroy(this);
    }
}
