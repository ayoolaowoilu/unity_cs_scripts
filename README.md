# Unity C# Scripts

Scripts that can help you in your game. Drop them into your Unity project and you get a third-person combat character, a smart enemy AI, a third-person camera with lock-on, a bow, and some polish effects, all editable from the Inspector.

Repo: https://github.com/ayoolaowoilu/unity_cs_scripts

```bash
git clone https://github.com/ayoolaowoilu/unity_cs_scripts.git
```

## What you get

| Feature | Where to read about it |
|---|---|
| **Player**: walk, run, 3-hit sword combo, shield guard (as an Animator layer), parry, roll, back jump, lock-on, bow and weapon switching | [`Player/README.md`](Player/README.md) |
| **Third-person (TPS) camera**: orbit, wall collision, lock-on, bow aim zoom | [`Player/README.md`](Player/README.md#the-tps-camera) |
| **Enemy AI**: NavMesh, melee / ranged / summoner types, random attacks and combos, confidence, boss phases, health bars | [`Enemy/README.md`](Enemy/README.md) |
| **Effects**: footstep and weapon dust, roll mesh trail, smoke | below |

## Folder guide: what goes in each folder

```
unity_cs_scripts/
├── README.md                  <- this file
├── Player/
│   ├── README.md              <- how to use the player + the TPS camera
│   ├── PlayerController.cs
│   ├── BowController.cs
│   ├── Arrow.cs
│   └── AimSpineIK.cs
├── Camera/
│   ├── ThirdPersonCamera.cs
│   └── LockOnSystem.cs
├── Enemy/
│   ├── README.md              <- how to use the EnemyAI
│   ├── EnemyAI.cs
│   ├── Projectile.cs
│   └── SummonRise.cs
├── Combat/
│   └── WeaponHitbox.cs
├── Animation/
│   ├── AnimId.cs
│   └── RootMotionRelay.cs
└── Effects/
    ├── DustEffects.cs
    ├── RollMeshTrail.cs
    └── SmokeFX.cs
```

| Folder | What goes in it | Scripts | Goes on |
|---|---|---|---|
| **Player/** | Everything about the player character | `PlayerController` | the Player |
| | | `BowController` (bow, arrows, switching weapons) | the Player |
| | | `AimSpineIK` (spine leans toward the aim, optional) | the Player |
| | | `Arrow` | nothing (made by `BowController`) |
| **Camera/** | The camera and targeting | `ThirdPersonCamera` | the Main Camera |
| | | `LockOnSystem` | the Player |
| **Enemy/** | Enemy brains and what they fire or summon | `EnemyAI` | every enemy |
| | | `Projectile` | nothing (made by `EnemyAI`) |
| | | `SummonRise` | nothing (added to summoned things) |
| **Combat/** | Shared damage code used by player and enemies | `WeaponHitbox` (also holds `IDamageable`, `IParryable`, `DamageInfo`, `HitResult`) | every weapon collider |
| **Animation/** | Animation helpers | `AnimId` (list of Animator state names + the cross-fade helper) | nothing (just a list) |
| | | `RootMotionRelay` | nothing (added automatically) |
| **Effects/** | Visual polish | `DustEffects` (optional) | the Player |
| | | `RollMeshTrail` (optional) | the Player |
| | | `SmokeFX` | nothing (created automatically) |

## Quick start

1. Copy **all the folders** into your project, for example `Assets/Scripts/`. Keep the file names as they are (Unity needs the file name to match the script's class name).
2. Set up the player: follow [`Player/README.md`](Player/README.md).
3. Set up an enemy: follow [`Enemy/README.md`](Enemy/README.md).
4. Bake a NavMesh, tag your player **Player** and your camera **MainCamera**, then press Play.

## Requirements

- Unity 2021 LTS or newer is recommended.
- **Input**: the scripts use Unity's classic Input Manager (`Input.GetKey`). If you use Unity 6 or a new project, go to *Edit > Project Settings > Player > Active Input Handling* and pick **Input Manager (Old)** or **Both**.
- **NavMesh**: enemies walk on a baked NavMesh. In newer Unity versions add the *AI Navigation* package and a `NavMeshSurface` to your ground, or use *Window > AI > Navigation* in older ones.
- **Render pipeline**: the effects use the built-in `Sprites/Default` shader, so they work in Built-in, URP and HDRP.
- An Animator Controller with states named as listed in the READMEs. The scripts play states **by name** with a cross-fade, so you do not need to draw transitions between them.

## Good to know

- **The scripts work as one set.** They reference each other, so copy every folder. Only `AimSpineIK`, `DustEffects` and `RollMeshTrail` are fully optional (nothing else needs them, and you can delete them).
- **Class names must be unique in a Unity project.** If you add scripts from another project that has its own `EnemyAI`, `PlayerController` or similar, keep them in separate projects or rename the classes.
- Animation state names live in one place, `Animation/AnimId.cs`. Change a string there if your Animator uses different names.
- There is no health UI for the player, no inventory and no save system. The player exposes `health` and `maxHealth` so you can add your own.
- Weapons, effects and bars all work without any prefabs or art: every visual has a built-in default, and you can replace it with your own sprites or prefabs in the Inspector.
