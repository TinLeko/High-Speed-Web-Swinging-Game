# High-Speed Web-Swinging Game

A game inspired by the traversal mechanics of Spider-man. The player has various abilities at his disposal, such as web swinging, web zipping, wall running, diving and more. 
The game is trial based, meaning there are multiple time trials where your goal is to complete the course in the fastest time possible. Upon completion, you will get a medal.

## Controls
 - **W, A, S, D** - Movement, while on the ground, in the air and during a swing
 - Walking up to a wall holding a movement key will trigger a wall crawl
 - **Shift:** - Sprinting (while on the ground)
 - Wall running and well climbing when held while in the air and possessing enough speed 
 - Climbing will be performed if hitting the wall head on, which will turn your
 horizontal momentum into upwards momentum
 - Hitting the wall at a sideways angle will trigger wall
 running, holding the key while hitting<br> a corner will perform a corner turn
 - **Space** - Jumping while on the ground, in the air, and off of a wall
 - Holding space while on the ground will charge the jump
 - Pressing space during a web swing will perform a swing jump, which will launch you higher and farther
 - **Right-click** - Hold to perform a web swing, fires the web at an upwards angle and makes
 your rigidbody<br> move like a pendulum
  - The web will stay attached as long as you hold it
  - Releasing the key or jumping will cut the web and give you momentum
  - **Left-click** - Performs a web zip, which will fire off a web line<br>
  in the direction of your cursor, pulling you towards it and giving you a momentum boost<br>
  upon finishing
  - Performing consecutive web-zips diminishes their speed, web swinging, wall running / climbing / crawling
  <br> and touching the ground resets this penalty
  - **CTRL** - Holding this key while in the air and after performing a web swing will trigger an **air-dive**
  - The dive boost your downward momentum (makes you fall faster)
  - Performing a web swing after a dive will transfer your downward momentum into horizontal swing momentum,
  <br> the amount of momentum gained depens on your downward velocity and how long you held the key
  - **E, Q, R** - Used for accepting the race, retriying the race, declining the race, aborting the race and<br>
  restarting at a checkpoint, the detailes are explained in the UI

## Table of contents

1. [Overview](#1-overview)
2. [Script inventory](#2-script-inventory)
3. [Requirements and packages](#3-requirements-and-packages)
4. [Architecture: how the pieces fit together](#4-architecture-how-the-pieces-fit-together)
5. [Scene setup checklist](#5-scene-setup-checklist)
6. [PlayerMovement](#6-playermovement)
7. [PlayerSwinging](#7-playerswinging)
8. [WebZip](#8-webzip)
9. [WallRun](#9-wallrun)
10. [Camera scripts](#10-camera-scripts)
11. [SpeedometerUI](#11-speedometerui)
12. [The time trial system](#12-the-time-trial-system)
13. [End-to-end walkthroughs](#13-end-to-end-walkthroughs)
14. [Tuning guide](#14-tuning-guide)
15. [Public API quick reference](#15-public-api-quick-reference)
16. [Appendix: formulas](#16-appendix-formulas)

---

## 1. Overview

### Disclaimer: the README was written by Claude, provided only with the current versions of the scripts and in depth explanations of the mechanics, without having seen the editor

The game is built around one idea: **moving fast and keeping your momentum**. The player can:

- **Walk, sprint and charge-jump** on the ground (`PlayerMovement`).
- **Swing on webs** from buildings using a physics rope, with a combo system that raises the speed cap the better you chain swings (`PlayerSwinging`).
- **Release or jump off a swing** for speed rewards that depend on timing (`PlayerSwinging`).
- **Dive** downward to build speed that is converted into swing speed (`PlayerSwinging`).
- **Air jump** once after each web release (`PlayerSwinging`).
- **Web zip** to a point under the crosshair, getting yanked there and launched with a speed burst (`WebZip`).
- **Wall run, climb and crawl** on vertical surfaces, follow corners, leap off and vault over ledges (`WallRun`).
- **Land with momentum**, so touching the ground after a swing does not kill your speed (`PlayerMovement` + `PlayerSwinging`).
- **Race time trials**: fly through hoops in order, with a timer, medals, top speed, a navigation arrow, checkpoint respawns and an optional time limit (the `TimeTrial*`, `Race*` scripts).
- A third-person Cinemachine camera whose field of view and distance react to speed, tilts when diving and turns with wall-run corners (`CameraSmoothing`, `CameraEffects`, `PlayerCamera`).
- A speedometer and speed-lines effect (`UIManager`).

The scripts are designed so that each ability **temporarily takes over** the player's Rigidbody and then **hands it back in a clean state**. Understanding that handoff pattern ([section 4.4](#44-the-handoff-pattern-beginzipmomentum--endzip)) is the key to understanding the whole project.

---

## 2. Script inventory

| File as uploaded | Class inside | Where it lives | What it does |
|---|---|---|---|
| `PlayerMovement.cs` | `PlayerMovement` | Player object | Baseline ground and air movement, charged jump, slope handling, ground drag, landing momentum carry. |
| `PlayerSwinging.cs` | `PlayerSwinging` | Player object | Web swinging, anchor finding, combo speed system, swing release and swing jump, post-release flight, air jump, dive. Also owns the "external momentum" API used by zip, wall run and respawn. |
| `WebZip.cs` | `WebZip` | Player object | Aim at the crosshair, fire a web, pull the player to the target, launch them out with a burst. Consecutive zips lose speed. |
| `Wallrun.cs` | `Wallrun` | Player object | Wall run (sideways), climb (head-on), crawl (slow), corner following, leap and ledge vault. |
| `PlayerCamera.cs` | `PlayerCamera` | Any scene object (camera rig) | Computes the camera-relative `orientation` transform and turns the player model. |
| `CameraEffects.cs` | `CameraEffects` | Any scene object | Dive tilt, optional climb tilt, wall-run corner turning, camera warp on respawn. |
| `CameraSmoothing.cs` | `CameraSmoothing` | Any scene object | Speed-based field of view and orbit distance. |
| `UIManager.cs` | `UIManager` | UI object | Speed text (km/h) and speed-lines particle intensity. |
| `TimeTrialManager.cs` | `TimeTrialManager` (and the `TrialMedal` enum) | One object in the scene | The race brain: prompt, countdown, hoop checks, timer, top speed, medals, saving, respawn, time limit. |
| `TimeTrialStarter.cs` | `TimeTrialStarter` | One per trial | The world item that starts a trial; holds the course and settings. |
| `TimeTrialUI.cs` | `TimeTrialUI` | Auto-created | Builds all time trial UI in code. |
| `RaceHoopVisual.cs` | `RaceHoopVisual` | Auto-created per hoop | Shows and animates one hoop from your prefab. |
| `RaceNavigationArrow.cs` | `RaceNavigationArrow` | Auto-created | The arrow above the player pointing at the next hoop. |
| `RaceModelUtils.cs` | `RaceModelUtils` (static) | n/a | Helpers for your own models: strip colliders, measure a hoop's radius, tint. |

---

## 3. Requirements and packages

| Requirement | Used by | Notes |
|---|---|---|
| **Unity 6** | Everything | Uses `Rigidbody.linearVelocity` and `linearDamping` (the Unity 6 names). |
| **Input System** package, project-wide actions | All input | Actions are found with `InputSystem.actions.FindAction("Name")`. *Active Input Handling* in Project Settings must include the new Input System. |
| **Cinemachine 3** (`Unity.Cinemachine`) | `CameraEffects`, `CameraSmoothing` | `CinemachineCamera` and `CinemachineOrbitalFollow`. |
| **UGUI** (legacy `Text`) | `TimeTrialUI` | Uses Unity's built-in font `LegacyRuntime.ttf`. |
| **TextMeshPro** | `UIManager` | `TextMeshProUGUI` for the speed text. |
| **Physics** | Swinging, wall run, zip, trials | `SpringJoint` for the swing rope; sphere casts and raycasts for aiming and detection. |

**Recommended Rigidbody settings** (not enforced by code, but strongly advised for a fast game): Interpolation = Interpolate (so the camera and arrow do not jitter), Collision Detection = Continuous Dynamic (so 80 to 100 m/s cannot tunnel through thin geometry), and a zero-friction Physics Material with Friction Combine = Minimum on the player collider (so contacts do not steal your speed). `PlayerMovement` sets `freezeRotation = true` for you.

---

## 4. Architecture: how the pieces fit together

### 4.1 The player object

The player is **one GameObject** with a Rigidbody and a collider, carrying four cooperating scripts:

```
Player (Rigidbody, Collider)
 ├─ PlayerMovement   baseline movement (always running)
 ├─ PlayerSwinging   swing, release, flight, air jump, dive, and the shared momentum API
 ├─ WebZip           zip ability
 └─ WallRun          wall run / climb / crawl
```

They find each other with `GetComponent` in `Awake`, so they **must all be on the same object**. `PlayerMovement`, `PlayerSwinging` and `WallRun` all dereference `WebZip` directly (to reset the zip chain), so `WebZip` is effectively required on the player (see [section 16](#16-known-quirks-and-gotchas)).

Other scripts reference the player's Rigidbody or components: `CameraSmoothing` (Rigidbody), `CameraEffects` (`WallRun`, `PlayerSwinging`), `PlayerCamera` (`WallRun`), `SpeedometerUI` (Rigidbody), `TimeTrialManager` (finds the player through `PlayerSwinging`).

### 4.2 Who is in control right now

Only **one** system should be driving the Rigidbody at any moment. The scripts decide this with a few shared flags:

| Situation | In control | How the others stay out of the way |
|---|---|---|
| Walking, sprinting, jumping, ordinary falling | `PlayerMovement` | `swingMomentum` is false, so its forces and speed cap run. |
| Web attached (swinging) | `PlayerSwinging` (`SwingPhysics`) | `isSwinging` and `swingMomentum` are true. `PlayerMovement.MovePlayer` and `SpeedControl` return early. |
| Just released a web, in the air-control window (1.2 s by default) | `PlayerSwinging` (`AirControl`, air gravity, air drag) | `swingMomentum` stays true until the window ends or you land. |
| Zipping | `WebZip` | `WebZip` sets `swingMomentum` through `BeginZipMomentum()`. `PlayerSwinging` returns early because `Busy` is true. |
| Wall running, climbing or crawling | `WallRun` | Same: `BeginZipMomentum()` on attach; `PlayerSwinging` returns early because `Busy` is true. |
| After any of the above ends | `PlayerSwinging` momentum window, then `PlayerMovement` again | `EndZip()` starts the air-control window. |

### 4.3 The shared flags

| Flag | Defined in | Meaning | Set by |
|---|---|---|---|
| `isSwinging` | `PlayerMovement` | A rope is attached. | `PlayerSwinging.StartSwing` (true), `ReleaseWeb`, `CancelSwingForZip`, `ResetForRespawn` (false). |
| `swingMomentum` | `PlayerMovement` | Something other than `PlayerMovement` owns the velocity (rope attached, or in the air-control window, or zipping, or on a wall). | Set true by `StartSwing` and `BeginZipMomentum`; set false by `EndMomentum` (when the air-control window ends or you land). |
| `jumpConsumed` | `PlayerMovement` | "This jump press was already used for something else." Stops the same press also triggering a ground jump or air jump. | Set true by swing jump, air jump, zip bail-out and wall leap. Reset when the jump key is released. |
| `IsGrounded` | `PlayerMovement` | A downward ray hits the ground mask. | `PlayerMovement.FixedUpdate`. |
| `Busy` | `PlayerSwinging` (private) | `Zipping || OnWall`. While true, `PlayerSwinging.FixedUpdate` returns early and air jump and dive are blocked. | Computed from `WebZip.IsZipping` and `WallRun.IsActive`. |

### 4.4 The handoff pattern: `BeginZipMomentum` / `EndZip`

Three different things need to take over the player and then hand it back: **zip**, **wall run** and **respawn**. They all reuse two methods on `PlayerSwinging`:

```csharp
BeginZipMomentum();                    // taking over
EndZip(exitVelocity, combo, drag);     // handing back
```

- `BeginZipMomentum()` sets `swingMomentum = true` (so `PlayerMovement` stops), clears the air-control timer, clears `airborneAfterSwing`, and ends any dive.
- `EndZip(exitVelocity, combo, dragRelief)` adds combo, **raises the speed cap to your exit speed** (`swingSpeedCap`) so the burst is not clamped, sets your velocity (clamped to the burst cap), applies drag relief, starts the **air-control window**, marks you as airborne-after-swing (which turns on post-release gravity and drag) and **refills your air jump**.

Because every ability ends through `EndZip`, leaving a zip, a wall or a respawn feels the same as leaving a swing: you get a short window of steering control, a high temporary speed cap and one air jump.

There is a second ending for touching the ground: `LandWithMomentum()` (used when you land normally and by `EndWallOnGround()`). It ends the momentum state, resets the combo to 0 and calls `PlayerMovement.BeginLandingCarry()` so you keep some speed on the ground ([section 6.7](#67-landing-momentum-carry)).

### 4.5 The speed model

There are several different speed limits. They apply in different situations:

| Limit | Where | Value (default) | Applies when |
|---|---|---|---|
| `CurrentMaxSpeed` | `PlayerSwinging` | 35 to 80 m/s depending on combo (`baseMaxSpeed` to `topMaxSpeed`) | The normal swing speed cap. |
| `swingSpeedCap` | `PlayerSwinging` | Raised temporarily by dives, zips, wall exits, landing carry | A temporary boost above `CurrentMaxSpeed` that decays by `diveCapDecay` (12 m/s per second). |
| `EffectiveMaxSpeed` | `PlayerSwinging` | `max(CurrentMaxSpeed, swingSpeedCap)` | Cap enforced during a swing. |
| `BurstMaxSpeed` | `PlayerSwinging` | `EffectiveMaxSpeed × 1.25` | Cap right after a release, jump, zip or wall exit; also used in air control. |
| `moveSpeed` / speed cap | `PlayerMovement` | 80 walking, 140 sprinting, 120 stale value after swinging (see quirks) | Ground and normal air movement. Doubles as a force. |
| Landing carry cap | `PlayerMovement` | Fades from your landing speed to normal over 1.5 s | Right after landing. |
| `zipTopSpeed` / `maxExitSpeed` | `WebZip` | 90 / 100 m/s | During and after a zip. |
| `maxRunSpeed`, `maxClimbSpeed`, `maxExitSpeed` | `WallRun` | 70 / 45 / 100 m/s | On and leaving a wall. |

The **combo** (`comboLevel`, 0 to 6) is what raises `CurrentMaxSpeed` and the swing power. It goes up with well-timed releases, swing jumps, zips and wall leaps, and it **resets to 0** when you land or when the combo window (1.5 s) expires.

### 4.6 Update versus FixedUpdate

A consistent rule is used throughout:

- **`Update`** reads *button presses* (`WasPressedThisFrame`, `WasReleasedThisFrame`), because these only report true for a single rendered frame. It also does one-shot velocity changes (cutting the web, air jump, leap).
- **`FixedUpdate`** applies *continuous physics* (forces, steering, hoop checks), because it runs in step with the physics engine.
- **`LateUpdate`** draws visuals that must follow the final position (rope lines, the navigation arrow).

### 4.7 Input actions

All scripts read the project-wide actions asset through `InputSystem.actions`.

| Action name | Used by | Required? | Fallback |
|---|---|---|---|
| `Move` | PlayerMovement, PlayerSwinging, WallRun | Yes | None |
| `Look` | PlayerCamera | Yes | None |
| `Jump` | PlayerMovement, PlayerSwinging, WebZip, WallRun | Yes | None |
| `Sprint` | PlayerMovement, WallRun (as run key if `WallRun` action is missing) | Yes | None |
| `Swing` | PlayerSwinging | **Yes (no null check)** | None |
| `Dive` | PlayerSwinging | No | Left Ctrl |
| `Zip` | WebZip | No | F |
| `WallRun` | WallRun | No | Falls back to the `Sprint` action |
| `TrialConfirm` / `TrialCancel` / `TrialRespawn` | TimeTrialManager | No | E / Q / R |

`PlayerSwinging`, `PlayerMovement` and `PlayerCamera` each also enable/disable the **Player** action map on their `inputActions` asset in `OnEnable`/`OnDisable`. See [section 16](#16-known-quirks-and-gotchas) for why that matters.

### 4.8 Layer masks you must set

| Mask | Script | What to include |
|---|---|---|
| `grappable` | `PlayerSwinging` | Surfaces a web may attach to (buildings). |
| `zippable` | `WebZip` | Surfaces a zip may target. |
| `wallMask` | `WallRun` | Surfaces that count as walls. Only near-vertical faces are used. |
| `groundMask` | `PlayerMovement` | Surfaces that count as ground. **Include the building layer** so rooftops count as ground. |

One layer can appear in several masks. A building layer is normally in all four. A downward ray only reports ground for upward-facing surfaces, and wall detection ignores near-horizontal surfaces, so a roof is never mistaken for a wall.

---

## 5. Scene setup checklist

Follow this once to wire everything up. Each step names the fields that must be filled in.

### 5.1 The player object

1. Create a GameObject with a **Rigidbody** and a **Collider** (capsule recommended). Rigidbody: Interpolate, Continuous Dynamic. Collider: a zero-friction Physics Material (Friction Combine = Minimum).
2. Add **PlayerMovement**, **PlayerSwinging**, **WebZip** and **WallRun** to it.
3. **PlayerMovement**: `Orientation` (an empty child that `PlayerCamera` rotates), `Input Actions` (your actions asset), `Player Height` (the player's full height in metres, used for the ground ray), `Ground Mask` (include rooftops), `Ground Drag`, `Min/Max Jump Force`, `Jump Cooldown`, `Air Multiplier`, `Max Slope Angle`.
4. **PlayerSwinging**: `Input Actions`, `Line Renderer` (the rope), `Web Tip` (an empty child at the hand where the web leaves), `Camera Transform` (the Main Camera's transform), `Grappable` mask.
5. **WebZip**: `Web Tip`, `Zip Line` (a **second** LineRenderer, different from the swing rope), `Zippable` mask. The camera is taken from `Camera.main`, so **the camera must be tagged MainCamera**.
6. **WallRun**: `Camera Transform`, `Wall Mask`. Keep `Probe Radius` smaller than the collider's radius, otherwise the wall probe starts inside the wall and nothing attaches.

### 5.2 The camera

1. A **Cinemachine Camera** with an **Orbital Follow** component. Set its **Tracking Target to the player root** (the object with the Rigidbody), not the model child, so the camera is not dragged around by the model's rotation.
2. **PlayerCamera**: `Input Actions`, `Orientation`, `Player`, `Player Obj` (the visible model), `Rotation Speed`, `Wall Run`.
3. **CameraEffects**: `Wall Run`, `Swinging`, `Orbital` (the Orbital Follow component). Remember to flip `Yaw Direction` / `Pitch Direction` if effects go the wrong way.
4. **CameraSmoothing**: `Camera` (the Cinemachine Camera), `Orbital Follow`, `Target` (the player's Rigidbody).

### 5.3 UI

- **SpeedometerUI**: `Player Rb`, `Speed Text` (a TextMeshProUGUI), `Speed Lines Particle` (a ParticleSystem). Assign the particle system, because the script does not guard against it being empty during `Update`.

### 5.4 Input actions to create

`Move`, `Look`, `Jump`, `Sprint`, `Swing`, and optionally `Dive`, `Zip`, `WallRun`, `TrialConfirm`, `TrialCancel`, `TrialRespawn`. See [section 4.7](#47-input-actions) for fallbacks.

### 5.5 Time trials

See [section 12](#12-the-time-trial-system) for the full setup: one `TimeTrialManager` in the scene (assign a **Hoop Prefab**), and one `TimeTrialStarter` per trial.

### 5.6 A quick test checklist

- [ ] Walk, sprint, charge-jump (hold Jump, release).
- [ ] Hold Swing near a building: a rope attaches, you swing.
- [ ] Release Swing: you fly out with steering control.
- [ ] Press Jump while swinging: a bigger launch.
- [ ] Press Dive in the air, then Swing: you convert fall speed into swing speed.
- [ ] Press Zip while aiming at a building.
- [ ] Sprint at a wall at an angle (run) and head-on (climb); walk into a wall slowly (crawl).
- [ ] Land on a roof after a swing: speed carries over instead of stopping dead.

---

## 6. PlayerMovement

**File:** `PlayerMovement.cs` | **Lives on:** the player.

This is the baseline controller. It handles everything that is *not* a special ability: walking, sprinting, a charge-up jump, slopes, ground friction and air movement. When another system takes over (`swingMomentum == true`) it politely does nothing.

### 6.1 Fields

| Field | Default | Purpose |
|---|---|---|
| `moveSpeed` (private, visible in Inspector) | n/a | Current speed value. Set every frame by `StateHandler`. Used **both as a force and as a speed cap**. |
| `walkSpeed` / `sprintSpeed` / `swingSpeed` (private) | 80 / 140 / 120 | The `moveSpeed` value per state. |
| `airDrag` | 0.3 | Linear air drag rate (per second) while in normal air. |
| `airDragQuadratic` | 0.025 | Extra drag that grows with speed. |
| `postReleaseGravity` | 10 | Extra downward acceleration while in normal air. |
| `fallGravity` | 12 | More downward acceleration while falling. |
| `orientation` | none | A transform whose forward/right define "camera-relative" directions. Updated by `PlayerCamera`. |
| `inputActions` | none | Only used to enable/disable the Player map. |
| `playerHeight` | none | Height used for ground and slope rays. |
| `groundMask` | none | What counts as ground. |
| `groundDrag` | none | Rigidbody drag applied on the ground. |
| `minJumpForce` / `maxJumpForce` | none | Range of the charged jump impulse. |
| `chargeRate` | 12.5 | Jump force gained per second of holding. |
| `jumpCooldown` | none | Seconds before another ground jump. |
| `airMultiplier` | none | Scales steering force in the air. |
| `maxSlopeAngle` | none | Steepest walkable slope. |
| `slopeGravity` | 80 (private) | Downward force that keeps you stuck to slopes. |
| `landingSpeedKeep` | 0.8 | Fraction of horizontal speed kept when you land. |
| `carryDuration` | 1.5 | Seconds for the landing carry to fade out. |
| `carryDragRatio` | 0.1 | Starting ground drag as a fraction of `groundDrag`. |

Public state (written by other scripts): `isSwinging`, `swingMomentum`, `jumpConsumed`. Read-only: `IsGrounded`, `CarrySpeed`, `currentState` (`walking`, `sprinting`, `air`, `swinging`).

### 6.2 Lifecycle

- **Awake**: finds the actions `Move`, `Jump`, `Sprint`; gets `WebZip` and `PlayerSwinging`.
- **Start**: gets the Rigidbody, sets `freezeRotation = true`, starts the jump charge at `minJumpForce`.
- **FixedUpdate**: `MovePlayer()` then the **ground check**: a ray straight down from the player's position, length `playerHeight × 0.5 + 0.2`, against `groundMask`.
- **Update** (every frame): jump charge and release, `SpeedControl()`, carry timer, ground drag, movement direction, state, and gravity toggle (details below).

### 6.3 Movement forces (`MovePlayer`)

Runs every physics step. If `swingMomentum` is true it **returns immediately** (another system is in charge). Otherwise it first resets the zip chain (`zip.consecutiveZips = 0`, see [section 8.7](#87-consecutive-zips-lose-speed)), then picks one of three cases:

1. **On a walkable slope and grounded** (and not mid-jump): pushes along the slope surface with force `moveSpeed`, plus a strong downward force (`slopeGravity`) while rising, so you do not launch off the slope.
2. **Grounded on flat ground**: force `moveDirection.normalized × moveSpeed`.
3. **In the air**: force `moveDirection.normalized × moveSpeed × airMultiplier`, then `AirDrag()`.

`moveDirection` is `orientation.forward × input.y + orientation.right × input.x`, so movement is always relative to where the camera looks.

### 6.4 Speed control

Each frame (not while `swingMomentum`), `SpeedControl()` clamps speed to `CurrentSpeedCap()`:

- On a slope: the **3D** speed is clamped.
- Otherwise: only the **horizontal** speed is clamped and vertical speed is preserved (so jumps and falls are not killed).

`CurrentSpeedCap()` is `moveSpeed` normally. During the landing carry it is the larger of `moveSpeed` and a value that fades from your landing speed down to `moveSpeed`.

### 6.5 The charged jump

The jump is **charge and release**, not press:

- While Jump is held (and the press has not been consumed by another system), `currentJumpForce` grows by `chargeRate × deltaTime`, clamped between `minJumpForce` and `maxJumpForce`.
- When Jump is **released**, if you are grounded, `canJump` is true and `jumpConsumed` is false, the jump fires: vertical velocity is zeroed and an upward **impulse** of `currentJumpForce` is applied. `canJump` goes false for `jumpCooldown` seconds.
- Every release resets the charge to `minJumpForce` and clears `jumpConsumed`.

`jumpConsumed` is how the swing jump, air jump, zip bail-out and wall leap prevent the **same press** from also producing a ground jump when you release it.

### 6.6 Slopes

`OnSlope()` fires a ray down (length `playerHeight × 0.5 + 0.3`) and checks the surface angle is non-zero and below `maxSlopeAngle`. On slopes, movement is projected onto the slope plane. Gravity is turned **off** only when you are on a slope **and** grounded (so you do not slide), and on otherwise. Note that this ray uses **no layer mask**, so it hits everything (see [section 16](#16-known-quirks-and-gotchas)).

### 6.7 Landing momentum carry

Without this, touching the ground after a swing applied full ground drag and a speed cap of 80, killing your speed. Now, when `PlayerSwinging` detects a landing it calls `BeginLandingCarry()`:

1. **Keep a fraction of horizontal speed**: horizontal velocity is multiplied by `landingSpeedKeep` (0.8). Vertical speed is untouched.
2. **Remember it as `carryCap`** and start a `carryDuration` (1.5 s) timer.

During the carry (`carryTimer > 0`):

- **Speed cap** = `max(moveSpeed, lerp(moveSpeed, carryCap, carryTimer / carryDuration))`. It starts at your landing speed and fades down to normal.
- **Ground drag** = `lerp(groundDrag × carryDragRatio, groundDrag, t²)` where `t` goes from 0 at landing to 1 when the carry ends. Drag starts nearly off and eases up, so you slide, then settle.

`PlayerSwinging` also stores the carry speed as its temporary swing cap, so swinging again straight after landing is not clamped back down.

### 6.8 Ground drag and gravity each frame

- If grounded and `swingMomentum` is false: `rb.linearDamping = CurrentGroundDrag()`. Otherwise `0` (so a swing release close to the ground is not braked).
- `rb.useGravity = swingMomentum || !(OnSlope() && grounded)`.

### 6.9 Normal air movement (`AirDrag`)

When airborne **and** `swingMomentum` is false, `AirDrag()` runs each physics step:

- Adds extra downward acceleration: `postReleaseGravity` (10), plus `fallGravity` (12) while falling.
- Applies horizontal drag: `speed *= 1 - clamp01((airDrag + airDragQuadratic × speed) × dt)`. This is equivalent to a deceleration of `0.3·v + 0.025·v²`: about **16 m/s²** at 20 m/s, **52 m/s²** at 40 m/s and **184 m/s²** at 80 m/s. The quadratic term dominates above 12 m/s, so very high speeds bleed off quickly.

### 6.10 The state handler

`StateHandler()` sets `currentState` and `moveSpeed`:

| Condition | State | `moveSpeed` |
|---|---|---|
| `isSwinging` | swinging | 120 |
| Grounded and Sprint held | sprinting | 140 |
| Grounded | walking | 80 |
| Otherwise | air | **unchanged** (keeps the last value) |

Because the air state keeps the previous value, `moveSpeed` is stale in the air (for example 120 after a swing). See quirks.

---

## 7. PlayerSwinging

**File:** `PlayerSwinging.cs` | **Lives on:** the player | **Requires:** a Rigidbody (enforced by `[RequireComponent]`).

This is the heart of the game and the largest script. It does five jobs:

1. **Finding a web anchor** with a forgiving, scored search.
2. **Swinging** on a `SpringJoint`, with reeling, extra gravity and steering.
3. **Releasing** the web, with a small reward for a plain release and a large reward for a well-timed **swing jump**.
4. **Post-release flight**: steering, extra gravity, drag, one air jump.
5. **Diving**, whose fall speed is converted into swing speed.

It also exposes the **momentum API** (`BeginZipMomentum`, `EndZip`, `EndWallOnGround`, `ResetForRespawn`, `CancelSwingForZip`) used by `WebZip`, `WallRun` and `TimeTrialManager`.

### 7.1 Inspector fields

**References**

| Field | Purpose |
|---|---|
| `inputActions` | Asset whose Player map is enabled/disabled with this component. |
| `swingAction` | Public, but overwritten in `Awake` with the `Swing` action. |
| `lineRenderer` | Draws the rope. |
| `webTip` | Where the rope visually starts (the hand). |
| `cameraTransform` | Defines "forward" for aiming and steering. (Formerly named `camera`; `FormerlySerializedAs` keeps old scenes working.) |
| `grappable` | Layers a web can attach to. |

**Web aiming (the anchor search)**

| Field | Default | Purpose |
|---|---|---|
| `maxSwingDistance` | 55 | Longest anchor distance. |
| `minAnchorDistance` | 8 | Anchors closer than this are rejected. |
| `minAnchorHeight` | 5 | Anchor must be at least this much above the player. |
| `anchorSphereRadius` | 1.5 | Radius of each search cast (the forgiving "fat ray"). |
| `pitchAngles` | 55, 45, 65, 35, 75 | Upward angles to try. |
| `yawOffsets` | 20, 0, 40, -20, -40, 70, -70 | Horizontal angles to try; positive = toward the swing side. |
| `idealYaw` / `idealPitch` / `idealDistance` | 20 / 55 / 25 | The "perfect" anchor the scoring prefers. |
| `cameraAimBonus` | 10 | Score bonus for the point the camera looks at. |
| `retryInterval` | 0.1 | While Swing is held with no valid anchor, retry this often. |

**Rope and feel**

| Field | Default | Purpose |
|---|---|---|
| `spring` / `damper` | 60 / 6 | `SpringJoint` stiffness and damping. |
| `reelSpeed` | 3 | Rope shortens by `reelSpeed × power` metres per second. |
| `minRopeLength` | 8 | The rope never reels shorter than this. |
| `ropeShootSpeed` | 200 | Visual only: how fast the rope tip travels. |
| `extraGravity` | 15 | Extra downward acceleration while swinging (× power). |
| `swingPushAcceleration` | 12 | Steering push while swinging (× power). |
| `swingCooldown` | 0 | Delay before you can attach again after a release. |

**Combo and speed**

| Field | Default | Purpose |
|---|---|---|
| `baseMaxSpeed` / `topMaxSpeed` | 35 / 80 | Speed cap at combo 0 / max combo. |
| `maxComboLevel` | 6 | Combo ceiling. |
| `comboWindow` | 1.5 | Seconds after a release/dive before the combo expires. |
| `maxPowerMultiplier` | 1.5 | Reel, gravity and push scale up to this at max combo. |
| `burstMultiplier` | 1.25 | Cap multiplier right after a release/jump/zip. |

**Plain release (minor reward)**

| Field | Default | Purpose |
|---|---|---|
| `minGoodSwingTime` | 0.5 | Swing must last this long to count. |
| `minRisingSpeed` | 2 | You must be moving upward at least this fast. |
| `goodReleaseSpeedRatio` | 0.7 | Must be at 70% of the speed cap. |
| `releaseComboGain` | 0.25 | Combo gained. |
| `releaseSpeedBoost` | 1.5 | Extra m/s along your velocity. |

**Swing jump (major reward)**

| Field | Default | Purpose |
|---|---|---|
| `idealJumpAngle` | 35° | Best launch angle above horizontal. |
| `jumpAngleWindow` | 30° | Half-width of the angle window. |
| `minJumpSpeedRatio` / `fullJumpSpeedRatio` | 0.5 / 0.9 | Speed (as a fraction of the cap) below which the jump earns nothing, and above which speed counts fully. |
| `minJumpTiming` | 0.3 | Minimum timing score to earn combo and drag relief. |
| `jumpComboGain` | 1 | Combo gained at perfect timing. |
| `jumpSpeedBoost` | 12 | Extra m/s at perfect timing. |
| `jumpSpeedBoostUp` | 3 | Fixed extra upward m/s on every swing jump. |
| `jumpUpImpulse` | 35 | Upward impulse applied on every swing jump. |
| `jumpDragRelief` / `dragReliefTime` | 0.7 / 1.5 | How much and how long air drag is reduced after a good jump. |
| `debugLogTiming` | off | Prints the timing breakdown to the Console. |

**After release**

| Field | Default | Purpose |
|---|---|---|
| `maxLaunchUpSpeed` | 10 | Upward speed is capped here on release; excess becomes forward speed. |
| `postReleaseGravity` / `fallGravity` | 10 / 12 | Extra gravity during flight, and more while falling. |
| `maxAirJumps` | 1 | Air jumps per release. |
| `airJumpUpSpeed` | 11 | Vertical speed of the air jump. |
| `airJumpMomentumKeep` | 0.85 | Horizontal speed kept by an air jump. |
| `airControlDuration` | 1.2 | Length of the steering window. |
| `steerDegreesPerSecond` | 120 | How fast flight direction can turn. |
| `airAcceleration` | 10 | Horizontal acceleration while steering. |
| `airDrag` / `airDragQuadratic` | 0.3 / 0.025 | Flight drag constants. |

**Dive**

| Field | Default | Purpose |
|---|---|---|
| `diveStartDownSpeed` | 10 | Downward speed you snap to when diving. |
| `diveAcceleration` | 45 | Extra downward acceleration while diving. |
| `maxDiveSpeed` | 70 | Terminal dive speed. |
| `minDiveTime` / `fullDiveTime` | 0.3 / 1.2 | Dive must last this long for any / full swing bonus. |
| `diveCarryTime` | 0.5 | Grace window after releasing dive. |
| `diveSwingConversion` | 0.9 | Fraction of fall speed converted at a full dive. |
| `maxDiveSwingSpeed` | 90 | Cap on converted speed. |
| `diveCapDecay` | 12 | How fast any raised speed cap decays (m/s per second). |

### 7.2 Public read-only properties

For HUDs, camera and other scripts: `ComboLevel` (integer), `ComboT` (0 to 1), `CurrentMaxSpeed`, `LastJumpTiming`, `AirJumpsRemaining`, `IsDiving`.

### 7.3 Lifecycle and input handling

**Awake** finds the actions `Swing`, `Move`, `Jump`, `Dive` and gets `PlayerMovement`, `WebZip`, the Rigidbody and `WallRun`.

**Update** (once per frame):

1. **Jump pressed**:
   - If a web is attached: set `wantsSwing = false` (you must press Swing again to re-attach) and `ReleaseWeb(true)`, a **swing jump**. Return.
   - Else, if an air jump is available and the player is not `Busy` (zipping or on a wall): `AirJump()`.
2. **Dive pressed** and `CanDive()`: `StartDive()`.
3. **Swing pressed**: `wantsSwing = true`.
4. **Swing released**: `wantsSwing = false`, `ReleaseWeb(false)`, a **plain release**.
5. **Retry loop**: while `wantsSwing`, no web is attached and `retryInterval` has passed, call `StartSwing()`. Holding Swing with no anchor in range keeps trying every 0.1 s, so the moment you pass a building a web fires.

> `swingAction.WasPressedThisFrame()` has no null check. If there is no `Swing` action, `Update` throws.

**FixedUpdate** (every physics step), in order:

1. Decay `swingSpeedCap` toward `CurrentMaxSpeed` at `diveCapDecay`.
2. If `Busy` (zipping or on a wall): **return**.
3. If a web is attached: `SwingPhysics()` and return.
4. `UpdateDive()`.
5. If the air-control window is running: `AirControl()`, count it down, and at zero call `EndMomentum()`.
6. If `airborneAfterSwing`: `ApplyAirGravity()` and `ApplyAirDrag()`.
7. **Landing check**: if grounded for more than 0.25 s since the last release and (airborne-after-swing, or combo above 0, or momentum still on): `LandWithMomentum()`. Otherwise, if not diving and the combo window (1.5 s since the last release or dive) has expired, reset the combo to 0.

**LateUpdate**: `DrawRope()`.

### 7.4 Finding an anchor (`TryFindSwingPoint`)

Instead of a single ray, it casts **a grid of fat rays** and picks the best-scoring hit. This is why swinging feels forgiving.

1. **Direction base**: the camera's forward direction flattened to horizontal.
2. **Which side**: normally `swingSide`, which **alternates every swing**; if you hold left/right (more than 0.3), that side is used instead.
3. **Cast grid**: for every yaw in `yawOffsets` (mirrored by the side) and every pitch in `pitchAngles`, sphere-cast from your chest (position + 1 m up) with radius `anchorSphereRadius`, up to `maxSwingDistance`, against `grappable`. That is up to 35 casts.
4. **Rejects**: a hit closer than `minAnchorDistance`, or lower than `minAnchorHeight` above the player.
5. **Score** (lower is better):

```
score = |yaw - idealYaw| × 0.5  +  |pitch - idealPitch| × 0.3  +  |distance - idealDistance| × 0.4  -  bonus
```

6. **Camera aim cast**: one extra cast straight along the camera's forward direction with **double** the radius, treated as yaw = `idealYaw`, pitch = the camera's actual pitch, with `cameraAimBonus` (10) subtracted. Looking directly at a building therefore wins.
7. The lowest score wins. If nothing qualifies, no web fires.

### 7.5 Starting a swing (`StartSwing`)

Guards: not zipping, not already attached, `canSwing` is true. If the player is on a wall, the wall state is cancelled so you can swing off it. Then it finds an anchor and:

1. Resets the zip chain (`zip.consecutiveZips = 0`).
2. Sets `isSwinging` and `swingMomentum` true, clears the air-control timer and `airborneAfterSwing`, records the start time.
3. Adds a **`SpringJoint`** to the player: manual anchor at the swing point, `maxDistance` = current distance to the anchor, `minDistance` = 0, your `spring`/`damper`, `massScale` 1.
4. Applies the **dive conversion** (section 7.14).
5. Starts the rope visual from the web tip, and flips `swingSide`.

**How the rope behaves.** A `SpringJoint` only pulls once you exceed `maxDistance`; it is a springy rope, not a rigid one. The pull is `spring × stretch` and does **not** depend on mass, so a heavier Rigidbody stretches the rope more at the same speed. At high speed the rope stretches noticeably at the bottom of the arc. That is part of the feel, and raising `spring` (or scaling it with mass) makes it stiffer.

### 7.6 Swing physics (`SwingPhysics`, every physics step while attached)

1. **Reel in**: `maxDistance -= reelSpeed × power × dt`, never below `minRopeLength`. Shortening a rope while you swing adds speed, like a pendulum shortening.
2. **Extra gravity**: `extraGravity × power` downward (on top of normal gravity). This makes swings snappier and falls into the arc faster.
3. **Steering**: if you press a direction, a push of `swingPushAcceleration × power` along the camera-relative direction.
4. **Hard cap**: speed is clamped to `EffectiveMaxSpeed`.

`power` is `lerp(1, maxPowerMultiplier, ComboT)`, so a high combo makes reel, gravity and steering all stronger.

### 7.7 The combo system

`comboLevel` is a float from 0 to `maxComboLevel` (6). Its normalised value `ComboT` drives:

- `CurrentMaxSpeed = lerp(baseMaxSpeed, topMaxSpeed, ComboT)` (35 to 80 m/s).
- `Power = lerp(1, maxPowerMultiplier, ComboT)` (1.0 to 1.5).

**Gains:** plain release (+0.25), perfect swing jump (up to +1, scaled by timing), zip (+0.5 by default), wall leap (+0.5 by default).

**Loss:** it resets to **0** when you land (`LandWithMomentum`), when you respawn, or when the combo window (1.5 s since your last release or dive, while not diving) expires. While you are swinging, the window does not run, so a long swing never loses combo.

### 7.8 Releasing the web (`ReleaseWeb`)

Called by letting go of Swing (plain) or pressing Jump (swing jump). It destroys the joint, clears `isSwinging`, hides the rope, sets `canSwing = false` and re-enables it after `swingCooldown` using `Invoke`. Then:

**Plain release** (`jumped == false`). It counts as "good" if all are true: the swing lasted at least `minGoodSwingTime`, you are rising at least `minRisingSpeed`, and you are at least `goodReleaseSpeedRatio` (70%) of `CurrentMaxSpeed`. A good release gives `releaseComboGain` and adds `releaseSpeedBoost` m/s along your velocity.

**Swing jump** (`jumped == true`):

1. `timing` = `EvaluateJumpTiming(velocity)` if the swing lasted long enough, otherwise 0.
2. If `timing >= minJumpTiming`: gain `jumpComboGain × timing` combo and set drag relief strength to `timing`.
3. Velocity gains `velocity.normalized × jumpSpeedBoost × timing`, then `+ up × jumpSpeedBoostUp` (always).
4. An upward **impulse** `jumpUpImpulse` is applied (always).
5. `jumpConsumed = true`, so the press does not also cause a ground jump.

**Then, for both:** `LimitUpwardVelocity` (section 7.9) and a clamp to `BurstMaxSpeed`, then the air phase starts: `lastReleaseTime = now`, the air-control window starts, `airborneAfterSwing = true`, air jumps refill.

> The upward impulse is applied with `AddForce(..., Impulse)` and the velocity is then assigned directly. The impulse is applied by the physics engine on the next step, on top of the assigned velocity, so it bypasses `LimitUpwardVelocity` and the burst clamp. See quirks.

### 7.9 Timing score and launch limiting

**`EvaluateJumpTiming`** returns 0 to 1:

```
elevation   = asin(velocity.y / speed)            (degrees above horizontal)
angleFactor = 1 - clamp01( |elevation - idealJumpAngle| / jumpAngleWindow )
speedRatio  = speed / CurrentMaxSpeed
speedFactor = InverseLerp(minJumpSpeedRatio, fullJumpSpeedRatio, speedRatio)
timing      = angleFactor × speedFactor
```

So the best moment is **fast and rising at about 35°**, which is just after the bottom of the arc. Turn on `debugLogTiming` to see each factor in the Console.

**`LimitUpwardVelocity`** stops a "rocket straight up" launch: if upward speed exceeds `maxLaunchUpSpeed` (10), it is capped and the removed speed is moved into horizontal speed in your flight direction, so total speed is preserved but is aimed forward instead of up.

---

### 7.10 Post-release flight

After a release, `airborneAfterSwing` is true and the **air-control window** (`airControlDuration`, 1.2 s) runs. In `FixedUpdate`, three things happen:

**Air control** (only during the window). If you hold a direction (camera-relative):

- Below 1 m/s horizontal speed: simply accelerate by `airAcceleration`.
- Otherwise: **rotate** your horizontal velocity toward the desired direction by at most `steerDegreesPerSecond` (120°/s), keeping the speed, then add `airAcceleration` along the desired direction. The result is clamped to `BurstMaxSpeed`. Vertical speed is untouched.

When the window ends, `EndMomentum()` is called: `swingMomentum` becomes false and `PlayerMovement` resumes control (see the stacking note in [section 16](#16-known-quirks-and-gotchas)).

**Extra gravity** (`ApplyAirGravity`, until you land): `postReleaseGravity` (10) downward, plus `fallGravity` (12) more while falling. This gives flights a heavier, more arcade-like drop than real gravity.

**Air drag** (`ApplyAirDrag`, until you land):

```
dragRate = airDrag + airDragQuadratic × horizontalSpeed
dragRate *= 1 - jumpDragRelief × dragReliefStrength × reliefFade
horizontalSpeed *= 1 - clamp01(dragRate × dt)
reliefFade = clamp01(1 - timeSinceRelease / dragReliefTime)
```

The relief term is why a **well-timed swing jump keeps its speed longer**: at perfect timing, drag is cut by up to 70%, fading back to full over 1.5 s. Drag affects horizontal speed only.

### 7.11 Air jump

One extra jump in the air after each web release, zip or wall exit (`maxAirJumps`, refilled by `ReleaseWeb` and `EndZip`).

- Allowed when: no web attached, `airborneAfterSwing`, not grounded, jumps remaining, the press is not already consumed, and the player is not zipping or on a wall.
- Effect: horizontal velocity is multiplied by `airJumpMomentumKeep` (0.85, so you lose 15%); vertical velocity becomes `max(currentVertical, airJumpUpSpeed)` (so it works while falling, and never reduces an existing climb).
- Sets `jumpConsumed` so the press cannot become a ground jump when you land.

### 7.12 Landing

Described in the `FixedUpdate` order above. `LandWithMomentum()` does four things: clears `airborneAfterSwing`, calls `PlayerMovement.BeginLandingCarry()`, stores the carry speed as `swingSpeedCap` (so an immediate re-swing is not clamped), ends the momentum state, and resets the combo to 0. The `0.25 s` check after a release prevents a release near the ground from instantly counting as a landing.

### 7.13 Diving

| Step | What happens |
|---|---|
| Start | Pressing Dive while airborne, not attached and not `Busy`. Vertical speed snaps to at most `-diveStartDownSpeed` (10 m/s down). |
| While held | Every physics step: extra downward acceleration `diveAcceleration` (45), downward speed capped at `maxDiveSpeed` (70). `lastDiveTime` is refreshed so the combo window stays open. |
| End (key released) | `EndDive(true)`: starts a `diveCarryTime` (0.5 s) window in which a swing still gets the bonus. |
| End (landed) | `EndDive(false)`: no bonus. |
| End (air jump or swing) | The dive is spent; a dive can only be converted once. |

### 7.14 Dive-to-swing conversion (`ApplyDiveConversion`)

When a web attaches during a dive (or within `diveCarryTime` after one), fall speed that a taut rope would normally absorb is converted into swing speed:

1. The dive must have lasted at least `minDiveTime` (0.3 s). Its strength `scale` is `InverseLerp(minDiveTime, fullDiveTime, duration)`, so a 1.2 s dive converts fully.
2. `outward` = your speed **away from the anchor**. If under 1 m/s, nothing happens.
3. The arc direction is "forward and down, perpendicular to the rope": your horizontal heading projected onto the plane perpendicular to the rope.
4. New velocity = (your velocity with the outward part removed) + (arc direction × `outward × diveSwingConversion × scale`), clamped to `maxDiveSwingSpeed` (90).
5. `swingSpeedCap` is raised to the new speed so it is not clamped immediately; it then decays at `diveCapDecay` (12 m/s per second) back toward `CurrentMaxSpeed`.

### 7.15 The momentum API (used by other scripts)

| Method | Called by | What it does |
|---|---|---|
| `CancelSwingForZip()` | `WebZip` | Destroys the joint, clears `isSwinging`, hides the rope, drops `wantsSwing`. |
| `BeginZipMomentum()` | `WebZip`, `WallRun`, `ResetForRespawn` | Takes control: `swingMomentum` true, clears air-control and airborne flags, ends dive. |
| `EndZip(velocity, combo, dragRelief)` | `WebZip`, `WallRun`, `ResetForRespawn` | Hands control back with a velocity, combo gain, speed-cap raise, drag relief, air-control window and air jump refill. |
| `EndWallOnGround()` | `WallRun` | Stepping off a wall onto the ground: calls `LandWithMomentum()`. |
| `ResetForRespawn(velocity)` | `TimeTrialManager` | Cancels the swing and cooldown timers, zeros the speed cap and combo, then `BeginZipMomentum()` + `EndZip(velocity, 0, 0)`. |
| `ApplyAirGravity()`, `ApplyAirDrag()` | Internal (public) | Flight forces. |

### 7.16 Drawing the rope

`DrawRope()` runs in `LateUpdate` while a web is attached. The visual tip (`currentGrapplePosition`) moves from the web tip toward the anchor at `ropeShootSpeed` (200 m/s), giving a fast "shoot" effect. The **physics** attaches instantly; only the drawing animates. After a release the joint reference is set to null immediately, so the rope stops drawing in the same frame.

---

## 8. WebZip

**File:** `WebZip.cs` | **Lives on:** the player.

A zip is a fast, targeted pull: you aim at a point, a web shoots out, you are yanked toward it and launched out the other side with a burst of speed.

### 8.1 Fields

| Field | Default | Purpose |
|---|---|---|
| `webTip` | none | Visual start of the zip line. |
| `zipLine` | none | A LineRenderer for the zip (must be separate from the swing rope's). |
| `zippable` | none | Layers a zip may target. |
| `maxZipDistance` / `minZipDistance` | 70 / 6 | Allowed target range. |
| `aimAssistRadius` | 0.6 | Radius of the fallback "fat ray". |
| `zipStartSpeed` | 45 | Minimum speed toward the target at launch (the "snap"). |
| `zipTopSpeed` | 90 | Highest pull speed. |
| `zipAcceleration` | 250 | How fast the pull speed ramps up (m/s per second). |
| `steerAcceleration` | 600 | How hard velocity is bent toward the target (m/s per second). |
| `arriveDistance` | 3 | Zip ends this close to the target. |
| `maxZipTime` | 1.5 | Safety timeout. |
| `cooldown` | 0.6 | Delay before the next zip. |
| `ropeShootSpeed` | 300 | Visual only. |
| `consecutiveZips` | 1 | The chain counter (see 8.7). |
| `exitSpeedBonus` | 12 | Extra m/s added at the end. |
| `maxExitSpeed` | 100 | Cap on exit speed. |
| `exitUpBias` | 0.15 | Tilts the exit direction upward so you pop over ledges. |
| `comboGain` | 0.5 | Combo gained by a completed zip. |
| `dragRelief` | 1 | Passed to `EndZip` as the post-zip drag relief strength. |

`IsZipping` is a public read-only property, used by `PlayerSwinging` and `WallRun`.

### 8.2 Lifecycle

- **Awake**: finds the Rigidbody, `PlayerSwinging`, `PlayerMovement`, the `Zip` and `Jump` actions, and caches **`Camera.main`** (so the camera needs the MainCamera tag). If there is no `Zip` action, the **F** key is used.
- **Update**: if zipping, a Jump press **bails out** (sets `jumpConsumed`, `FinishZip(true)`). Otherwise, if Zip is pressed, the cooldown has passed and a target is found: `StartZip`.
- **FixedUpdate**: the pull physics.
- **LateUpdate**: animates the line end toward the target at `ropeShootSpeed`.
- **OnCollisionEnter**: after the first 0.1 s of a zip, hitting anything solid ends it with `FinishZip(true)` (no reward).
- **CancelForRespawn**: stops a zip with no reward (used by the time trials).

### 8.3 Aiming (`TryFindTarget`)

1. **Ray**: from the screen centre if the cursor is locked (it normally is), or from the mouse position if the cursor is free.
2. **Start level with the player**: in third person the camera sits behind the player. The ray's start is moved forward to the point closest to the player, so a wall between the camera and the player cannot be targeted.
3. **Exact ray first**, then a **fat sphere cast** (`aimAssistRadius`) if the exact ray misses. Both use `zippable` and ignore triggers.
4. **Rejected if**: nothing found, the sphere started inside a collider, or the hit is closer than `minZipDistance`.

### 8.4 Starting a zip (`StartZip`)

1. `consecutiveZips++` (chain counter, see 8.7).
2. Stores the target and marks `IsZipping`.
3. `CancelSwingForZip()` and `BeginZipMomentum()` take control from the swing and `PlayerMovement`.
4. `launchDir` is the direction to the target. `zipSpeed = max(zipStartSpeed, your current speed along launchDir)`, so you never start slower than you are already going toward the target, but you always get at least the 45 m/s snap.
5. The line starts at the web tip.

### 8.5 During the zip (every physics step)

1. **Stop if**: you are within `arriveDistance` of the target, you have **passed** it (the target is now behind your launch direction), or `maxZipTime` has elapsed. Each calls `FinishZip(false)`.
2. `zipSpeed` ramps up by `zipAcceleration × dt`, capped at `zipTopSpeed`.
3. Velocity is **bent toward the target** with `MoveTowards(velocity, direction × zipSpeed, steerAcceleration × dt)`. At 600 this changes velocity by up to 12 m/s per step, so the pull has weight and old momentum fades out quickly instead of vanishing instantly.
4. Gravity is cancelled (`AddForce(-Physics.gravity)`) because `PlayerMovement` re-enables gravity every frame while `swingMomentum` is on.

### 8.6 Finishing a zip (`FinishZip`)

1. `IsZipping = false`, starts the cooldown, hides the line.
2. Takes your current velocity. If the zip **completed** (not cancelled): the direction is tilted up by `exitUpBias`, and speed becomes `min(speed + exitSpeedBonus, maxExitSpeed)`.
3. Calls `EndZip(direction × speed / consecutiveZips, combo, dragRelief)`. Combo and drag relief are **0 for a cancelled zip**.

### 8.7 Consecutive zips lose speed

The final velocity is **divided by `consecutiveZips`**, the chain counter:

| Zip in a row | Counter after `StartZip` | Exit speed multiplier |
|---|---|---|
| 1st | 1 | ÷ 1 (full) |
| 2nd | 2 | ÷ 2 |
| 3rd | 3 | ÷ 3 |
| 4th | 4 | ÷ 4 |

**What resets the counter to 0:**

- `PlayerMovement.MovePlayer`, **every physics step in which `swingMomentum` is false**: that is, once you are back under ordinary movement (on the ground, or in the air after the 1.2 s air-control window ends).
- `PlayerSwinging.StartSwing`: starting a web swing.
- `WallRun.StepWall`: every physics step while on a wall.

**Consequences worth knowing:**

- Zips fired within the air-control window of the previous zip (about 1.2 s) chain, because `swingMomentum` stays true. A longer pause lets the counter reset.
- The division applies to **cancelled zips as well** (a jump bail-out or collision), since the division is in the shared `EndZip` call.
- Combo gain is **not** reduced by the chain, only the exit velocity is.
- The raised speed cap is set from the already-divided speed, so later zips do not benefit from the earlier ones' speed.
- The inspector default of `consecutiveZips = 1` is harmless: `PlayerMovement` resets it to 0 within one physics step of starting the game.

---

## 9. WallRun

**File:** `Wallrun.cs` (class `WallRun`) | **Lives on:** the player | **Requires:** a Rigidbody.

`WallRun` lets the player stick to vertical surfaces in three ways, and leave them in several ways.

### 9.1 The three modes at a glance

| Mode | When it starts | What you do | How long it lasts |
|---|---|---|---|
| **Running** (sideways) | Run key held, fast, hitting the wall at an angle, **in the air** | Run along the wall at your entry speed. Follows corners. | As long as the run key is held (unless you set a speed decay). |
| **Climbing** (head-on) | Run key held, fast, within 35° of head-on | Run straight up the wall. A/D steers slightly. | As long as the run key is held (unless you set a speed decay). |
| **Crawling** (slow) | Walk or fall slowly into a wall. **No key needed.** | Move freely on the wall with the movement keys. | While you hold the run key **or** any movement input. |

A jump leaps you off the wall in any mode. Reaching the top of a climb or crawl vaults you over the edge.

"Fast" means horizontal speed of at least `minRunSpeed` (18 m/s). A run or climb whose speed decays below `dropToCrawlSpeed` (10 m/s) turns into a crawl. With the default decay of 0, that only matters if you change the decay values.

### 9.2 Fields

**Detection**

| Field | Default | Purpose |
|---|---|---|
| `cameraTransform` | none | Defines camera-relative input for crawl attach. |
| `wallMask` | none | Layers that count as walls. |
| `probeRadius` | 0.3 | Radius of the wall probe. **Must be smaller than the collider's radius.** |
| `probeDistance` | 1.2 | How far to probe for walls. |
| `maxWallNormalY` | 0.25 | Surfaces tilted more than this are not walls (0 = perfectly vertical). |
| `headOnAngle` | 35° | Within this angle of head-on = climb, otherwise run. |
| `lostContactGrace` | 0.15 | How long the wall may be missing before you fall off or vault. |
| `reattachDelay` | 0.3 | Cooldown after leaving a wall before grabbing again. |
| `normalSmoothing` | 15 | Smooths the wall normal so bumpy walls do not wobble the run. |

**Speed thresholds and modes**

| Field | Default | Purpose |
|---|---|---|
| `minRunSpeed` | 18 | Horizontal speed (and the run key) needed to run or climb. |
| `dropToCrawlSpeed` | 10 | A run or climb slower than this becomes a crawl. |
| `maxRunSpeed` / `runSpeedDecay` | 70 / 0 | Run speed cap, and m/s lost per second (0 = lasts). |
| `climbConversion` | 0.9 | Fraction of horizontal speed turned into upward speed. |
| `maxClimbSpeed` / `climbSpeedDecay` | 45 / 0 | Climb speed cap and decay. |
| `climbSteerSpeed` | 6 | Sideways speed from A/D while climbing. |
| `crawlSpeed` | 9 | Crawl movement speed. |
| `crawlAttachDistance` | 0.7 | How close you must be when walking into a wall to grab on. |
| `crawlApproachDot` | 0.5 | 1 = must face the wall squarely, 0 = any angle. |
| `allowCrawlFromAir` | on | If off, crawling only grabs when you start on the ground. |

**Corners**

| Field | Default | Purpose |
|---|---|---|
| `followCorners` | on | Turn around wall corners while running. |
| `cornerReach` | 2 | How far ahead or around an edge to look. |
| `cornerSteerAcceleration` / `cornerSteerTime` | 1500 / 0.25 | Extra steering right after a corner so the turn stays tight. |
| `cornerCooldown` | 0.6 | Minimum time between two corner turns. |
| `outerCornerConfirm` | 0.06 | The wall must be missing this long before a convex corner is tried (skips window gaps). |
| `cornerHeightCheck` | 1.5 | A wall ahead only counts as a corner if it also exists this far above and below (rejects pillars and balconies). |

**Camera output**

| Field | Default | Purpose |
|---|---|---|
| `cameraFollow` | 1 | How much of the run's turning is handed to the camera. |
| `cameraTurnSharpness` | 8 | How quickly the camera catches up. |
| `cameraDeadzone` | 0.05 | Ignore heading changes smaller than this per physics step. |
| `maxCameraTurnRate` | 240 | Fastest the camera may be turned (degrees per second). |

**Sticking and leaving**

| Field | Default | Purpose |
|---|---|---|
| `stickSpeed` | 3 | Constant speed into the wall, to stay in contact. |
| `steerAcceleration` | 400 | How fast velocity bends toward the wall velocity (m/s per second). |
| `stuckTime` | 0.4 | A blocked run or climb for this long drops you off. |
| `leapAwaySpeed` / `leapUpSpeed` | 14 / 14 | Leap velocity away from and up the wall. |
| `leapMomentumKeep` | 0.8 | Fraction of your along-the-wall speed kept in a leap. |
| `maxLeapUpSpeed` | 28 | Upward speed above this is moved away from the wall. |
| `maxExitSpeed` | 100 | Cap on leap speed. |
| `leapComboGain` / `leapDragRelief` | 0.5 / 0.8 | Rewards for leaping from a run or climb (not from a crawl). |
| `ledgeVaultSpeed` | 8 | Extra push over the top when a climb or crawl runs out of wall. |

### 9.3 Public properties

`IsActive`, `Mode`, `WallNormal`, `RunDirection`, plus `ConsumeYawDelta()` (the camera hook) and `Cancel()` (used by swing/zip/respawn to stop the wall state without touching velocity).

### 9.4 Attaching (`TryAttach`, every physics step when not on a wall)

1. **Skip** if the re-attach cooldown has not passed, or you are swinging or zipping.
2. Measure your horizontal velocity. `fast = speed ≥ minRunSpeed`. **If fast and the run key is not held, do nothing.**
3. **Fast**: probe **ahead** along your velocity direction, with a reach of `probeDistance + speed × dt`. Reaching an extra step ahead means the wall is detected **before** the physics collision eats your speed. If nothing is ahead, probe **both sides** (for running parallel to a wall).
4. **Slow**: only if `allowCrawlFromAir` or you are grounded. Probe in the direction you are **pushing** (camera-relative input) for `crawlAttachDistance`. The wall must be roughly in front of you: the dot product of your input and the wall's inward direction must be at least `crawlApproachDot`.
5. A probe only counts if it is a real wall: not starting inside a collider, and the surface normal within `maxWallNormalY` of horizontal.
6. **Pick the mode**: slow → Crawling. Fast → the angle between your horizontal velocity and the wall's inward direction decides: within `headOnAngle` = Climbing, otherwise Running.
7. **Running on the ground is refused** (that is just running).
8. **Initial speeds**: Running keeps your horizontal speed (capped at `maxRunSpeed`) and the direction projected along the wall. Climbing sets `wallSpeed = min(horizontalSpeed × climbConversion, maxClimbSpeed)`. Crawling starts at 0.
9. Sets the state and calls `PlayerSwinging.BeginZipMomentum()` to take control.

### 9.5 While on the wall (`StepWall`, every physics step)

In order:

1. **Swing or zip took over** → `Cancel()` and stop. Also resets the zip chain (`zip.consecutiveZips = 0`).
2. **Clinging check**: crawl needs the run key **or** any movement input; run and climb need the run key. If not clinging, `EndWall(currentVelocity, 0, 0)`: you leave carrying your current velocity.
3. **Inner corner check** (running only).
4. **Re-probe** straight into the wall. If found, the wall normal is smoothly updated (`Slerp` with `normalSmoothing`) and the lost-contact timer resets. If missing, the lost-contact timer runs, and a convex (outer) corner is tried once the wall has been missing `outerCornerConfirm`. If still missing past `lostContactGrace`: you leave. If climbing or crawling upward (`velocity.y > 1`) you also get `ledgeVaultSpeed` of push away from the wall's former surface, over the edge.
5. **Wall-plane basis**: `wallRight = cross(wallNormal, up)` (your right when facing the wall) and `wallUp = cross(wallRight, wallNormal)` (up along the wall).
6. **Target velocity by mode**:
   - Running: `runDir × wallSpeed`, with `runDir` re-projected onto the (possibly curved) wall each step. Speed decays by `runSpeedDecay`.
   - Climbing: `wallUp × wallSpeed + wallRight × (input.x × climbSteerSpeed)`. Speed decays by `climbSpeedDecay`.
   - Crawling: `(wallUp × input.y + wallRight × input.x) × crawlSpeed`.
   - If a run or climb decays below `dropToCrawlSpeed`, it becomes a crawl.
7. **Stuck check** (run and climb, after the first 0.3 s): if your actual speed along the intended direction stays below 20% of `wallSpeed` for `stuckTime`, you are blocked (ceiling, unfollowable corner) and you let go.
8. **Ground check**: grounded, more than 0.2 s after attaching, and not rising faster than 0.5 m/s → `EndWallOnGround()` (you land with momentum).
9. **Apply**: subtract `stickSpeed` into the wall from the target, then bend velocity toward the target with `MoveTowards` at `steerAcceleration` (or `cornerSteerAcceleration` for `cornerSteerTime` after a corner). Cancel gravity.

### 9.6 Following corners

Only **running** follows corners.

- **Inner (concave) corner**: a wall right ahead of the run. The probe looks `cornerReach + wallSpeed × 0.1` ahead. It only counts if the face is turned toward you and the **same face also exists 1.5 m above and below** (so pillars and balconies are ignored). You then turn onto the new wall, heading **away from the old wall**.
- **Outer (convex) corner**: the wall ended. After the wall has been gone `outerCornerConfirm`, it casts back from behind the old wall's plane toward the run direction to find the face that continues around the edge. The face must point roughly the way you are heading. You then wrap **around** the edge.
- **Cooldown** (`cornerCooldown`, 0.6 s) stops two corners being taken in quick succession, and a short burst of extra steering keeps turns tight at speed.
- Gradual curves need no special handling: the smoothed wall normal simply rotates and the run direction follows.

### 9.7 Leaving the wall

| Way out | What you carry out |
|---|---|
| **Release the run key** (run/climb) or let go of everything (crawl) | Your current velocity. |
| **Lost contact** (wall ended) | Current velocity, plus the vault push if you were climbing or crawling upward. |
| **Stuck** (blocked) | Current velocity. |
| **Reached the ground** | Handled by `EndWallOnGround`: landing carry, combo reset. |
| **Jump (leap)** | See below. |
| **Swing or zip pressed** | The wall state is cancelled; the new ability takes over. |

**Leap**: `exit = along × leapMomentumKeep + wallNormal × leapAwaySpeed + up × leapUpSpeed`, where `along` is your velocity projected onto the wall plane. If the upward component exceeds `maxLeapUpSpeed` (28), the excess is moved horizontally **away from the wall**, so a fast climb does not become a vertical rocket. The result is clamped to `maxExitSpeed`. Leaping from a run or climb earns `leapComboGain` and `leapDragRelief`; from a crawl it earns nothing. `jumpConsumed` is set so the press does not also trigger an air jump.

All exits except landing finish through `PlayerSwinging.EndZip`, so you get the air-control window, the raised speed cap and a refilled air jump.

### 9.8 The camera hook

While running, the script measures how much your **run direction rotates** each physics step (around corners or along curves), ignores changes below `cameraDeadzone`, and stores the total as a pending yaw. `ConsumeYawDelta()` hands out that pending yaw smoothly (an exponential ease limited to `maxCameraTurnRate`). `CameraEffects` calls it every frame and adds the result to the Cinemachine camera's horizontal axis, so the camera turns with the player.

---

## 10. Camera scripts

Three small scripts cooperate with a Cinemachine 3 camera. They write to **different** things, so they do not fight each other:

| Script | What it controls |
|---|---|
| `PlayerCamera` | The `orientation` transform and the player model's facing (not the camera itself). |
| `CameraEffects` | The Orbital Follow's horizontal and vertical **axis values** (turn and tilt). |
| `CameraSmoothing` | The camera **lens FOV** and the Orbital Follow **radius** (zoom). |

### 10.1 PlayerCamera

**Fields:** `inputActions`, `lookAction` (assigned in `Awake`), `orientation`, `player`, `playerObj`, `rb` (unused), `rotationSpeed`, `wallRun`.

- **Start**: locks and hides the cursor (which is also what makes `WebZip` aim from the screen centre).
- **Update**, step by step:
  1. **Orientation**: `orientation.forward` is set to the horizontal direction from the camera to the player. This is what `PlayerMovement` uses to make movement camera-relative.
  2. **Model facing**: builds a direction from `orientation` and the **Look** action's value, and if it is non-zero, slerps `playerObj.forward` toward it at `rotationSpeed`.
  3. **Wall facing**: if `WallRun.IsActive`: facing along the run direction while running, or into the wall (`-WallNormal`) while climbing or crawling, again slerping at `rotationSpeed`.
- `OnEnable`/`OnDisable` enable and disable the Player action map.

> Step 2 reads the **Look** action (mouse or stick movement), not the Move action, so the model turns toward where the *view* is moving, not where you are walking. Step 3 then runs in the same frame, so during a wall run the model blends the two. See quirks.

### 10.2 CameraEffects

**Fields:** `wallRun`, `swinging`, `orbital` (the Orbital Follow), `yawDirection` and `pitchDirection` (flip to -1 if an effect goes the wrong way), `diveTiltDown` (12°), `climbTiltUp` (0 = off), `tiltSharpness` (6).

Every frame (`Update`):

1. **Pitch target**: `+diveTiltDown` while `swinging.IsDiving`; `-climbTiltUp` while `WallRun.Mode == Climbing`.
2. **Eased offset**: the applied tilt moves toward the target with `1 - exp(-tiltSharpness × dt)`.
3. **Apply the change, not the value**: only the frame-to-frame **difference** in tilt is added to the Orbital Follow's vertical axis (clamped to its range). The player's own mouse look is never overwritten, and when the tilt returns to zero the camera returns to where it was.
4. **Yaw**: `WallRun.ConsumeYawDelta()` is added to the horizontal axis, wrapped inside the axis's range.

Also, on `OnEnable` it subscribes to `TimeTrialManager.PlayerTeleported`. When the player is teleported (respawn or retry), it calls `CinemachineCore.OnTargetObjectWarped` so the camera **jumps with the player** instead of gliding across the map.

### 10.3 CameraSmoothing

**Fields:** `camera` (CinemachineCamera), `orbitalFollow`, `target` (Rigidbody), `maxSpeed` (48), `minFov`/`maxFov` (75/100), `minRadius`/`maxRadius` (5/8), `smoothing` (5).

Every `LateUpdate`:

```
speedPercent = clamp01( |target velocity| / maxSpeed )       (full 3D speed)
targetFov    = lerp(minFov,    maxFov,    speedPercent)
targetRadius = lerp(minRadius, maxRadius, speedPercent)
step         = 1 - exp(-smoothing × dt)
FOV    = lerp(FOV,    targetFov,    step)
Radius = lerp(Radius, targetRadius, step)
```

The faster you go, the wider the view and the farther the camera sits, with a frame-rate-independent ease. At 48 m/s the effect is at its maximum.

### 10.4 How the camera pieces cooperate

- Movement direction comes from **`PlayerCamera.orientation`**, which is derived from the camera's position, so it is always correct after `CameraEffects` turns the camera.
- `CameraEffects` changes the **axes**; `CameraSmoothing` changes the **lens and radius**. Neither touches the other's properties.
- Set the Cinemachine **Tracking Target** to the player root. The model rotates quickly around corners; a camera tracking the model would inherit that.

---

## 11. SpeedometerUI

**File:** `UIManager.cs` (class `SpeedometerUI`) | **Lives on:** a UI object.

A simple speed readout and a speed-lines particle effect.

| Field | Default | Purpose |
|---|---|---|
| `playerRb` | none | The player's Rigidbody. |
| `speedText` | none | A `TextMeshProUGUI`. |
| `speedLinesParticle` | none | A ParticleSystem for speed lines. |
| `speedMultiplier` / `speedUnit` | 3.6 / "km/h" | Converts m/s to the displayed unit. |
| `minSpeedToShowLines` | 15 | Speed at which lines begin. |
| `maxSpeed` | 30 | Speed at which lines reach full density. |
| `maxEmissionRate` | 150 | Particles per second at full density. |
| `normalColor` / `maxSpeedColor` | white / red | **Declared but not used.** |

Every frame (`Update`):

```
currentSpeed    = horizontal speed of the Rigidbody (vertical speed ignored)
speedPercentage = InverseLerp(minSpeedToShowLines, maxSpeed, currentSpeed)
emission rate   = speedPercentage × maxEmissionRate
text            = round(currentSpeed × speedMultiplier) + " " + speedUnit
```

Notes: the readout is **horizontal** speed only, whereas the time trial's "top speed" is the full 3D speed, so the two can differ during dives and climbs. Lines hit full density at 30 m/s horizontal, well below swing speeds (35 to 80 m/s), so in practice the lines are at maximum for most of a swing. Raise `maxSpeed` for a more gradual effect.

---

## 12. The time trial system

Six scripts make up the time trials. You only add two of them to the scene yourself: `TimeTrialManager` (one) and `TimeTrialStarter` (one per trial). The other four are created at runtime.

```
TimeTrialStarter  ──touch──▶  TimeTrialManager  ──creates──▶  TimeTrialUI
 (world item, holds             (state machine,               RaceNavigationArrow
  the course + settings)         timer, hoops, saving)         RaceHoopVisual (one per hoop)
                                        │
                                        └── uses RaceModelUtils (strip colliders, measure, tint)
```

### 12.1 How a race flows

```
Idle ──touch starter──▶ Prompt ──Yes──▶ Countdown ──GO──▶ Racing ──last hoop──▶ Results ──Close──▶ Idle
                          │                │                │                       │
                          └─No / leave─▶ Idle ◀──Abort──────┘                       └─Retry──▶ Countdown
                                                              └─time limit hit──▶ Results (failed)
```

| State | What the player sees | Keys |
|---|---|---|
| **Idle** | Nothing. | n/a |
| **Prompt** | Panel: trial name, best time, medal times, rules, "Start the race?" | **E** Yes, **Q** No (or walk away) |
| **Countdown** | Big 3, 2, 1 (default 3 seconds). Hoops are built. The player is **not** frozen. | **Q** abort |
| **Racing** | Timer, hoop counter, distance, "time left" (if limited), arrow, hoops. "GO!" flashes for 0.7 s. | **R** respawn, **Q** abort |
| **Results** | Time, best, medal, top speed, respawn summary, next goal (or the failure screen). | **E** retry, **Q** close |

### 12.2 Setup

**The manager**

1. Create an empty GameObject, add `TimeTrialManager`. Only one per scene; a second destroys itself.
2. Assign a **Hoop Prefab** (required, see below). The player is found automatically through `PlayerSwinging`. The UI and arrow are created automatically.
3. Optional: **Arrow Prefab**, scales and heights, key bindings, countdown length.

**A trial**

1. Create an empty GameObject where the trial begins, add `TimeTrialStarter`. A Sphere Collider (trigger) is added automatically. Give it a unique **Trial Id**.
2. Create empty transforms for the hoops, one per hoop, and drag them into **Hoop Points** in order. Rotate each so its **blue (forward) arrow points the way through the hoop**. Scale a point to resize that hoop.
3. Set **Hoop Radius**, the medal times (gold < silver < bronze), and optionally **Time Limit**, respawn settings and a marker prefab.

**Your models (prefabs)**

Wrap each model in an empty parent and rotate the model inside it until the parent's blue (forward) arrow lines up with what the code expects:

| Prefab | Expected orientation | Notes |
|---|---|---|
| Hoop | Ring lies in the **XY plane**, opening along **+Z**, centred on the parent's origin | Scaled automatically so its size matches the hoop's radius. |
| Arrow | Points along **+Z**, centred on the parent's origin | About 2 m long at scale 1 is a good size. |
| Starter marker | Anything | Appears `Marker Height` above the starter object. |

Colliders on spawned models are removed automatically.

### 12.3 TimeTrialManager

**Fields**

| Field | Default | Purpose |
|---|---|---|
| `playerRb` | auto | The player's Rigidbody. |
| `ui` | auto | The UI (created if empty). |
| `confirmKey` / `cancelKey` / `respawnKey` | E / Q / R | Used when the `TrialConfirm` / `TrialCancel` / `TrialRespawn` actions do not exist. |
| `countdownSeconds` | 3 | Countdown length; 0 starts instantly. |
| `passPadding` | 0.6 | Extra distance around a hoop that still counts. |
| `hoopPrefab` | none | **Required.** Your hoop model. |
| `hoopPrefabRadius` | 0 | Radius of the hoop's **opening** in the prefab's own units. 0 = measure the model's outer radius automatically. |
| `tintHoopModel` / `customTintProperty` | off / empty | Tint hoops by state (grey = upcoming, cyan = next, green = passed). Needs a shader with `_BaseColor`, `_Color` or your named property. |
| `arrowPrefab` / `arrowScale` / `arrowHeight` | none / 1.1 / 2.8 | The arrow model, its size and its height above the player. |
| `respawnCooldown` | 1 | Minimum time between respawns. |
| `respawnForwardOffset` | 2 | How far past the hoop you reappear. |
| `urgentSeconds` | 10 | The "time left" line turns red below this. |

**Startup (`Awake`)**: becomes the singleton (`TimeTrialManager.Instance`), finds the player (logging an error if none), finds or creates the UI, creates the arrow, warns if no hoop prefab is set, and looks up the optional input actions.

**The prompt.** When the player enters a starter's trigger and the manager is Idle, it shows the prompt. Leaving the trigger (while the prompt is up) closes it. The prompt text lists the best time, the medal times, the time limit if any, and the respawn rule.

**Starting a race (`StartRace`).** It refuses (with a message) if the starter has no hoops or there is no hoop prefab. Otherwise it builds the hoops, resets the counters (next hoop, top speed, penalties, respawns), reads the starter's time limit, shows the HUD with the controls hint and enters the countdown. When the countdown ends, `BeginRacing` records the start time and the current position, flashes "GO!", and lights up the first hoop.

**Hoop detection: a swept plane test.** Hoops are not trigger colliders, because at 80 to 100 m/s you travel about 2 m per physics step and a thin trigger would be skipped. Every physics step, while racing, the manager tests whether the **straight line from your previous position to your current position** crossed the *next* hoop's disc:

```
normal = hoop.rotation × forward
d0 = dot(previousPosition - hoopCentre, normal)
d1 = dot(currentPosition  - hoopCentre, normal)
if d0 and d1 have the same sign        → did not cross the plane
otherwise crossing = lerp(previous, current, d0 / (d0 - d1))
passed if  distance(crossing, hoopCentre) ≤ hoopRadius + passPadding
```

This works at any speed, in either direction. Only the **next** hoop in order is tested, so flying through hoop 5 early does nothing, and crossing the plane outside the ring does nothing.

**Hoop states.** When a hoop is passed it plays its "collected" animation and the next one becomes active. At any moment the **next** hoop is Active, the one after is a faint Preview, later ones are hidden, earlier ones are Collected.

**Timer and top speed.**
- Race time = `Time.time - raceStartTime + penaltyTotal`. The final time is taken at the physics step where the last hoop is crossed, so its precision is one fixed timestep (0.02 s by default).
- Top speed = the highest full **3D** speed (`linearVelocity.magnitude`) measured every physics step. It is displayed in m/s and km/h.

**Medals and saving.** On finishing:
1. Medal: time ≤ gold = Gold, ≤ silver = Silver, ≤ bronze = Bronze, else none.
2. **Best time** is saved to `PlayerPrefs` under `TimeTrial_BestTime_<trialId>` only if it is faster (or none exists). That is a **NEW RECORD**.
3. **Best medal** is saved under `TimeTrial_BestMedal_<trialId>` only if it is an upgrade.
4. The results screen shows time, best, record flag, top speed, respawn summary, the medal icon and name, and what you need for the next medal. The starter's marker refreshes its medal tint.

Times are formatted `m:ss.mmm` with a dot regardless of the computer's language settings.

**Checkpoint respawn.**
- Every hoop you pass is a checkpoint. Press the respawn key (R) during a race, subject to the starter's `allowRespawn` and the manager's `respawnCooldown`.
- **Before the first hoop** you return to the starter's `StartRespawnPosition` (its `Start Respawn Point` if assigned, otherwise the starter object plus 1.1 m up) with zero velocity.
- **After a hoop** you appear `respawnForwardOffset` metres past that hoop, in the direction you flew through it, launched at the starter's `respawnSpeed` (20 m/s) along that direction.
- Each respawn adds the starter's `respawnPenalty` (3 s) to your time, which also counts against the time limit. A toast shows, for example, "RESPAWNED AT HOOP 3 +3 s".

**What a teleport does (`TeleportPlayer`).** It cancels the wall run and zip, calls `PlayerSwinging.ResetForRespawn(velocity)` (cancels the swing and dive, zeros the speed cap and combo, hands over a clean airborne state), sets the Rigidbody and transform position, zeroes angular velocity, syncs physics transforms, restarts the hoop sweep from the new position (so the jump itself can never count as passing a hoop), and raises the `PlayerTeleported` event so the camera warps with you.

**Time limit.** If the starter's `timeLimit` is above 0, a "TIME LEFT" line shows under the timer and turns red in the last `urgentSeconds`. When the elapsed time (including penalties) reaches the limit, the race ends on a **TIME'S UP** screen showing hoops reached, top speed and respawns. No time or medal is saved. Crossing the last hoop **after** the limit has already passed also counts as a failure.

**Abort, retry, close.**
- **Q during countdown or race**: aborts, hides everything, removes hoops, saves nothing.
- **E on the results screen** (retry): **teleports the player to the starter's position with zero velocity**, then restarts the race.
- **Q on the results screen**: closes it. The starter shows its prompt again only after you leave its trigger and re-enter it.

### 12.4 TimeTrialStarter

**Fields**

| Group | Field | Default | Purpose |
|---|---|---|---|
| Trial | `trialId` | trial_01 | **Unique** save key. |
| | `trialName` | Rooftop Run | Title in the prompt and results. |
| Course | `hoopPoints` | none | The hoops, in order. |
| | `hoopRadius` | 6 | Radius of an unscaled hoop (point scale multiplies it). |
| Medals | `goldTime` / `silverTime` / `bronzeTime` | 40 / 55 / 75 | Thresholds in seconds. Gold must be the smallest. |
| Time limit | `timeLimit` | 0 | Seconds allowed; 0 = no limit. |
| Respawn | `allowRespawn` | on | Allows the respawn key. |
| | `respawnPenalty` | 3 | Seconds added per respawn. |
| | `respawnSpeed` | 20 | Launch speed when respawning at a hoop. |
| | `startRespawnPoint` | none | Where you reappear before the first hoop. |
| Interaction | `triggerRadius` | 3 | How close the player must be to get the prompt. |
| Marker | `markerPrefab` | none | Your model for the starter item. |
| | `markerScale` / `markerHeight` | 1 / 1.8 | Size and height above the object. |
| | `markerSpinSpeed` / `markerBobAmount` | 70 / 0.15 | Spin (degrees per second) and bob (metres). Set to 0 to stop. |
| | `tintCustomMarker` | off | Tint the marker with the colour of your best medal. |

**Behaviour.** In `Awake` it forces its Sphere Collider to be a trigger with `triggerRadius`, and builds the marker if a prefab is assigned. `OnTriggerEnter`/`Exit` detect the player by checking whether the entering collider's parents contain a `PlayerSwinging`, then notify the manager. No tags are needed. For the trigger to fire, the layers of the starter and the player must be allowed to collide in the Physics Layer Collision Matrix.

**Editor gizmos.** In the Scene view each hoop is drawn as a numbered circle at its true radius, with a line from the starter through every hoop in order and a short line showing the direction through each hoop.

### 12.5 TimeTrialUI

Built entirely in code on a Screen Space Overlay canvas (sort order 50, 1920×1080 reference, scale with screen size). It uses the legacy `Text` component and Unity's built-in `LegacyRuntime.ttf` font, with a soft drop shadow on all text.

| Element | Position | Purpose |
|---|---|---|
| Prompt panel | Centre, lower | Trial name, best time, medal times, rules, keys. |
| Timer | Top centre, large | Current race time (including penalties). |
| Info line | Under the timer | "HOOP 3/8 \| 142 m". |
| Limit line | Under the info line | "TIME LEFT 0:42.100" (red when urgent). |
| Toast | Under the limit line | Short messages such as respawns (shown 1.5 s). |
| Hint | Bottom centre | Control reminders. |
| Countdown | Centre, huge | 3, 2, 1 and a brief "GO!". |
| Results panel | Centre | Title, medal icon, medal name, time, best, record flag, top speed, detail, next goal, keys. |

**Public methods:** `ShowPrompt`, `HidePrompt`, `ShowHud`, `SetHud`, `SetLimit`, `ShowToast`, `ShowCountdown`, `FlashMessage`, `HideCountdown`, `ShowResults`, `ShowFailed`, `HideResults`, `HideAll`.

The medal icon is a circle sprite generated in code, tinted gold, silver or bronze (grey for no medal or a failure). `SetHud` still accepts a speed string but ignores it: the speed is no longer displayed in this HUD (see `SpeedometerUI` for the speedometer).

### 12.6 RaceHoopVisual

Created by the manager once per hoop. It creates a rotated "holder" child, instantiates your hoop prefab inside it, strips its colliders, and **scales** the model by `hoopRadius / measuredRadius`, where the measured radius is either your `hoopPrefabRadius` or the model's own outer radius (found by transforming every mesh's bounding-box corners into the holder's space and taking the larger of the X and Y extents).

States (scaling the holder, since most shaders cannot fade):

| State | Look |
|---|---|
| Hidden | The object is disabled. |
| Preview | 85% size. |
| Active | Gentle pulse: `1 + 0.04 × sin(time × 6)`. |
| Collected | Swell then shrink to nothing over 0.35 s, then the object destroys itself. |

If `tintHoopModel` is on, each state also tints the model through a `MaterialPropertyBlock` (grey for Preview, cyan for Active, green for Collected).

### 12.7 RaceNavigationArrow

Created by the manager. If no arrow prefab is assigned, **nothing is shown**. Otherwise, while a race is running it floats `arrowHeight` above the player (with a gentle bob), and smoothly rotates (`1 - exp(-14 × dt)`) to point from itself at the centre of the next hoop, **including up and down**. It updates in `LateUpdate` so it follows the player's interpolated position, and hides when the race ends.

### 12.8 RaceModelUtils

Three static helpers: `StripColliders(root)` (destroys every collider so models cannot block or trigger anything), `MeasureRadius(root)` (the outer radius of a ring-shaped model in the XY plane, or 1 if nothing could be measured) and `Tint(renderers, colour, extraProperty)` (sets `_BaseColor`, `_Color` and optionally your own property via a property block).

### 12.9 Resetting saved data

Use **Edit > Clear All PlayerPrefs** in the Unity editor, or delete the keys `TimeTrial_BestTime_<trialId>` and `TimeTrial_BestMedal_<trialId>` from code. `PlayerPrefs` is stored on the player's machine and can be edited by them.

### 12.10 Designing a good course

- At top speed, hoops **60 to 100 m apart** with a radius of **6 to 8 m** feel good.
- Use each ability: a hoop at a roof lip for wall running and vaulting, one across a gap for zipping, a low one between buildings for a swing release, one below the player for a dive.
- Aim for **8 to 15 hoops** and **40 to 70 seconds**.
- Keep hoops in open air (a respawn that lands inside geometry will trap the player) and put the starter where the player has room to build speed.
- Choose the medal times by playing: gold a little slower than your best, silver about 25 to 30% slower than gold, bronze about 60% slower.

---

## 13. End-to-end walkthroughs

These follow the code step by step through common situations, showing which script is in control at each moment.

### 13.1 A swing, a release and a re-swing

| Moment | In control | What happens |
|---|---|---|
| You press and hold **Swing** | `PlayerSwinging.Update` | `wantsSwing = true`. The retry loop calls `StartSwing()` every 0.1 s until an anchor is found. |
| An anchor is found | `StartSwing` | Anchor chosen by the scored cast grid. A `SpringJoint` is added. `isSwinging` and `swingMomentum` become true, so `PlayerMovement` goes quiet. The zip chain resets. |
| You swing | `SwingPhysics` | The rope reels in, extra gravity and steering apply, and speed is capped at `EffectiveMaxSpeed`. |
| You **release Swing** (plain) | `ReleaseWeb(false)` | The joint is destroyed. If the release was good (long enough, rising, fast enough) you get a little combo and +1.5 m/s. Upward speed is limited, speed is clamped to the burst cap, the 1.2 s air-control window starts and your air jump refills. |
| *Or* you **press Jump** | `ReleaseWeb(true)` | A swing jump. Timing is scored by speed and launch angle. A good one gives combo, +12 m/s at most and drag relief. |
| In flight | `PlayerSwinging.FixedUpdate` | You can steer for 1.2 s, with extra gravity and drag pulling you down and slowing you. |
| You press **Jump** in the air | `AirJump` | Vertical speed becomes at least 11 m/s, horizontal speed drops 15%. |
| You hold **Swing** again near a building | `StartSwing` | A new swing begins. The raised speed cap from your last release lets the new swing start fast. |
| You touch the ground | `LandWithMomentum` | You keep 80% of horizontal speed, slide with low drag for 1.5 s, and the combo resets to 0. |

### 13.2 Zipping, and why chained zips get weaker

1. You press **Zip** while aiming at a building. `WebZip` finds a target, increments `consecutiveZips` to 1 and takes control (cancelling any swing).
2. The pull ramps from at least 45 m/s up to 90 m/s while bending your velocity toward the target.
3. You are now in the 1.2 s air-control window (`swingMomentum` is still true). If you zip again now, the counter becomes 2 and the exit speed is **halved**; a third zip, divided by 3.
4. The counter returns to 0 when you swing, wall-run, or stay in ordinary movement (the window ends, or you land and `PlayerMovement` runs again).

### 13.3 Wall run, vault, land

1. You sprint at a wall in the air at an angle with Sprint held. `WallRun.TryAttach` sees the wall a step early (so no speed is lost), picks **Running**, and calls `BeginZipMomentum`. `PlayerSwinging` now returns early (`Busy`).
2. Each step, `StepWall` re-probes the wall, follows corners, and bends your velocity toward the run direction. The camera is turned by `ConsumeYawDelta`.
3. Sprint a wall head-on instead and you **climb**. At the top the wall disappears; after the lost-contact grace, `EndWall` gives you your velocity plus `ledgeVaultSpeed` (8 m/s) over the edge, through `EndZip`.
4. You are in the air-control window with one air jump. When you touch the roof, `LandWithMomentum` applies the landing carry.
5. Press Jump on the wall instead and you **leap** away, keeping most of your along-the-wall speed.

### 13.4 Dive into a swing

1. In the air, hold **Dive**: your vertical speed snaps to at least 10 m/s down, then accelerates to a maximum of 70 m/s down.
2. While you hold it, the combo window stays open. Release it and you have 0.5 s to swing and still get the bonus.
3. Press **Swing** during or just after the dive. The anchor is found and `ApplyDiveConversion` converts the speed you were moving **away from the anchor** into speed along the arc (up to 90% for a dive of 1.2 s or longer). The speed cap is raised so the boost survives, then decays at 12 m/s per second.

### 13.5 A complete time trial

1. You touch the starter's trigger. The manager shows the prompt. You press **E**.
2. Hoops are built, the countdown runs (you can pick up speed), then "GO!" and the timer starts.
3. The first hoop pulses cyan, the arrow points at it, the HUD shows distance. Every physics step the manager checks whether your last step crossed the hoop's disc.
4. You miss a hoop and fall: press **R**. You return past the last hoop you passed, moving at 20 m/s, 3 s are added to your time, and the camera jumps with you.
5. You cross the last hoop. The time, medal, top speed and best are shown. Press **E** to retry (you are teleported to the starter) or **Q** to close.

---

## 14. Tuning guide

| I want... | Change |
|---|---|
| Swings to feel faster | Raise `baseMaxSpeed`/`topMaxSpeed`, `extraGravity`, `reelSpeed`. |
| A stiffer, less bouncy rope | Raise `spring` (hundreds), or scale it with mass; raise `damper`. |
| Easier anchor finding | Raise `anchorSphereRadius`, add more `pitchAngles`/`yawOffsets`, lower `minAnchorHeight`. |
| Anchors chosen higher/farther | Raise `idealPitch` / `idealDistance`. |
| A bigger reward for good swing jumps | Raise `jumpSpeedBoost`, `jumpComboGain`, `jumpDragRelief`. |
| A wider, more forgiving swing-jump timing | Raise `jumpAngleWindow`, lower `minJumpSpeedRatio`. |
| Speed to bleed off more slowly in the air | Lower `airDrag` and `airDragQuadratic` (in **both** `PlayerSwinging` and `PlayerMovement`). |
| More steering after a release | Raise `airControlDuration`, `steerDegreesPerSecond`, `airAcceleration`. |
| Longer combos | Raise `comboWindow`, `maxComboLevel`. |
| A stronger dive | Raise `diveSwingConversion`, `maxDiveSwingSpeed`, `maxDiveSpeed`. |
| A snappier zip | Raise `zipStartSpeed`, `steerAcceleration`; lower `zipAcceleration` for a softer start. |
| A bigger zip launch | Raise `exitSpeedBonus`, `maxExitSpeed`. |
| Zips to chain better or worse | Edit the division in `WebZip.FinishZip` (see 8.7). |
| Easier wall attach | Raise `probeDistance`; lower `minRunSpeed` and `headOnAngle` as needed. |
| Wall runs to run out | Set `runSpeedDecay` / `climbSpeedDecay` above 0 (5 to 10 is gentle). |
| A bigger wall leap | Raise `leapAwaySpeed`, `leapUpSpeed`, `maxLeapUpSpeed`. |
| More slide on landing | Raise `landingSpeedKeep`, `carryDuration`; lower `carryDragRatio`. |
| A calmer camera | Lower `CameraSmoothing.smoothing`, raise `minRadius`; reduce `diveTiltDown`. |
| A less dramatic zoom | Narrow `minFov`/`maxFov` and `minRadius`/`maxRadius`. |
| Easier trial hoops | Raise `hoopRadius` or `passPadding`; space hoops closer. |

---

## 15. Public API quick reference

### PlayerMovement
`IsGrounded`, `CarrySpeed`, `currentState`, `isSwinging`, `swingMomentum`, `jumpConsumed`, `BeginLandingCarry()`.

### PlayerSwinging
`ComboLevel`, `ComboT`, `CurrentMaxSpeed`, `LastJumpTiming`, `AirJumpsRemaining`, `IsDiving`, `CancelSwingForZip()`, `BeginZipMomentum()`, `EndZip(Vector3, float, float)`, `EndWallOnGround()`, `ResetForRespawn(Vector3)`, `ApplyAirGravity()`, `ApplyAirDrag()`.

### WebZip
`IsZipping`, `consecutiveZips`, `CancelForRespawn()`.

### WallRun
`IsActive`, `Mode` (`None`, `Running`, `Climbing`, `Crawling`), `WallNormal`, `RunDirection`, `ConsumeYawDelta()`, `Cancel()`.

### TimeTrialManager
`Instance`, `CurrentState`, `PlayerTeleported` (static event: transform that moved, distance moved), `OnPlayerEnteredStarter(starter)`, `OnPlayerLeftStarter(starter)`, static `BestTimeKey`, `BestMedalKey`, `MedalColor`, `FormatTime`.

### TimeTrialStarter
`HasValidCourse`, `StartRespawnPosition`, `GetMedal(float)`, `RefreshMarker()`.

### TimeTrialUI
`ShowPrompt`, `HidePrompt`, `ShowHud`, `SetHud`, `SetLimit`, `ShowToast`, `ShowCountdown`, `FlashMessage`, `HideCountdown`, `ShowResults`, `ShowFailed`, `HideResults`, `HideAll`.

### RaceHoopVisual / RaceNavigationArrow / RaceModelUtils
`RaceHoopVisual.Init(...)`, `SetState(...)`, `StateNow`; `RaceNavigationArrow.Setup(...)`, `SetTarget(...)`, `Clear()`; `RaceModelUtils.StripColliders`, `MeasureRadius`, `Tint`.

---

## 16. Appendix: formulas

**Combo and power** (`PlayerSwinging`)
```
ComboT          = comboLevel / maxComboLevel
CurrentMaxSpeed = lerp(baseMaxSpeed, topMaxSpeed, ComboT)
EffectiveMax    = max(CurrentMaxSpeed, swingSpeedCap)
BurstMax        = EffectiveMax × burstMultiplier
Power           = lerp(1, maxPowerMultiplier, ComboT)
```

**Anchor score** (lower is better)
```
|yaw - idealYaw| × 0.5 + |pitch - idealPitch| × 0.3 + |distance - idealDistance| × 0.4 - bonus
```

**Swing jump timing**
```
angleFactor = 1 - clamp01(|elevation - idealJumpAngle| / jumpAngleWindow)
speedFactor = InverseLerp(minJumpSpeedRatio, fullJumpSpeedRatio, speed / CurrentMaxSpeed)
timing      = angleFactor × speedFactor
```

**Air drag** (both scripts use the same shape)
```
dragRate  = airDrag + airDragQuadratic × horizontalSpeed
speed    *= 1 - clamp01(dragRate × dt)
deceleration ≈ 0.3·v + 0.025·v²   (16 at 20 m/s, 52 at 40, 184 at 80)
```
PlayerSwinging's version also multiplies `dragRate` by `1 - jumpDragRelief × reliefStrength × reliefFade`.

**Landing carry** (`PlayerMovement`)
```
kept horizontal speed = horizontal speed × landingSpeedKeep
speed cap  = max(moveSpeed, lerp(moveSpeed, carryCap, carryTimer / carryDuration))
ground drag = lerp(groundDrag × carryDragRatio, groundDrag, t²),   t = 1 - carryTimer / carryDuration
```

**Dive conversion**
```
scale   = InverseLerp(minDiveTime, fullDiveTime, diveDuration)
outward = max(0, dot(velocity, -ropeDirection))
new     = (velocity with the rope-direction part removed) + arcDirection × outward × diveSwingConversion × scale
```

**Zip**
```
zipSpeed  = max(zipStartSpeed, dot(velocity, launchDir)),  then + zipAcceleration × dt up to zipTopSpeed
velocity  = MoveTowards(velocity, direction × zipSpeed, steerAcceleration × dt)
exit      = direction(tilted up by exitUpBias) × min(speed + exitSpeedBonus, maxExitSpeed) / consecutiveZips
```

**Wall basis and leap**
```
wallRight = cross(wallNormal, up)
wallUp    = cross(wallRight, wallNormal)
leap exit = along-wall velocity × leapMomentumKeep + wallNormal × leapAwaySpeed + up × leapUpSpeed
```

**Hoop crossing** (see 12.3)
```
crossing = lerp(previous, current, d0 / (d0 - d1));  passed if distance(crossing, hoopCentre) ≤ radius + passPadding
```

**Camera**
```
CameraSmoothing: FOV/Radius = lerp(current, target, 1 - exp(-smoothing × dt))
CameraEffects:   tilt moves with the same exponential ease; only the change per frame is applied to the axis
```

---

*End of README.*
