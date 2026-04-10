# MicroVride V2

A VR micromobility simulator for research on electric ride-on vehicles (e-scooter, Segway, electric unicycle, skateboard/OneWheel). Built in Unity for the Meta Quest headset. Physical hardware sensors (IMU + foot pressure boards via ESP32) drive movement in VR.

This prototype was designed for human factors research. It produces structured log files for each ride session that can be analysed offline.

---

## Table of Contents

1. [What the prototype does](#what-the-prototype-does)
2. [Hardware requirements](#hardware-requirements)
3. [Scene flow](#scene-flow)
4. [Architecture overview](#architecture-overview)
5. [Core scripts reference](#core-scripts-reference)
6. [Sensor data pipeline](#sensor-data-pipeline)
7. [Vehicle controllers](#vehicle-controllers)
8. [Coin & difficulty system](#coin--difficulty-system)
9. [Data logging](#data-logging)
10. [Inspector parameters you will want to tune](#inspector-parameters-you-will-want-to-tune)
11. [How to extend the prototype](#how-to-extend-the-prototype)
12. [Project setup checklist](#project-setup-checklist)

---

## What the prototype does

The participant puts on a Quest headset and stands on one of the physical boards/vehicles. They see a VR street environment and collect coins by riding through them. Their real physical movements (lean, steer, throttle) are captured by sensors on the physical device and streamed wirelessly to Unity over UDP. The simulation physics engine (BikeLab Segway asset) translates those inputs into vehicle motion in VR.

At the end of a run the prototype writes four CSV/JSONL log files to the headset's Documents folder that record every telemetry sample, every coin collected, every collision, and a session summary.

---

## Hardware requirements

| Component | Purpose | Notes |
|-----------|---------|-------|
| Meta Quest 2/3 | VR headset + compute | Android build target |
| ESP32 (×2 or more) | Foot pressure boards | Left board IP `172.20.10.2`, right `172.20.10.3` |
| IMU on vehicle | Pitch / Yaw / Roll | Streamed as UDP on port 1235 |
| Throttle sensor | Forward input (scooter) | Streamed as UDP on port 4210 |
| Wi-Fi access point | Local LAN between headset and ESP32s | Hotspot from phone works |

**UDP ports used by the prototype** (configured on the `InputHandler` and `SensorManager` GameObjects in the Start scene):

| Port | Component | Data |
|------|-----------|------|
| 4210 | `VehicleDataReceiver` on `SensorManager` | Throttle (float 0–1) |
| 1235 | `VehicleDataReceiver` on `SensorManager` | IMU — `imu-pitch=X&imu-roll=Y&imu-yaw=Z` |
| 1234 | `FootSensorInput` on `InputHandler` | Foot sensors — `left_heel_norm=X&left_toe_norm=Y&…` |

All packets are plain UTF-8 text using the format `key=value&key=value`.

---

## Scene flow

```
Start scene  (VehicleSelectionScene)
    │
    │  FootSensorInput calibration runs here
    │  (prep → baseline → max phases, retries every 2 s)
    │
    ▼
Vehicle selection UI  (segway / escooter / unicycle / skateboard)
    │
    │  SpawnPoseStore captures headset position + yaw
    │  SessionState stores chosen vehicle
    │
    ▼
Training scene  (e.g. SegwayTraining, EScooterTraining, …)
    │
    │  Participant gets familiar with the vehicle controls
    │  No data is logged here
    │  A "Start Game" button is available in the scene to launch the real task
    │
    ▼  [participant presses "Start Game" when ready to begin the coin collection game]
    │
Simulator scene  (e.g. SegwaySimulator, EScooterSimulator, …)
    │
    │  SimulatorSceneBootstrap enables the right controller
    │  StudyLoggerBootstrap starts a new ride log
    │  Coins are spawned by TransitionCoinSpawner along PathRoot waypoints
    │
    ▼
Finish menu  (shown when all coins are collected)
    │
    │  StudyLogger.FinishRide() writes summary CSV
    │  10-second countdown then auto-return to Start scene
    │
    └──► Back to Start scene (calibration skipped on return)
```

Scene names that must exist in Build Settings:

- `Start` (vehicle selection)
- `SegwayTraining`, `EScooterTraining`, `UnicycleTraining`, `SkateboardTraining`
- `SegwaySimulator`, `EScooterSimulator`, `UnicycleSimulator`, `SkateboardSimulator`

The distinction between Training and Simulator scenes matters for extending the prototype: **only the Simulator scenes log data**. If you want to study behaviour during a practice phase too, you would need to call `StudyLogger.StartRide()` in the Training scene as well.

---

## Architecture overview

```
Physical hardware
    └── ESP32 boards  ──UDP──►  VehicleDataReceiver  (persists across scenes)
                                FootSensorInput       (persists across scenes)
                                        │
                           ┌────────────┴────────────┐
                           ▼                         ▼
                  Vehicle Controller          StudyLogger
                  (per vehicle type)          (writes log files)
                           │
                           ▼
                  BikeLab Segway API
                  (setVelocity / setSideIncline)
                           │
                           ▼
                    Unity physics / VR camera
```

**All sensor receiving happens in the Start scene only.** Two GameObjects in the Start scene host the sensor components. Because they are marked `DontDestroyOnLoad`, they persist automatically into every training and simulator scene — no sensor setup is needed in those scenes.

| Component | Lives on | Persists |
|-----------|----------|---------|
| `VehicleDataReceiver` | `SensorManager` (Start scene) | Yes — all scenes |
| `FootSensorInput` | `InputHandler` (Start scene) | Yes — all scenes |
| `UnityMainThreadDispatcher` | `InputHandler` (Start scene) | Yes — all scenes |

`GameManager` and `StudyLogger` do **not** persist; they are recreated fresh in each simulator scene.

---

## Core scripts reference

### `VehicleDataReceiver.cs`
Singleton. Opens three non-blocking UDP sockets. Each `Update()` drains all queued packets. Exposes:
- `throttle` (float 0–1)
- `imuPitch`, `imuRoll`, `imuYaw` (degrees)
- `footSensors` (Dictionary `string → float`) — receives foot data as a fallback only; in practice always empty because `FootSensorInput` owns port 1234

### `FootSensorInput.cs`
Singleton. Runs calibration handshake with ESP32 boards (prep → baseline → max) on a background thread. After calibration it parses `fs-…` packets and populates `leftHeel`, `leftToe`, `leftMidL`, `leftMidR`, `rightHeel`, `rightToe`, `rightMidL`, `rightMidR` (all normalised 0–1). Calls `VehicleSelectionManager.OnCalibrationComplete()` when done.

### `EscooterController.cs`
Reads throttle (speed) and IMU yaw (steering) from `VehicleDataReceiver.Instance` every frame. Applies deadzone, expo, slew-rate filtering, speed-scaled steering, and front bumper collision guard before writing to the BikeLab Segway API. Logs telemetry to `StudyLogger` each frame.

### `SegwayController.cs`
Reads foot pressure from `FootSensorInput.Instance` (toe vs heel intent) for speed, and IMU roll from `VehicleDataReceiver` for steering. Uses hysteresis deadzone on foot intent to prevent direction chatter. Otherwise same filtering pipeline as the e-scooter. Logs telemetry to `StudyLogger` each frame.

### `ElectricUnicycleController.cs`
Reads IMU pitch (lean forward/back) for speed and IMU yaw for steering, both from `VehicleDataReceiver`. Includes a slow auto-recentering of the pitch zero when the vehicle is nearly stationary, and a direction-flip boost for snappier reverse. Logs telemetry to `StudyLogger` each frame.

### `OneWheelSkateboardController.cs`
Reads IMU pitch for speed and IMU roll for steering from `VehicleDataReceiver`. Features asymmetric expo and gain (forward vs backward separately) and directional slew rates (acceleration and deceleration tuned independently). Logs telemetry to `StudyLogger` each frame.

### `GameManager.cs`
Per-scene singleton. Tracks coin count, triggers finish menu, fires events `OnCoinChanged` and `OnVehicleChanged` that HUD and other systems listen to.

### `SessionState.cs`
Static (not MonoBehaviour). Carries data between scenes: chosen `VehicleType`, calibration flag, spawn pose.

### `SimulatorSceneBootstrap.cs`
Runs on scene start. Reads `SessionState.SelectedVehicle` and calls `EnableControl(true)` on the matching controller.

### `StudyLoggerBootstrap.cs`
Calls `StudyLogger.Instance.StartRide(vehicle, coinCount)` at scene start.

### `HUDController.cs`
World-space canvas parented to the XR camera. Displays coin count. Updates every frame from `GameManager.Instance.CoinCount`.

### `SpawnPoseStore.cs` / `ApplySpawnPoseHere.cs`
Records the headset position at vehicle-selection time and applies it as the vehicle's spawn position in the simulation scene, so the player feels standing in the same real-world spot.

---

## Sensor data pipeline

### IMU → steering / speed

All sensor data is received by `VehicleDataReceiver` and `FootSensorInput`, which are started in the Start scene and persist across all scenes via `DontDestroyOnLoad`. Each vehicle controller simply reads from `VehicleDataReceiver.Instance` every frame — no sensor code runs in the vehicle scenes themselves. The pipeline per input axis is:

```
raw degrees
    → subtract zero offset  (calibrated on Start or at scene load)
    → apply deadzone        (ignore small values, e.g. ±3°)
    → normalise by sensitivity  (e.g. 55° = full deflection)
    → expo curve            (optional squaring for fine control near centre)
    → optional gain         (amplify forward lean)
    → clamp to –1..1
    → multiply by max speed or max turn angle
    → low-pass filter (Lerp)  + slew-rate limit (MoveTowards)
    → output to BikeLab Segway API
```

Calibration methods (`CalibrateSteerZero()`, `CalibrateSpeedZero()`) snapshot the current IMU reading as the zero reference. `autoZeroOnStart = true` does this automatically at scene load.

### Foot sensors → Segway speed

Only the Segway uses foot sensors for speed. The intent is computed as:

```
intentRaw = max(leftToe, rightToe) – max(leftHeel, rightHeel)
```

A hysteresis deadzone prevents direction chatter. Magnitude is expo-shaped then multiplied by `speedGain` and `maxSpeed`.

> Each insole has 4 sensors: heel, toe, mid-left, mid-right. `lToe` / `rToe` refer to the toe sensor of the **left insole** and **right insole** respectively (not left/right sides of one toe). The mid sensors are logged but not used in the speed calculation.

---

## Vehicle controllers

All four controllers follow the same structure and share the same helper functions. The differences are in which physical input maps to which output:

| Vehicle | Speed input | Steer input |
|---------|------------|-------------|
| E-Scooter | Throttle sensor (port 4210) | IMU Yaw |
| Segway | Foot sensors (toe vs heel) | IMU Roll |
| Electric Unicycle | IMU Pitch (lean forward/back) | IMU Yaw |
| Skateboard / OneWheel | IMU Pitch (lean forward/back) | IMU Roll |

> **Note for developers:** `SegwayController.cs` has `steerAxis = SteerAxis.Yaw` as its code default, which does not match the intended mapping above. Make sure this field is set to **Roll** in the Inspector on the Segway prefab.

All controllers write every frame to `StudyLogger.Instance.LogTelemetry(…)`.

### Collision handling (all controllers)

On `OnCollisionEnter` / `OnTriggerEnter` (ignoring the Coins layer):
1. Call `segway.TriggerCollisionBlackout()` (brief screen flash from BikeLab)
2. Zero velocity and angular velocity on the Rigidbody
3. Force the vehicle upright (preserve yaw only)
4. Start a short `recoverDuration` window during which forward motion into the obstacle normal is blocked
5. Log the collision event to `StudyLogger`

---

## Coin & difficulty system

Coins are spawned at runtime by `TransitionCoinSpawner` along a path defined by waypoints (`PathRoot`). Difficulty is controlled by a `TransitionDifficultyConfig` ScriptableObject.

### How difficulty works

Difficulty is not about coin count or path shape — it is about **how much the coin moves laterally between consecutive coins**. The metric is:

```
deltaLateral = |lateral[i] - lateral[i-1]|   (meters)
```

Each coin is classified into a bucket based on this delta:

| Label | Condition |
|-------|-----------|
| Easy | deltaLateral ≤ 0.2 m |
| Medium | 0.2 m < deltaLateral ≤ 0.5 m |
| Hard | deltaLateral > 0.5 m |

The spawner tries to place coins matching a **target mix** across the whole run. The current configuration is:

| Difficulty | Target proportion |
|------------|------------------|
| Easy | 35% |
| Medium | 45% |
| Hard | 25% |

For each coin, the spawner makes up to **60 attempts** to find a lateral position that brings the actual mix closer to the target. Coins can be placed up to **±1 m** from the path centre (`Max Lateral Amplitude`).

### Defining the coin path (PathRoot waypoints)

The route that coins follow is defined by the child `Transform` objects under the `PathRoot` GameObject in the scene hierarchy. Each child is a waypoint (e.g. `Waypoint0`, `Waypoint1`, … `Waypoint16`), and `TransitionCoinSpawner` interpolates along the straight segments between them to place coins.

To change the coin route:
- **Move waypoints** — select any `WaypointN` in the hierarchy and reposition it in the scene view
- **Add waypoints** — create a new empty child GameObject under `PathRoot` and position it where the path should continue; the spawner reads all children in order
- **Remove waypoints** — delete the child GameObject; the path will skip that point
- **Reorder waypoints** — drag children up/down in the Unity hierarchy to change the sequence. The spawner reads waypoints by their **hierarchy order** (`GetChild(0)`, `GetChild(1)`, …), not by the number in their name. Renaming `Waypoint3` to `Waypoint14` has no effect on the order — only the position in the hierarchy matters

The number and placement of waypoints directly controls the shape, length, and curvature of the route. Each coin's `Segment Index` in the log records which waypoint segment it was placed on, so path changes are automatically reflected in the output data.

### `TransitionCoinSpawner` Inspector settings (current values)

| Field | Value | Meaning |
|-------|-------|---------|
| Coin Spacing | 15 | Distance between coins along the path |
| Forward Jitter | 0.15 | Random variation in forward spacing |
| Air Height | 1.5 | Height above ground coins are placed (m) |
| Random Seed | 0 | Set to a fixed value for reproducible layouts |
| Spawn On Start Play | ✓ | Coins are spawned when the scene starts |

### `CoinMeta` — per-coin data

Every spawned coin has a `CoinMeta` component written at spawn time:

| Field | Meaning |
|-------|---------|
| `Index` | Position in the sequence along the path |
| `S` | Distance along path in meters |
| `Lateral` | Lateral offset from path centre (meters) |
| `Delta Lateral` | Absolute lateral change from previous coin (meters) |
| `Label` | Easy / Medium / Hard (derived from delta) |
| `Segment Index` | Which waypoint segment the coin sits between |

This metadata is logged to `coins_*.csv` and attached to every `coin_collected` event, allowing post-hoc analysis of where on the path participants had errors and at what difficulty level.

`Coin.cs` calls `GameManager.Instance.AddCoin(1)` on pickup. When all coins are collected, `FinishRide()` is triggered automatically if `autoFinishWhenAllCollected = true`.

---

## Data logging

On each run `StudyLogger` creates a per-session folder inside `/storage/emulated/0/Documents/` (on the Quest) with four files:

| File | Format | Content |
|------|--------|---------|
| `telemetry_*.csv` | CSV | One row per sample at `telemetryHz` (default 10 Hz): timestamp, world XYZ, rig rotation, IMU values, throttle, commanded speed/turn, estimated speed, 8 foot sensor values |
| `events_*.jsonl` | JSONL | One JSON object per event: `ride_start`, `ride_finish`, `coin_collected`, `coin_missed`, `collision`, `fall` |
| `coins_*.csv` | CSV | Catalogue of all coins placed this run with their world positions and metadata |
| `summary_*.csv` | CSV | One row: session ID, vehicle, start/end UTC, duration, coins planned vs collected |

Each session folder is automatically named `{timestamp}_{vehicle}_{rideId}` and is uniquely identifiable without any manual setup.

**To retrieve logs from the headset:** connect via USB and use `adb pull /storage/emulated/0/Documents/ ./logs` or browse with Android File Transfer.

---

## Inspector parameters you will want to tune

These are the most likely parameters to adjust when adapting the prototype for a new study:

**Vehicle feel (per controller):**
- `maxSpeed` — top speed in m/s
- `yawSensitivityDeg` — degrees of physical turn that equals full virtual deflection
- `speedSensitivityDeg` (EUC only) — degrees of lean that equals full speed
- `throttleExpo` / `yawExpo` — 0 = linear response, 1 = squared (more precision near centre)
- `speedGain` / `forwardGain` — multiplier after expo to amplify weak inputs
- `turnGain` — multiplier on final turn command
- `idleBrakePerSec` — how fast the vehicle brakes when input is released

**Collision recovery:**
- `recoverDuration` — seconds after collision during which forward motion is blocked (default 0.35–0.4 s)
- `bumperDistance` — raycast length for the frontal wall detector (meters)

**Difficulty:**
- Edit `TransitionDifficultyConfig.asset` to change the target mix (Easy/Medium/Hard proportions), the lateral amplitude cap, the delta thresholds that define each bucket, or the number of placement attempts per coin

**Logging:**
- `telemetryHz` on StudyLogger — sample rate (10 Hz is enough for most analyses; raise to 30 Hz for fine-grained kinematics)

---

## How to extend the prototype

This section describes the most common research additions and where in the code to implement them.

### Adding a vehicle visualisation (e.g. 3D scooter model visible to the rider)

The BikeLab Segway asset already has a vehicle mesh. To make it visible to the rider in first person:

1. Find the vehicle prefab in the scene (referenced by the controller as `segway`).
2. Enable or add a child mesh renderer for the vehicle body.
3. The XR camera and vehicle physics are already linked through `XRVehicleFollower.cs` / `XRCameraFollower.cs` — the camera follows the Segway rig. No structural changes needed.
4. For a semi-transparent body: use a custom URP shader with alpha < 1 on the vehicle material.

### Adding a body / avatar visualisation (e.g. showing the rider's hands or legs)

1. **Hands only:** XR Interaction Toolkit already has hand tracking. The `Samples/` folder contains XR Hands visualiser samples. Enable `XR Hand Tracking Subsystem` in XR Plugin Management and add hand mesh renderers to the XR Origin.
2. **Full body / IK avatar:** Import an IK avatar package (e.g. Final IK or Unity Animation Rigging). Drive the hips from the vehicle rig transform and the head from the XR camera. Drive arms/hands from XR controllers or hand tracking. Key transforms to source from: `segway.transform` (root of vehicle), `Camera.main.transform` (head).
3. **Foot visualisation:** `FootSensorInput` already exposes normalised per-sensor values each frame (`leftToe`, `rightHeel`, etc.). These can drive a colour-mapped foot overlay or deform a foot mesh to show pressure.

### Adding a new visual cue (e.g. speed indicator, lean indicator)

The `HUDController` is a world-space canvas following the XR camera. Add new UI elements as children of that canvas. Read values from `GameManager.Instance` (coin count, current vehicle) or directly from `VehicleDataReceiver.Instance` (raw sensor data) or from the controller's `segway.getVelosity()`.

### Adding a new vehicle type

1. Create a new controller script following the same pattern as the existing four (`EscooterController`, `SegwayController`, `ElectricUnicycleController`, `OneWheelSkateboardController`). In `Update()`: read from `VehicleDataReceiver.Instance`, compute `targetVelocity` and `targetTurn`, write to `segway.setVelocity()` and `segway.setSideIncline()`.
2. Add the new `VehicleType` enum value to `SessionState.cs` and `GameManager.VehicleKind`.
3. Map it in `VehicleSelectionManager.TrainingSceneForVehicle()`.
4. Add a button in the vehicle selection scene UI.
5. Add a case in `SimulatorSceneBootstrap.Start()`.

### Changing what gets logged

All log output goes through `StudyLogger`. To add a new event:
```csharp
Study.StudyLogger.Instance.LogEvent("my_event", new Dictionary<string, object> {
    { "myField", someValue },
    { "timeSinceStart", ... }
});
```
`LogEvent` is currently `private` — change it to `public` first, or add a dedicated public method following the same pattern as `LogCollision`.

To add new columns to the telemetry CSV, edit the header string and row-building code in `StudyLogger.LogTelemetry()`.

### Modifying the coin path / task design

See the [Defining the coin path](#defining-the-coin-path-pathroot-waypoints) and [Coin & difficulty system](#coin--difficulty-system) sections for full details. In summary:
- To change the **route**: reposition or add/remove `WaypointN` children under `PathRoot` in the scene hierarchy
- To change **difficulty distribution**: edit `TransitionDifficultyConfig.asset` (delta thresholds, target Easy/Medium/Hard mix, lateral amplitude)
- To change **coin density**: adjust `Coin Spacing` on the `TransitionCoinSpawner` component in the scene

### Running without physical hardware (desktop / editor testing)

Every controller has a `useDebugSpeed` toggle and `debugSpeed` float in the Inspector. Enable this to move the vehicle at a fixed speed without any sensor input. IMU-based steering still uses keyboard or the Editor's input system unless you also override `VehicleDataReceiver` with simulated values.

---

## Project setup checklist

1. **Unity version:** **2021.3.35f1** — use this exact version to open the project.
2. **XR Plugin Management:** enable Oculus / OpenXR for Android in Project Settings → XR Plugin Management.
3. **Build target:** Android, ARM64, IL2CPP, minimum API level 29.
4. **Scenes in Build:** add all scenes listed in [Scene flow](#scene-flow) in the correct order.
5. **Layers:** a `Coins` layer must exist in the project (used by collision filtering in all controllers and the bumper raycast).
6. **Permissions:** `WRITE_EXTERNAL_STORAGE` is requested at runtime on Android in `StudyLogger.Awake()`. Accept it on first launch or logs will not be written.
7. **Wi-Fi:** headset and ESP32 boards must be on the same subnet. Default ESP32 IPs are hardcoded in `FootSensorInput.leftBoardIP` / `rightBoardIP` — change to match your network.
8. **BikeLab asset:** the vehicle physics rely on the `VK.BikeLab.Segway` package (found in `Assets/BikeLab/`). This is a third-party paid asset; ensure it is present before building.

---

## Codebase map

```
Assets/
├── GameManager.cs              Central game state, coin tracking, finish logic
├── SessionState.cs             Static cross-scene state (vehicle choice, spawn pose)
├── SimulatorSceneBootstrap.cs  Enables correct controller on scene load
├── VehicleSelectionManager.cs  Start-scene UI, triggers scene load
├── StartSceneFlow.cs           One-shot flag to skip calibration on return
│
├── VehicleDataReceiver.cs      UDP receiver: throttle (port 4210), IMU (port 1235)
├── FootSensorInput.cs          ESP32 calibration handshake + foot sensor parsing (port 1234)
│
├── EscooterController.cs            Throttle + yaw IMU → velocity/turn
├── SegwayController.cs              Foot sensors + roll IMU → velocity/turn
├── ElectricUnicycleController.cs    Pitch IMU (speed) + yaw IMU (steer) 
├── OneWheelSkateboardController.cs  Pitch IMU (speed) + roll IMU (steer) 
│
├── StudyLogger.cs              Writes telemetry CSV, events JSONL, summary CSV
├── StudyLoggerBootstrap.cs     Calls StudyLogger.StartRide() at scene start
│
├── Coin.cs                     Spin animation, trigger pickup, audio/particles
├── CoinMeta.cs                 Design metadata per coin (index, label, lateral)
├── CoinLifecycleController.cs  Manages coin spawning lifecycle
├── BlockManager.cs             Streams environment: activates/deactivates street blocks based on player distance to keep performance stable
├── TransitionCoinSpawner.cs    Spawns coins along PathRoot waypoints with difficulty-controlled lateral placement
├── DistanceCoinCollector.cs    Collects coins within distance threshold
│
├── TransitionDifficultyConfig.cs  ScriptableObject: lateral amplitude, Easy/Medium/Hard delta thresholds, target mix ratios, attempts per coin
│
├── HUDController.cs            World-space HUD following XR camera
├── FloatingText.cs             Pop-up floating text (coin pickup feedback)
│
├── XRVehicleFollower.cs        Locks XR rig to vehicle transform
├── XRCameraFollower.cs         Keeps camera aligned with vehicle
├── XRSpawnCoordinator.cs       Places vehicle at stored spawn pose
├── SpawnPoseStore.cs           Records headset pose at selection time
├── ApplySpawnPoseHere.cs       Applies stored pose to vehicle on scene load
├── AvatarFollower.cs           Drives avatar from XR rig transforms
│
├── MovingAverage.cs            Generic moving-average filter utility
├── UnityMainThreadDispatcher.cs  Dispatches background callbacks to main thread
│
└── Scenes/                     Additional scenes (Miami Beach, Dark Beach Street…)
```
