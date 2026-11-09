using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Animator STATE names. Each string must match the state's name in your Animator Controller exactly
/// (case-sensitive). To use a different name in your Animator, change the string here and nowhere else.
/// The scripts play these with a cross-fade, so you do not need any transitions or int parameter.
/// </summary>
public static class AnimId
{
    // Basics
    public const string Idle        = "Idle";
    public const string Walk        = "Walk";         // blend tree on the "Speed" float
    public const string Run         = "Run";
    public const string Guard       = "Guard";
    public const string GuardWalk   = "GuardWalk";    // blend tree on "MoveX" / "MoveY"
    public const string Attack      = "Attack";
    public const string Parry       = "Parry";
    public const string Hit         = "Hit";          // taking an unguarded hit
    public const string HitReaction = "HitReaction";  // blocked hit (guard)
    public const string Stagger     = "Stagger";      // enemy got parried
    public const string Die         = "Die";

    // Roll / back jump / falling
    public const string Roll        = "Roll";
    public const string BackJump    = "BackJump";     // roll key while standing still
    public const string Fall        = "Fall";         // falling off a ledge

    // Lock-on locomotion (forward uses Walk / Run)
    public const string StrafeLeft  = "StrafeLeft";
    public const string StrafeRight = "StrafeRight";
    public const string WalkBack    = "WalkBack";
    public const string RunLeft     = "RunLeft";
    public const string RunRight    = "RunRight";
    public const string RunBack     = "WalkBack";

    // Enemy attack animations: type these names into each attack in the EnemyAI inspector
    public const string Attack1     = "Attack1";
    public const string Attack2     = "Attack2";
    public const string Attack3     = "Attack3";
    public const string Attack4     = "Attack4";
    public const string Attack5     = "Attack5";
    public const string Attack6     = "Attack6";
    public const string Attack7     = "Attack7";
    public const string Attack8     = "Attack8";
    public const string BossPhase   = "BossPhase";    // boss phase-change roar / transition

    // Ranged / summoner enemies (these are just suggestions: type any state name into an attack's Anim Name)
    public const string Shoot       = "Shoot";        // firing a projectile
    public const string Summon      = "Summon";       // calling things up out of the ground

    // Bow
    public const string EquipBow    = "EquipBow";     // taking out the bow (sword + shield go on the back)
    public const string EquipSword  = "EquipSword";   // taking out the sword + shield (bow goes on the back)
    public const string AimIdle     = "AimIdle";      // bow raised, standing still
    public const string AimWalk     = "AimWalk";      // bow raised, moving (blend tree on MoveX / MoveY)
    public const string BowDraw     = "BowDraw";      // pulling the string back
    public const string BowShoot    = "BowShoot";     // letting go
}

/// <summary>
/// Plays an Animator state by name with a smooth cross-fade.
/// Fading to the state that is already playing blends it into a fresh copy of itself,
/// so the same animation can restart (e.g. two attacks in a row) without a hard pop.
/// </summary>
public static class AnimPlay
{
    static readonly Dictionary<string, int> hashes = new Dictionary<string, int>();

    public static void CrossFade(Animator animator, string stateName, float blendSeconds, int layer = 0)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;

        if (!hashes.TryGetValue(stateName, out int hash))
        {
            hash = Animator.StringToHash(stateName);
            hashes[stateName] = hash;
        }

        animator.CrossFadeInFixedTime(hash, Mathf.Max(0f, blendSeconds), layer, 0f);
    }
}