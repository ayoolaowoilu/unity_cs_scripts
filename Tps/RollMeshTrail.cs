using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put this on the Player. While the player rolls (and optionally back-jumps) it leaves fading
/// "ghost" copies of the character's mesh behind, like an afterimage trail.
/// No setup needed: it finds the player's meshes by itself.
/// </summary>
public class RollMeshTrail : MonoBehaviour
{
    [Header("References (auto-found)")]
    public PlayerController player;
    public SkinnedMeshRenderer[] skinnedMeshes;      // the character body
    public MeshFilter[] staticMeshes;                // sword, shield, hat...

    [Header("Look")]
    [Tooltip("Optional. Leave empty to use a built-in flat transparent material.")]
    public Material ghostMaterial;
    public Color ghostColor = new Color(0.35f, 0.8f, 1f, 0.55f);
    public float fadeTime = 0.35f;                   // how long each ghost lasts
    public bool includeStaticMeshes = true;          // also copy sword / shield

    public enum SkinnedScaleMode { Auto, UseTransformScale, NoScale }
    [Tooltip("Auto works out the right size by itself. If the ghosts are still too big, try NoScale. Too small, try UseTransformScale.")]
    public SkinnedScaleMode skinnedScale = SkinnedScaleMode.Auto;
    [Tooltip("Fine-tune the ghost size. 1 = same size as the character, 0.95 = slightly slimmer.")]
    public float ghostSizeMultiplier = 1f;

    [Header("Timing")]
    public float spawnInterval = 0.04f;              // time between ghosts (smaller = denser trail)
    public bool trailOnBackJump = true;

    [Header("Attack Trail")]
    public bool trailOnAttack = true;
    [Tooltip("Which hits of the combo leave a trail (1 = first hit, 3 = third hit)")]
    public int[] attackTrailHits = { 3 };
    [Tooltip("On = only while the sword is swinging (hitbox on). Off = the whole attack animation.")]
    public bool onlyWhileSwinging = false;
    public float attackSpawnInterval = 0.03f;        // smaller = smoother trail on a fast swing
    public bool useAttackColor = true;
    public Color attackGhostColor = new Color(1f, 0.55f, 0.2f, 0.55f);
    public int maxGhosts = 80;                       // safety cap

    class Ghost
    {
        public GameObject go;
        public Mesh ownedMesh;       // baked meshes are destroyed with the ghost
        public Material mat;
        public Color color;
        public float born;
    }

    readonly List<Ghost> ghosts = new List<Ghost>();
    readonly Dictionary<SkinnedMeshRenderer, Vector3> scaleCache = new Dictionary<SkinnedMeshRenderer, Vector3>();
    Material fallbackMaterial;
    float spawnTimer;

    void Awake()
    {
        if (player == null) player = GetComponent<PlayerController>();
        if (player == null) player = GetComponentInParent<PlayerController>();

        if (skinnedMeshes == null || skinnedMeshes.Length == 0)
            skinnedMeshes = GetComponentsInChildren<SkinnedMeshRenderer>();
        if (staticMeshes == null || staticMeshes.Length == 0)
            staticMeshes = GetComponentsInChildren<MeshFilter>();
    }

    void Update()
    {
        if (player != null)
        {
            PlayerController.State state = player.CurrentState;

            bool dodging = state == PlayerController.State.Roll
                           && (trailOnBackJump || !player.IsBackJumping);

            bool attacking = trailOnAttack && state == PlayerController.State.Attack
                             && IsTrailHit(player.CurrentAttackIndex + 1)
                             && (!onlyWhileSwinging || player.SwordActive);

            if (dodging || attacking)
            {
                spawnTimer -= Time.deltaTime;
                if (spawnTimer <= 0f)
                {
                    Color c = (attacking && useAttackColor) ? attackGhostColor : ghostColor;
                    SpawnSnapshot(c);
                    spawnTimer = attacking ? attackSpawnInterval : spawnInterval;
                }
            }
            else spawnTimer = 0f;   // the first ghost appears immediately next time
        }

        FadeGhosts();
    }

    // ───────────────────────── Spawning ─────────────────────────

    bool IsTrailHit(int hitNumber)
    {
        if (attackTrailHits == null) return false;
        for (int i = 0; i < attackTrailHits.Length; i++)
            if (attackTrailHits[i] == hitNumber) return true;
        return false;
    }

    void SpawnSnapshot(Color color)
    {
        Material baseMat = GetBaseMaterial();
        if (baseMat == null) return;

        // Body
        foreach (var smr in skinnedMeshes)
        {
            if (smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy) continue;

            Mesh baked = new Mesh();
            smr.BakeMesh(baked);   // frozen copy of the current pose
            baked.RecalculateBounds();
            CreateGhostPart(baked, true, smr.transform, baseMat, GetBakedScale(smr, baked), color);
        }

        // Sword / shield and other static meshes
        if (includeStaticMeshes)
        {
            foreach (var mf in staticMeshes)
            {
                if (mf == null || mf.sharedMesh == null || !mf.gameObject.activeInHierarchy) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;

                CreateGhostPart(mf.sharedMesh, false, mf.transform, baseMat, mf.transform.lossyScale, color);
            }
        }

        // Keep the total in check: drop the oldest ghosts first
        while (ghosts.Count > maxGhosts) DestroyGhost(0);
    }

    void CreateGhostPart(Mesh mesh, bool ownsMesh, Transform source, Material baseMat, Vector3 scale, Color color)
    {
        var go = new GameObject("RollGhost");
        go.transform.SetPositionAndRotation(source.position, source.rotation);
        go.transform.localScale = scale * ghostSizeMultiplier;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var mat = new Material(baseMat);
        SetColor(mat, color);

        // One material slot per submesh, otherwise only the first submesh would draw
        var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
        mr.sharedMaterials = mats;

        ghosts.Add(new Ghost { go = go, ownedMesh = ownsMesh ? mesh : null, mat = mat, color = color, born = Time.time });
    }

    // Baked meshes don't always come out at the same scale as the character (it depends on how the model was
    // imported). Auto mode checks both options and keeps the one whose size matches the real, visible character.
    Vector3 GetBakedScale(SkinnedMeshRenderer smr, Mesh baked)
    {
        Vector3 lossy = smr.transform.lossyScale;

        if (skinnedScale == SkinnedScaleMode.UseTransformScale) return lossy;
        if (skinnedScale == SkinnedScaleMode.NoScale) return Vector3.one;

        if (scaleCache.TryGetValue(smr, out Vector3 cached)) return cached;

        Vector3 result = lossy;
        float bakedSize = baked.bounds.size.magnitude;
        float realSize = smr.bounds.size.magnitude;   // world-space size of the character as it appears

        if (bakedSize > 0.0001f && realSize > 0.0001f)
        {
            float avgScale = (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f;

            // Which option puts the ghost closest to the real size?
            float errNoScale = Mathf.Abs(Mathf.Log(bakedSize / realSize));
            float errScaled  = Mathf.Abs(Mathf.Log(bakedSize * Mathf.Max(avgScale, 0.0001f) / realSize));
            result = errNoScale <= errScaled ? Vector3.one : lossy;
        }

        scaleCache[smr] = result;
        return result;
    }

    // ───────────────────────── Fading ─────────────────────────

    void FadeGhosts()
    {
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            Ghost g = ghosts[i];
            float t = (Time.time - g.born) / Mathf.Max(0.01f, fadeTime);

            if (t >= 1f)
            {
                DestroyGhost(i);
                continue;
            }

            Color c = g.color;
            c.a = g.color.a * (1f - t);
            SetColor(g.mat, c);
        }
    }

    void DestroyGhost(int index)
    {
        Ghost g = ghosts[index];
        if (g.go != null) Destroy(g.go);
        if (g.ownedMesh != null) Destroy(g.ownedMesh);
        if (g.mat != null) Destroy(g.mat);
        ghosts.RemoveAt(index);
    }

    void OnDestroy()
    {
        for (int i = ghosts.Count - 1; i >= 0; i--) DestroyGhost(i);
        if (fallbackMaterial != null) Destroy(fallbackMaterial);
    }

    // ───────────────────────── Material helpers ─────────────────────────

    Material GetBaseMaterial()
    {
        if (ghostMaterial != null) return ghostMaterial;

        if (fallbackMaterial == null)
        {
            // Sprites/Default is unlit, transparent and ships with every render pipeline
            Shader s = Shader.Find("Sprites/Default");
            if (s == null)
            {
                Debug.LogWarning("RollMeshTrail: couldn't find a shader. Assign a transparent material to Ghost Material.", this);
                return null;
            }
            fallbackMaterial = new Material(s);
        }
        return fallbackMaterial;
    }

    static void SetColor(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);   // URP / HDRP
        if (m.HasProperty("_Color"))     m.SetColor("_Color", c);       // built-in
    }
}