# Sprint or Shift: Setup Guide

A fast first-person maze runner with three rules:

1. **Don't stop.** Stand still (speed < 0.1) for 2 seconds and you are teleported back to START. A red "DON'T STOP!" overlay fades in with a countdown first.
2. **Don't look back.** Purple "dynamic" walls follow one rule: a wall can move only after you have actually seen it, and only once it is behind you. One-way gates slam shut after you walk through them. Toggle walls flip open or closed each time they end up behind you. Every purple wall starts open.
3. **Dead ends are traps.** If you get boxed in, stand still. After 2 seconds you go back to START and every shifting wall resets.

Reach the glowing cyan **Finish Portal** to finish a level. The game runs Level 1, then Level 2, then the Victory screen (press **R** to play again).

---

## 1. Run it now (Unity 6)

1. Open the `SprintOrShift` folder in Unity Hub with **Unity 6000.4.0f1**. Any Unity 6 version with URP should also work.
2. Open `Assets/Scenes/Level1.unity`.
3. Press **Play**. Click in the Game view to capture the mouse.

| Input | Action |
|---|---|
| WASD / arrow keys | Move |
| Mouse | Look |
| Esc | Release the mouse (click to capture it again) |
| R | Restart from Level 1 (Victory screen only) |

Both levels are already in **Build Profiles → Scene List** (Level1 = index 0, Level2 = index 1).

---

## 2. Project layout

```
Assets/
├── Scripts/
│   ├── FirstPersonController.cs   movement, mouse look, stillness rule, portal trigger
│   ├── DynamicMazeWall.cs         "don't look back" wall shifting
│   └── GameManager.cs             spawn point, UI, level flow
├── Editor/
│   ├── SprintOrShiftLevelBuilder.cs   (optional) rebuilds both scenes from text layouts
│   └── SubmissionTools.cs             (optional) captures submission screenshots, builds the macOS app
├── Scenes/      Level1.unity, Level2.unity
├── Materials/   Wall, Floor, DynamicWall, Portal, StartPad, SignPlate, Neon_*
└── Settings/    URP assets + SprintOrShiftVolume (bloom, vignette)
```

The three runtime scripts depend on each other in only one way: the player and the walls call `GameManager.Instance`.

---

## 3. Scene hierarchy (the same in both levels)

```
Level1
├── Directional Light
├── Global Volume          bloom makes the emissive signs and portal glow
├── GameManager            [GameManager]
├── Environment
│   ├── Floor
│   └── StartPoint         ← spawn transform (blue arrow = facing direction)
│       ├── StartPad       green glowing floor pad
│       └── Sign_START
├── Maze
│   ├── Walls              static cubes (Wall_row_col)
│   ├── DynamicWalls       [DynamicMazeWall] Gate_* (one-way) and ShiftWall_* (toggle)
│   └── Signs              Sign_DON'T STOP, Sign_DON'T LOOK BACK, ...
├── Player                 [CharacterController] [FirstPersonController]
│   └── PlayerCamera       Camera (MainCamera tag, Post Processing on)
│       └── Flashlight     spot light
├── UI                     Canvas (Screen Space Overlay, scales from 1920×1080)
│   ├── Crosshair
│   ├── StillnessOverlay   red Image + CanvasGroup (alpha driven by script)
│   │   └── WarningText    TMP "DON'T STOP! 1.4"
│   ├── MessageText        TMP level intro / "TOO SLOW!" / "LEVEL COMPLETE"
│   └── VictoryPanel       (disabled) "YOU ESCAPED!"
└── FinishPortal           emissive Cube, BoxCollider (Is Trigger), Tag = Finish
    └── PortalLight
```

---

## 4. Inspector fields

### GameManager
| Field | Level 1 | Level 2 | Notes |
|---|---|---|---|
| Level Title / Subtitle | LEVEL 1 / DON'T STOP. DON'T LOOK BACK. | LEVEL 2 / HIGH CHAOS | Intro text |
| Next Scene Name | `Level2` | *(empty)* | Leave this empty on the last level to show the Victory screen |
| Transition Delay | 2.5 | 2.5 | Seconds before the next level loads |
| Player | Player | Player | |
| Start Point | Environment/StartPoint | same | The only place the spawn point is set |
| Stillness Overlay | UI/StillnessOverlay (CanvasGroup) | same | |
| Stillness Text | UI/StillnessOverlay/WarningText | same | |
| Message Text | UI/MessageText | same | |
| Victory Panel | UI/VictoryPanel | same | Must start disabled |

### FirstPersonController (on Player)
| Field | Default | Notes |
|---|---|---|
| Camera Pivot | PlayerCamera | Child camera, which tilts up and down |
| Move Speed / Acceleration | 6.5 / 12 | Higher acceleration means snappier movement |
| Mouse Sensitivity / Look Smoothing | 0.45 / 30 | |
| Max Look Angle | 85 | |
| Stillness Limit | **2.0** s | Rule 1 |
| Still Speed Threshold | **0.1** | Rule 1 |

### DynamicMazeWall (on each Gate_* / ShiftWall_* cube)
| Field | Gate (one-way) | ShiftWall (toggle) |
|---|---|---|
| Mode | CloseBehindPlayer | Toggle |
| Starts Closed | off | off (`T` in a layout = on, `t` = off) |
| Behind Angle | 110° | 110° (how far behind your view direction counts as "behind") |
| Shift Cooldown | 0 | L1: 2.0, **L2: 0.6** (faster chaos) |
| Min Player Distance | 1.5 | 1.5 (never shifts on top of the player) |
| Pass Distance | 3 | n/a |

**One-way gates:** the cube's blue **Z arrow** must point the way the player travels through it. Select a wall in the Scene view to see its gizmo: a cyan ray shows a gate's direction, and a magenta box marks a toggle wall.

---

## 5. How each rule is implemented

- **Stillness:** `FirstPersonController.HandleStillness()` reads `CharacterController.velocity`. Pushing into a wall therefore counts as standing still, which is what makes the dead-end rule work. The timer only starts after your first step, so waiting at START is safe. `TeleportToStart()` moves the player (with the controller briefly disabled), then calls `GameManager.OnPlayerTeleported()`. That shows "TOO SLOW!" and resets every dynamic wall.
- **Don't look back:** every frame, `DynamicMazeWall` checks two things:
  1. **Seen:** the wall is inside the player camera's view frustum **and** a `Physics.Linecast` from the camera reaches it. A wall hidden behind other walls doesn't count.
  2. **Behind:** the wall is more than `behindAngle` (110°) from where the player is looking, and it is off screen.

  A wall shifts only when it was seen and is now behind. After it shifts, it must be seen again before it can shift again. Shifting turns the Renderer and Collider on or off together. This replaces `OnBecameInvisible()`, which also counts the Editor's Scene-view camera and ignores what is in the way.
- **Portal:** the portal has the built-in **Finish** tag and a trigger collider. `FirstPersonController.OnTriggerEnter` calls `GameManager.OnPortalReached()`, which disables input, shows a message, and loads `nextSceneName`. If `nextSceneName` is empty, it shows the Victory panel instead.

---

## 6. Editing or rebuilding the levels

**By hand:** move or duplicate cubes freely. To add a shifting wall, duplicate an existing `Gate_*` or `ShiftWall_*`, or add `DynamicMazeWall` to any cube.

**With the builder (optional):** the layouts live as text in `Assets/Editor/SprintOrShiftLevelBuilder.cs`:

```
#  wall      .  floor     S  start     F  finish portal
> < ^ v      one-way gate (arrow = travel direction)
T / t        toggle wall starting closed / open
1-9          wall with a sign (text + facing listed under the layout)
```

Edit a layout, then run **Sprint or Shift → Build Both Levels** from the menu bar. ⚠️ This overwrites `Level1.unity` and `Level2.unity`, including any changes you made by hand.

### Building the scene from scratch, without the builder
1. **TextMeshPro:** Window → TextMeshPro → Import TMP Essential Resources (already done in this project).
2. **Floor:** a Cube scaled (44, 0.5, 44) at y = −0.25.
3. **Walls:** Cubes scaled (4, 3.5, 4) at y = 1.75, placed on a 4 m grid.
4. **StartPoint:** an empty GameObject on the floor, with its blue arrow facing down the first corridor.
5. **Player:** an empty GameObject. Add **FirstPersonController**, which also adds a CharacterController (Height 1.8, Radius 0.4, Center y 0.9). Add a child Camera at y = 1.6, tag it MainCamera, and tick **Rendering → Post Processing**. Drag the camera into **Camera Pivot**.
6. **Dynamic walls:** wall cubes with **DynamicMazeWall**.
7. **FinishPortal:** a Cube with an emissive material. Set its Tag to **Finish** and tick **Is Trigger** on its BoxCollider.
8. **UI:** Canvas, then add the StillnessOverlay Image (+ CanvasGroup, alpha 0) with a TMP child, MessageText (TMP), and VictoryPanel (Image + TMP, disabled).
9. **GameManager:** an empty GameObject with **GameManager**. Fill in the fields as in section 4.
10. **Global Volume:** a Volume with Bloom (threshold 0.9, intensity 1.4).
11. Add both scenes to **File → Build Profiles → Scene List** (Level1 first).

---

## 7. Level design notes

- **Level 1 (tutorial):** a single serpentine corridor. It teaches the stillness rule ("DON'T STOP" is the first thing you see) and has two one-way gates ("DON'T LOOK BACK").
- **Level 2 (high chaos):** a 17×13 grid with 4 dead ends, a "DEAD END" trick corridor that stops one wall short of the real path, a large dead-end region on the left, 5 one-way gates and 2 fast toggle walls (0.6 s). `ShiftWall_5_10` is an open shortcut to the exit that closes once it's behind you, and `ShiftWall_7_10` can close behind you in the left region, which forces the long way round. Both layouts were checked with a solver: each can be finished even when every toggle wall is closed.
