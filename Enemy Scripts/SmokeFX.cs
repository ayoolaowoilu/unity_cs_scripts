using System.Collections;
using UnityEngine;

/// <summary>
/// Built-in smoke puffs used by summons (and projectile impacts). No setup needed:
/// it creates its own soft smoke particles the first time it is used.
///   Telegraph = smoke that keeps rising on the ground for a while (the warning before something appears)
///   Burst     = one quick puff
/// </summary>
public class SmokeFX : MonoBehaviour
{
    static SmokeFX instance;
    ParticleSystem ps;

    static SmokeFX Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("SmokeFX (auto)");
                instance = go.AddComponent<SmokeFX>();
                instance.Build();
            }
            return instance;
        }
    }

    // ───────────────────────── Public ─────────────────────────

    // Smoke that thickens on the ground for `duration` seconds
    public static void Telegraph(Vector3 position, float duration, float radius, Color color, ParticleSystem prefab)
    {
        if (prefab != null)
        {
            ParticleSystem fx = Instantiate(prefab, position, Quaternion.identity);
            fx.Play();
            Destroy(fx.gameObject, duration + 3f);
            return;
        }
        Instance.StartCoroutine(Instance.TelegraphRoutine(position, duration, radius, color));
    }

    // One quick puff
    public static void Burst(Vector3 position, float radius, Color color, int count, ParticleSystem prefab)
    {
        if (prefab != null)
        {
            ParticleSystem fx = Instantiate(prefab, position, Quaternion.identity);
            fx.Play();
            Destroy(fx.gameObject, 4f);
            return;
        }
        Instance.BurstNow(position, radius, color, count);
    }

    // ───────────────────────── Effects ─────────────────────────

    IEnumerator TelegraphRoutine(Vector3 center, float duration, float radius, Color color)
    {
        float t = 0f;
        float accumulator = 0f;

        while (t < duration)
        {
            float k = duration > 0f ? t / duration : 1f;
            float rate = Mathf.Lerp(10f, 28f, k);              // thicker and thicker as the moment gets closer

            accumulator += rate * Time.deltaTime;
            while (accumulator >= 1f)
            {
                accumulator -= 1f;

                Vector2 disc = Random.insideUnitCircle * radius * 0.8f;
                Vector3 pos = center + new Vector3(disc.x, 0.05f, disc.y);
                float size = radius * Random.Range(0.45f, 0.85f) * Mathf.Lerp(0.8f, 1.25f, k);
                Vector3 vel = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.35f, 0.9f), Random.Range(-0.15f, 0.15f));

                Emit(pos, vel, size, color, Random.Range(0.9f, 1.4f));
            }

            t += Time.deltaTime;
            yield return null;
        }
    }

    void BurstNow(Vector3 center, float radius, Color color, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 disc = Random.insideUnitCircle * radius * 0.5f;
            Vector3 pos = center + new Vector3(disc.x, 0.1f, disc.y);
            Vector3 outward = new Vector3(disc.x, 0f, disc.y).normalized * radius * Random.Range(0.6f, 1.6f);
            Vector3 vel = outward + Vector3.up * Random.Range(0.3f, 0.9f);

            Emit(pos, vel, radius * Random.Range(0.6f, 1.1f), color, Random.Range(0.8f, 1.3f));
        }
    }

    void Emit(Vector3 position, Vector3 velocity, float size, Color color, float lifetime)
    {
        var ep = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = velocity,
            startSize = size,
            startColor = color,
            startLifetime = lifetime
        };
        ps.Emit(ep, 1);
    }

    // ───────────────────────── Particle system ─────────────────────────

    void Build()
    {
        ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 1.2f;
        main.startSpeed = 0f;                                  // set per puff
        main.startSize = 1f;                                   // set per puff
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.02f;                         // drifts up a little
        main.maxParticles = 800;

        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;

        // Fade in fast, fade out slowly
        var colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = grad;

        // Puffs swell as they rise
        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.8f));

        // Slow down so it hangs in the air
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.2f;
        limit.limit = 0.8f;

        var r = GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        Shader shader = Shader.Find("Sprites/Default");        // unlit + transparent, works in every pipeline
        if (shader != null) r.sharedMaterial = new Material(shader) { mainTexture = MakeSoftCircle(64) };
        else Debug.LogWarning("SmokeFX: couldn't find a particle shader. Give the attack your own Smoke Prefab.", this);

        ps.Play();
    }

    static Texture2D MakeSoftCircle(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);                     // smoothstep: soft edges
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }
}
