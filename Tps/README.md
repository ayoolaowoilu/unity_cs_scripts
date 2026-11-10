# Player Controller

A third-person melee character with a bow. Everything is set from the Inspector, and the animations are played by **Animator state name**, so you do not need to draw transitions.

**What it does**

- Walk, run, strafe, fall off ledges
- **3-hit sword combo** with input buffering
- **Shield guard** (as an upper-body Animator layer, or as full-body Guard states)
- **Parry** (staggers enemies and reflects projectiles)
- **Roll** with i-frames, and a **back jump** when you roll while standing still
- **Lock-on** onto enemies, with strafe and backwards animations
- **Bow**: aim with a zoom camera, draw and shoot, switch weapons with the sword and shield going on your back
- **Root motion** support, so rolls and attacks can be driven by the clips
- Optional polish: dust, a roll mesh trail, the spine leaning toward the aim

> Read the repo's [main README](../README.md) first for the folder layout and requirements.

---

## Files you need

| File | Goes on | Needed? |
|---|---|---|
| `Player/PlayerController.cs` | the **Player** | yes |
| `Camera/ThirdPersonCamera.cs` | the **Main Camera** | yes (see [The TPS camera](#the-tps-camera)) |
| `Combat/WeaponHitbox.cs` | the sword's **collider** object | yes |
| `Animation/AnimId.cs` | nothing | yes |
| `Animation/RootMotionRelay.cs` | nothing (added automatically) | yes |
| `Camera/LockOnSystem.cs` | the **Player** | recommended |
| `Player/BowController.cs` + `Player/Arrow.cs` | the **Player** | needed to compile; leave unused if you have no bow |
| `Effects/SmokeFX.cs` | nothing | yes (the arrow uses it) |
| `Enemy/EnemyAI.cs` | enemies | needed to compile (lock-on and the bow read it) |
| `Player/AimSpineIK.cs` | the **Player** | optional |
| `Effects/DustEffects.cs`, `Effects/RollMeshTrail.cs` | the **Player** | optional |

---

## Setup

### 1. The Player object

1. Create an empty object, name it **Player**, and set its **Tag** to `Player`.
2. Add a **Character Controller** (match the height and radius to your model, and set the center so the bottom touches the ground).
3. Put your character model **under** the Player (as a child). The model has the **Animator**.
4. Add **PlayerController**. It finds the Animator, camera, `LockOnSystem` and `BowController` by itself.

### 2. The sword hitbox

1. Select the sword, or the blade, and add a **Collider** (Box or Capsule) that covers the part that hits. A Mesh Collider must be **Convex**.
2. On that same object add **WeaponHitbox**.
3. Drag it into **Sword** on `PlayerController` (it also finds one by itself if you leave it empty).
4. Leave **Target Layers** on Everything, or choose the layer your enemies are on.

The collider switches itself on and off during your attack. While the game runs, the active hitbox is drawn as a red box in the Scene view (turn Gizmos on). Tick **Debug Log** on the `WeaponHitbox` to see in the Console why a hit does or does not land.

### 3. The camera

See [The TPS camera](#the-tps-camera).

### 4. The Animator Controller

Create the states listed in the next section, **directly in the Base Layer** (not inside a sub-state machine). Name them exactly, because names are case-sensitive.

**No transitions needed.** The script cross-fades to a state by name. You only need float parameters if you use blend trees:

| Parameter | Type | Used for |
|---|---|---|
| `Speed` | Float | optional, 0 to 1 walking amount |
| `MoveX`, `MoveY` | Float | the `GuardWalk` and `AimWalk` blend trees (left/right and forward/back) |

### 5. Optional add-ons

Add `LockOnSystem`, `BowController`, `AimSpineIK`, `DustEffects` and `RollMeshTrail` to the Player when you want them.

---

## Controls

All keys can be changed in the Inspector.

| Action | Default | Notes |
|---|---|---|
| Move | WASD | relative to the camera |
| Run | Left Shift | |
| Attack | Left Mouse | tap again to chain the combo |
| Guard | hold Right Mouse | sword mode |
| Parry | Q | |
| Roll | Space | back jump if you are not holding a direction |
| Lock on | Tab | press again to release; scroll wheel or Left/Right arrows switch target |
| Switch weapon | R | or `1` for sword and `2` for bow |
| Aim (bow out) | hold Right Mouse | camera zooms in |
| Draw / shoot (bow out) | hold Left Mouse, release to shoot | |
| Free the cursor | Esc | click to lock it again |

---

## Animator states

Every state name is defined in `Animation/AnimId.cs`. To use a different name, change the string there and nowhere else.

| State | When it plays |
|---|---|
| `Idle`, `Walk`, `Run` | normal movement |
| `Attack1`, `Attack2`, `Attack3` | the three sword hits (the names are set per hit in **Attack Combo**) |
| `Parry` | parry |
| `Roll`, `BackJump` | roll, and the hop backwards when standing still |
| `Fall` | walked off a ledge |
| `Hit` | took damage |
| `HitReaction` | blocked a hit (only when **not** using the shield layer) |
| `Guard`, `GuardWalk` | shield up (only when **not** using the shield layer) |
| `Die` | health reached 0 |
| `StrafeLeft`, `StrafeRight`, `WalkBack`, `RunLeft`, `RunRight`, `RunBack` | moving while locked on (forward uses `Walk` and `Run`) |
| `EquipBow`, `EquipSword` | switching weapons (optional) |
| `AimIdle`, `AimWalk`, `BowDraw`, `BowShoot` | the bow |

If you do not have a clip for a state, use one of the options in the matching section below. A missing state only makes Unity print a warning, nothing breaks.

**Blending.** Under **Animation Blend (seconds)** you can set how long each cross-fade takes: `locomotionBlend` (0.2), `actionBlend` (0.08) and `hitBlend` (0.05). Playing the same state again blends it into itself, so repeated attacks stay smooth.

---

## Combat

### The 3-hit combo

Under **Attack Combo** there is one entry per hit. Pressing attack again once the swing is far enough along chains into the next hit, and early presses are remembered (**Buffer Attack Input**).

| Per-hit field | Meaning |
|---|---|
| `Anim Name` | the Animator state for this hit |
| `Damage` | damage the sword deals on this hit |
| `Blend` | seconds to blend in |
| `Hit Start` / `Hit End` | when the sword hitbox is on, as a fraction of the clip (0.3 = 30% through) |
| `Chain From` | from here on, pressing attack starts the next hit |
| `End At` | the hit ends here if you did not chain |
| `Duration`, `Hit On Time`, `Hit Off Time` | backup timing in seconds, used only if the clip cannot be read |

Other settings: **Combo Reset Time** (0.6 s after a hit, the combo starts over), **Attack Move Speed** (0 = you cannot walk while attacking) and **Hitbox Timing**:

- `AnimationProgress` (default): the hitbox follows the real clip.
- `Seconds`: uses the backup seconds.
- `AnimationEvents`: call `AnimEvent_HitboxOn` and `AnimEvent_HitboxOff` from your clips.

Tick **Debug Attack** to log which hit is playing and whether the sword is assigned. Add more hits by increasing the size of the list.

### Guard and the shield layer

Holding guard reduces damage from hits in front of you (`Guard Damage Multiplier` 0.2 means you take 20%, and `Guard Angle` is the front arc that counts).

**Shield layer (recommended).** With **Use Shield Layer** on, raising the shield fades an Animator **layer** in over normal walking, so the legs keep walking as usual:

1. In the Animator window, open **Layers** and add a layer called **`Shield`** (the name must match `Shield Layer Name`).
2. Set the layer's **Mask** to your upper-body Avatar Mask. Leave **Blending** on Override.
3. Add a state with your shield-up clip and make it the layer's **default state**.

The script sets the layer weight to 0 and fades it to 1 while you guard (`Shield Blend In` 0.12 s, `Shield Blend Out` 0.2 s).

| Setting | What it does |
|---|---|
| `Shield Layer State` | optional state on the layer played each time the shield goes up |
| `Shield Hit State` + `Shield Hit Duration` | optional recoil on the layer when you block a hit; it returns to the raised pose by itself |
| `Shield Walk Speed Multiplier` | 1 = walk at normal speed with the shield up |
| `Shield Faces Camera` | on = face the camera and use strafe / backwards animations |

Blocking a hit with the layer does not stun you. If there is no layer with that name, the script prints a warning and uses the full-body `Guard` and `GuardWalk` states instead.

### Parry

Press Q. The first `Parry Window` (0.25 s) of the parry is active: a hit from the front is cancelled, the attacker is staggered (and takes extra damage while staggered), and enemy projectiles are sent back. `Parry Cooldown` is 0.6 s.

### Roll and back jump

- **Roll** (Space while holding a direction): rolls that way. You cannot be hit between `Roll Invuln Start` and `Roll Invuln End` (0.05 to 0.4 s).
- **Back jump** (Space while standing still): a small hop backwards with its own i-frames and a `Backstep Hop Height`.
- Set `Roll Duration` and `Backstep Duration` to the length of your clips.

### Taking damage

Hits reduce `health` (`Max Health` 100). A clean hit plays `Hit` and stuns you for `Hit Stun`. A hit while rolling is dodged. There is no health bar included: read `health` and `maxHealth` from `PlayerController` to draw your own.

---

## Root motion

With **Use Root Motion** on, attacks, parry, roll and hit knockback are moved by the animation clips instead of by code. The script applies the movement safely through the Character Controller.

- In each clip's import settings (Animation tab), leave **Root Transform Position (XZ)** with *Bake Into Pose* **off** so it moves, and tick *Bake Into Pose* on **Position (Y)** so gravity stays in the script.
- The Animator will say "Handled by Script". That is expected.
- **Root Motion Multiplier** scales how far the clips move you.
- Walk, run and strafe still use the speed numbers unless you tick **Root Motion Locomotion**.

---

## Lock-on

Add **LockOnSystem** to the Player. Press Tab near an enemy to lock on. No layers or colliders are needed, because it finds every `EnemyAI` in the scene.

- A red `.` is drawn over the target (change `Indicator Text`, `Color`, `Size` and `Offset`).
- The camera swings behind you so the target stays in front, and the player faces it.
- Moving uses the strafe and backwards animations (`Lock-On Movement` sets the side and backwards speed multipliers).
- The lock drops if the target dies (it jumps to the nearest enemy if `Auto Switch On Kill` is on), gets too far away (`Break Range`), or stays hidden for `Lost Sight Time`.
- If nothing happens on Tab, the Console says why (no enemies, out of range, or line of sight blocked).

---

## Bow and switching weapons

Add **BowController** to the Player (and `Arrow.cs` and `SmokeFX.cs` must be in the project).

### Weapon sockets

The script moves your sword, shield and bow between "sockets", which are empty objects placed where each weapon should sit.

1. Under the chest or spine bone, create empties named `SwordBack`, `ShieldBack` and `BowBack`.
2. Under the left hand bone, create an empty named `BowHand`.
3. Move and rotate the **sockets** (not the weapons) in the Scene view until each weapon sits right.
4. On `BowController`, fill in the three items:

| Slot | Item | Hand Socket | Back Socket | Starts In Hand |
|---|---|---|---|---|
| Sword | your sword object | leave empty | `SwordBack` | on |
| Shield | your shield object | leave empty | `ShieldBack` | on |
| Bow | your bow object | `BowHand` | `BowBack` | **off** |

`BowController` goes on the Player **only once**; the sockets get no script. If a **Back Socket** is empty, that weapon just hides when put away.

Press **R** (or 1 / 2) to switch while standing on the ground. The player is busy for `Equip Duration`, and the weapons change hands at `Swap Time`. You can tick `Swap On Animation Event` and call `AnimEvent_SwapWeapons` from your equip clips. Clear `Equip Bow Anim` / `Equip Sword Anim` if you have no clips for them.

### Aiming and shooting

- Hold Right Mouse to aim. The camera moves in over the shoulder and zooms, a crosshair appears, and you strafe slowly facing the camera. The crosshair turns red over an enemy.
- Hold Left Mouse to draw, release to shoot. Releasing before `Min Charge` cancels the shot. A fuller draw gives more damage and speed (`Min/Max Damage`, `Min/Max Speed`).
- Arrows fly in an arc and aim slightly high so they land on the crosshair (`Compensate Drop`). They stick into what they hit.
- **Aim assist**: an enemy within `Aim Assist Angle` (2.5 degrees) of the crosshair pulls the shot onto it. Set it to 0 to turn it off.
- **Ammo**: unlimited by default. Untick `Unlimited Arrows` to use `Arrows`, and call `AddArrows(int)` from your pickups.
- Optional: your own `Arrow Prefab`, `Crosshair Sprite`, draw and shoot sounds, and an `Arrow Spawn` transform on the bow's nock.

### Spine lean (optional)

Add **AimSpineIK** to the Player. While aiming, the spine turns up, down and sideways toward the aim point so the shot looks accurate. It works on top of your animations. A Humanoid rig is detected automatically; for a Generic rig, drag your spine bones in. `Strength` and the limits are adjustable.

---

## Optional effects

| Script | What it does | Setup |
|---|---|---|
| `DustEffects` | dust under the feet when walking or running, when rolling and landing, and where the sword brushes the floor | add to the Player; drag an empty object at the sword tip into `Weapon Tip` |
| `RollMeshTrail` | fading ghost copies of the character while rolling and on chosen combo hits (attack 3 by default) | add to the Player; change colors, fade time and `Attack Trail Hits` |

Both build their own particles and materials, so they need no art. If the trail ghosts look the wrong size, set `Skinned Scale` to NoScale or UseTransformScale.

---

## The TPS camera

`Camera/ThirdPersonCamera.cs` is an over-the-shoulder orbit camera. It also handles lock-on and bow zoom.

### Setup

1. Put **ThirdPersonCamera** on your **Main Camera** (tagged `MainCamera`). The camera must **not** be a child of the player.
2. Drag the Player into **Target**. `Player`, `Lock On` and `Bow` are found automatically.
3. Set **Collision Mask** to your world layers and **untick** the Player and Enemy layers, or the camera will bump into them.

### What it does

- **Orbit** with the mouse around a pivot at about head height. The cursor is locked on start (Esc frees it, a click locks it again).
- **Collision**: if a wall is behind you, the camera moves in.
- **Guard zoom**: the camera moves closer while you guard.
- **Lock-on**: while locked on, mouse look is off and the camera swings behind you toward the target.
- **Bow aim**: it slides over the shoulder, moves in and narrows the field of view. Drawing zooms in a little more. Turning is slower while aiming for precision. Free aim takes priority over lock-on.

### Settings

| Section | Setting | Default | Meaning |
|---|---|---|---|
| Target | `Pivot Offset` | (0, 1.6, 0) | the point the camera orbits |
| Distance | `Distance` | 4.2 | how far behind you |
| | `Guard Distance` | 3.2 | distance while guarding |
| | `Shoulder Offset` | (0.6, 0, 0) | slide to the right; 0 = centered behind you |
| | `Zoom Smooth` | 8 | how fast distances change |
| Look | `Sensitivity X` / `Y` | 2.5 / 2 | mouse speed |
| | `Min Pitch` / `Max Pitch` | -25 / 60 | up/down limits |
| | `Invert Y`, `Lock Cursor` | off / on | |
| Bow Aim | `Aim Distance` | 2.1 | camera distance while aiming |
| | `Aim Shoulder Offset` | (0.75, 0.1, 0) | position over the shoulder |
| | `Aim Fov` | 40 | zoomed field of view |
| | `Draw Zoom` | 6 | extra zoom at a full draw |
| | `Aim Sensitivity Mult` | 0.6 | slower turning while aiming |
| Lock-On | `Lock Follow Speed` | 8 | how fast it swings to the target |
| | `Lock Pitch` | 15 | camera tilt while locked on |
| Follow | `Follow Smooth Time` | 0.05 | small = tight follow, larger = floaty |
| Collision | `Collision Mask`, `Collision Radius`, `Collision Padding` | all, 0.25, 0.1 | |

---

## Troubleshooting

| Problem | Fix |
|---|---|
| The player does not move | Check the Player has a **Character Controller**, and that **Active Input Handling** is *Input Manager (Old)* or *Both*. |
| The camera does not follow | The script must be on the Main Camera, with **Target** set. |
| Animation does not play | The state name must match exactly (see the table). A Console warning names the missing state. |
| The sword never damages enemies | Tick `Debug Log` on the `WeaponHitbox`. The enemy needs a Collider on its body, and `Target Layers` must include its layer. |
| The player slides or drifts during attacks | Check the clips' *Bake Into Pose* settings (see Root motion), or untick `Use Root Motion`. |
| The camera shakes against walls | Untick the Player and Enemy layers in **Collision Mask**. |
| Nothing happens when pressing R | You can only switch while standing on the ground, not attacking, rolling or aiming. |
| Shield does not show | The layer must be named `Shield`, have your Avatar Mask, and its default state needs a clip. |
| Lock-on does nothing | Enemies need the `EnemyAI` script. Check the Console message after pressing Tab. |

---

## Quick reference for your own scripts

```csharp
PlayerController player = FindObjectOfType<PlayerController>();

player.health;                 // current health
player.maxHealth;
player.IsDead;                 // true after death
player.CurrentState;           // Idle, Move, Run, Guard, Aim, Roll, Attack, Hit ...
player.CurrentAttackIndex;     // 0, 1 or 2 during the combo
player.TakeDamage(new DamageInfo { amount = 10f, source = someTransform });

BowController bow = player.bow;
bow.BowEquipped;               // true when the bow is out
bow.AddArrows(5);              // when not using Unlimited Arrows
```
