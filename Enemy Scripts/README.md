# EnemyAI

A NavMesh enemy brain that picks its own attacks, gets braver when it hits you and more careful when it gets hurt, and can be a close-range fighter, an archer, a summoner or a boss. Everything is set in the Inspector, and animations are played by **Animator state name**, with no transitions to draw.

**What it does**

- Chases, circles and backs away on a **NavMesh**
- Picks attacks **at random** from a list you make, with optional **combos** (series of attacks)
- A **confidence** level: landing hits makes it bolder, getting parried or hurt makes it cautious
- Three **types**: Melee, Ranged (keeps its distance and shoots) and Summoner (stays back and calls things up out of smoke)
- A **boss mode**: phases, a custom boss health bar, and an "ENEMY FELLED" banner
- A small **health bar over the head** of normal enemies
- Works with the player's guard, parry (including reflecting shots), roll and lock-on

> Read the repo's [main README](../README.md) first for the folder layout and requirements.

---

## Files you need

| File | Goes on | Needed? |
|---|---|---|
| `Enemy/EnemyAI.cs` | every enemy | yes |
| `Combat/WeaponHitbox.cs` | the enemy's weapon colliders | yes (for melee attacks) |
| `Animation/AnimId.cs` and `Animation/RootMotionRelay.cs` | nothing | yes |
| `Effects/SmokeFX.cs` | nothing | yes |
| `Enemy/Projectile.cs` | nothing (made by `EnemyAI`) | yes |
| `Enemy/SummonRise.cs` | nothing (added to summoned things) | yes |
| `Player/PlayerController.cs` | the Player | yes (the enemy reads the player's state) |

---

## Setup

1. **Bake a NavMesh** on your ground. In newer Unity versions add the *AI Navigation* package and a `NavMeshSurface`; in older ones use *Window > AI > Navigation*.
2. Tag your player **Player** (the enemy finds it by this tag, or drag it into **Target**).
3. Make the enemy object: a **model with an Animator**, a **Collider** on the body (a Capsule works, it is what the player's sword and arrows hit), and add **EnemyAI**. A **NavMeshAgent** is added for you.
4. For melee attacks, give the weapon (a hand, a sword) a **Collider**, add **WeaponHitbox** to it and drag it into **Weapons**. Leave **Weapons** empty and the enemy uses any `WeaponHitbox` it finds on itself.
5. In your Animator Controller, create the states from [the table below](#animator-states) in the Base Layer.
6. Press Play and walk into the enemy's `Detect Range` (12 m by default).

If something is wrong, the Console tells you: no target, not on the NavMesh, no weapon hitbox, no Animator.

---

## Enemy types

Pick **Enemy Type** on the script:

| Type | Behaviour |
|---|---|
| **Melee** | walks up to you and fights close, standing nearer as its confidence goes up |
| **Ranged** | stands at `Ranged Preferred Distance` (9 m), backs away if you get closer than `Keep Away Distance` (5 m), and shoots. Give it **Projectile** attacks |
| **Summoner** | the same spacing as Ranged, but uses **Summon** attacks to call things up. Give it one other attack too, so it has something to do between summons |

Select the enemy in the Scene view to see its ranges: yellow is the detect range, red circles are each attack's reach, and for Ranged and Summoner types cyan is where it backs away and green is where it likes to stand.

---

## Attacks

The **Attacks** list holds everything the enemy can do. Each attack has a **Kind**:

- **Melee**: swings a weapon hitbox.
- **Projectile**: shoots something.
- **Summon**: calls things up out of the ground.

### Fields every attack has

| Field | Meaning |
|---|---|
| `Anim Name` | the Animator state to play (`Attack1`, `Shoot`, `Summon` ... any name) |
| `Blend Time` | seconds to blend into it |
| `Damage` | damage dealt (boss phases can multiply it) |
| `Min Range` / `Max Range` | the distance it can be used at. Always keep one attack with `Min Range` 0 |
| `Weight` | higher = picked more often |
| `Min Confidence` | the enemy needs at least this much confidence to use it |
| `Min Phase` | boss only: unlocked from this phase on |
| `Duration` | how long the attack lasts (usually the clip length) |
| `Extra Recovery` | extra pause after this attack before the next decision |
| `Track Target During Windup` | keeps facing you until the swing or shot |

### Melee fields

| Field | Meaning |
|---|---|
| `Hitbox On Time` / `Hitbox Off Time` | when the weapon is dangerous, in seconds from the start of the animation |
| `Hitbox Index` | which `Weapons` entry to switch on (0 = the first, -1 = all of them) |
| `Use Root Motion` | the clip moves the enemy; turn off to use the lunge settings |
| `Lunge Speed`, `Lunge Start`, `Lunge End` | step forward during the attack |

### Projectile fields

| Field | Meaning |
|---|---|
| `Action Time` | the moment it fires, in seconds from the start of the animation |
| `Projectile Prefab` | optional; empty = a built-in glowing orb with a trail |
| `Muzzle` | optional; where it comes out (the enemy's default **Muzzle** is used if empty) |
| `Projectile Count` + `Spread Angle` | fire several in a fan |
| `Projectile Speed`, `Lifetime`, `Size`, `Color` | the shot itself |
| `Predict Target` | leads the shot if you are moving |
| `Homing` + `Homing Strength` | the shot curves toward you |
| `Parry Reflects` | a well-timed parry sends the shot back at the shooter |

The player's **guard** blocks a shot, **rolling** lets it pass through, and a **parry** can send it back for double damage.

### Summon fields

| Field | Meaning |
|---|---|
| `Action Time` | the moment it summons |
| `Summon Prefab` | the thing to summon (usually another enemy prefab with `EnemyAI`) |
| `Summon Count` | how many at once |
| `Max Alive` | it will not summon more while this many of its things are alive |
| `Summon Radius` / `Summon Around Player` | where they appear: around the summoner, or around you |
| `Spawn Stagger` | delay between each one |
| `Smoke Lead Time` | **smoke shows on the ground this long before the thing comes out** |
| `Rise Time` | how long it takes to come up out of the smoke |
| `Smoke Radius`, `Smoke Color`, `Smoke Prefab` | the smoke (empty `Smoke Prefab` = built-in smoke) |

What happens: smoke appears on the ground and thickens, a puff goes off, and the thing pops up out of the ground. While it rises it cannot be hit and does nothing, then it wakes up and attacks. If the summoner dies, its things die too (`Kill Minions On Death`). If your summoned enemy has a "climbing out of the ground" clip, type its state name into that enemy's **Emerge Anim**.

### Combos

The **Combos** list is optional. Each combo is a list of indexes into the Attacks list, played in order: `0, 1, 2` plays attack 0, then 1, then 2. Only the first attack must be in range. After each attack the enemy decides whether to continue, and it is much more likely to continue if the attack landed. Confident enemies prefer long combos, and nervous ones prefer single strikes. Leave the list empty to just pick single attacks.

### Example setups

**Melee grunt**
- Enemy Type `Melee`, `Weapons` = its hand or sword hitbox.
- Attack 0: Kind `Melee`, `Anim Name` `Attack1`, Min Range 0, Max Range 2.2, `Hitbox On` 0.4, `Hitbox Off` 0.7, `Duration` 1.
- Attack 1: Kind `Melee`, `Anim Name` `Attack2`, `Damage` 18, `Weight` 0.5, `Min Confidence` 0.5, `Extra Recovery` 0.4.

**Archer**
- Enemy Type `Ranged`, set the **Muzzle** to a hand or bow tip.
- One attack: Kind `Projectile`, `Anim Name` `Shoot`, Min Range 0, Max Range 18, `Action Time` at the frame the string is released, `Duration` the clip length, `Predict Target` on.

**Summoner**
- Enemy Type `Summoner`.
- Attack 0: Kind `Summon`, `Anim Name` `Summon`, Min Range 0, Max Range 20, `Summon Prefab` = your minion, `Max Alive` 3, `Extra Recovery` 6 (so it summons rarely).
- Attack 1: a normal attack or shot so it can defend itself.

---

## Confidence

Every enemy has a **confidence** between 0 and 1 (watch the `Confidence` value in the Inspector while playing, or tick `Show Confidence Debug`).

| Event | Change | Setting |
|---|---|---|
| It lands a hit on you | up | `Hit Gain` (+0.15) |
| You block its attack | slightly up | `Blocked Gain` (+0.03) |
| You roll through its attack | down | `Whiff Penalty` (-0.06) |
| You parry it | down a lot | `Parried Penalty` (-0.25) |
| It takes damage | down | `Hurt Penalty` (-0.08) |

It drifts back toward `Start Confidence` over time (`Recover Rate`). What confidence changes:

- **Low confidence:** it hesitates, keeps its distance, circles or backs away, waits longer between attacks, and avoids strong attacks (`Min Confidence`).
- **High confidence:** it thinks faster, attacks more often, stands closer, uses long combos, and (with `Use Run Animation`) runs at you from far away.
- It never swings at you in the middle of a **roll**, it picks a different opener from the last one, and it will not repeat itself.

If an enemy feels too passive, raise `Start Confidence` or `Hit Gain`, or lower `Base Attack Cooldown`. If it is too aggressive, do the opposite or raise `Parried Penalty`.

---

## Boss

Tick **Is Boss**. A boss:

- shows a **boss health bar** with its name (it fades in when the fight starts),
- is not interrupted by normal hits (`Boss Hyper Armor`), recovers faster from parries and hits (`Boss Stagger Mult`),
- never loses interest once the fight starts,
- never gets scared below `Boss Confidence Floor` (0.4).

### Phases

Add entries to **Boss Phases**. Each has `Starts At Health` (0.66 = when health falls to 66%), `Speed Mult`, `Damage Mult`, `Cooldown Mult` and `Confidence Boost`. When a phase starts the boss plays the `BossPhase` state for `Phase Change Duration` and cannot be hurt during it. Set **Min Phase** on attacks so some only unlock in later phases.

### Boss bar

The built-in bar sits at the **bottom** of the screen (`Boss Bar Anchor`). You can decorate it with your own sprites: `Background`, `Fill` (it shrinks from the right), `Trail` (the pale damage part), `Frame`, and `Nameplate`, plus a font, name size and color. Any sprite you leave empty uses the built-in look, so you can mix your art with the default. Set the sprites' texture type to **Sprite (2D and UI)**.

### Victory banner

When the boss dies, **"ENEMY FELLED"** fades in over a dark band. Change `Victory Text`, `Font`, color and timing, or give it a `Victory Sprite` (for example your own artwork) and a `Victory Sound`. Untick `Show Victory Banner` to turn it off.

---

## Health bar over the head (normal enemies)

Normal enemies show a small bar over their heads when they notice you or get hurt, and it fades after `Head Bar Hide Delay` (4 s). Tick `Head Bar Always Visible` to show it all the time.

| Setting | Meaning |
|---|---|
| `Head Bar Height` / `Head Bar Anchor` | how high it floats, or an empty object above the head to attach it to |
| `Head Bar Size`, `Head Bar Max Distance` | size, and how far away it can be seen |
| `Head Bar Name` | an optional name over the bar |
| `Head Bar Background / Fill / Frame` | optional sprites; empty = built-in |
| `Head Bar Fill / Back / Trail Color` | the built-in colors |

---

## Root motion

With **Use Root Motion** on, attack and hit clips move the enemy (applied through the NavMeshAgent), while walking and strafing still use the NavMesh. Each melee attack has its own `Use Root Motion` tick. Turn it off to use the lunge settings instead. In the clips' import settings, leave *Root Transform Position (XZ)* with **Bake Into Pose off** to let it move.

---

## Animator states

Every name is in `Animation/AnimId.cs` (change it there if you want other names). Create these in the Base Layer. No transitions needed.

| State | When it plays |
|---|---|
| `Idle` | standing still |
| `Walk` | walking toward you |
| `Run` | only if `Use Run Animation` is on |
| `StrafeLeft`, `StrafeRight` | circling you |
| `WalkBack` | backing away |
| your attack names (`Attack1` ... `Attack8`, `Shoot`, `Summon`, or anything) | one per attack's `Anim Name` |
| `Hit` | took a hit |
| `Stagger` | got parried |
| `BossPhase` | boss changes phase |
| `Die` | died |
| your **Emerge Anim** | when this enemy is summoned (optional) |

An optional float parameter `Speed` is set from the enemy's walking speed, for blend trees.

---

## Hitting enemies and lock-on

- Enemies take damage from anything that sends a `DamageInfo` to them: the player's sword (`WeaponHitbox`), arrows, or your own code.
- Every living enemy registers itself in `EnemyAI.All`, which the player's lock-on and aim assist use. Set **Lock Point** to a chest or head object to choose where the lock-on aims.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| The enemy does not move | Bake a NavMesh and make sure the enemy stands on it. Tag the player **Player**. Check the Console for a warning from `EnemyAI`. |
| It plays attack animations but never hurts me | Add a **WeaponHitbox** to the weapon collider and put it in **Weapons**. Tick `Debug Log` on the hitbox to see why. Check `Hitbox On Time` is smaller than `Duration`. |
| It stands still at range | No attack is usable at that distance. Add one with `Min Range` 0, or raise `Max Range`. |
| The player's sword does not hurt it | The enemy needs a Collider on its body. |
| It never summons | Fill in `Summon Prefab`, and check `Max Alive` is not already reached. |
| The animation state is not found | The Console names it. Create the state or change `Anim Name`. |
| The boss bar is not visible | `Is Boss` and `Show Boss Bar` must be on, and the bar appears once the fight starts. |
| It slides while attacking | Check the clips' *Bake Into Pose* settings, or turn off `Use Root Motion`. |
| Two enemies stack into one | Give them separate NavMeshAgent avoidance priorities, or space their spawn points. |

---

## Quick reference for your own scripts

```csharp
EnemyAI enemy = GetComponent<EnemyAI>();

enemy.health;                 // current health
enemy.Confidence;             // 0 to 1
enemy.Phase;                  // boss phase (0 = first)
enemy.CurrentState;           // Idle, Combat, Attack, Staggered, Hit, PhaseChange, Dead
enemy.IsDead;
enemy.Aggro();                // wake it up and send it after the player
enemy.Kill();                 // kill it instantly
enemy.TakeDamage(new DamageInfo { amount = 20f, source = someTransform });

foreach (EnemyAI e in EnemyAI.All) { /* every living enemy */ }
```
