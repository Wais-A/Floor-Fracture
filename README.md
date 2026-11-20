# Floor Fracture

A solo mixed-reality survival game for Meta Quest 3. Step off cracking glass before the countdown reaches zero. Staggered shaft-style brick covers the scanned floor around the glass. Live Quest depth keeps real furniture in front of the masonry. Real walls and furniture stay in view as neighboring panes open into shafts.

[Main project document](./FLOOR_FRACTURE.md) · [Implementation checklist](<./Floor Fracture — Master Project Checklist.md>) · [Course design PDF](<./Docs/Floor Fracture Design Document.pdf>)

## Open the project

Install Unity `6000.6.9f1` with Android Build Support, the Android SDK and NDK, and OpenJDK. Clone with Git LFS enabled, run `git lfs pull`, and open this directory in Unity Hub. Unity resolves Meta XR `209.0.0` and Unity OpenXR `1.19.0` and Unity OpenXR: Meta `2.7.0` from the package lockfile.

Open `Assets/FloorFracture/Scenes/FloorFracture.unity`. The Android build list points to this scene. The signed ARM64 package is `com.wa003.floorfracture`. Git `3265e4b` is the earlier template-scene packaging baseline. Game source is under `Assets/FloorFracture/`.

## Play

1. Open the game and allow spatial data. If there is no saved room, use Quest Space Setup to capture the floor and furniture. The game fits the board to the scan and shows its size on Welcome. The gold outline follows the playable glass perimeter. Use **Play area** to try another spot or update the scan.
2. Press a menu button with a fingertip, point and pinch with a hand, or point and press the controller trigger. Choose **Tutorial** and stand on the glass. When your pane cracks, step onto a clear neighbor. After practicing, choose **Start wave**. **Try the stone (optional)** lets you grab a rock and drop or toss it onto any intact pane.
3. Stand on a pane, choose **Start wave**, and step to intact glass. Your starting pane always breaks. The ring shows your current support. In Memory, remember the cracks before they disappear. Last Tile completes the level.
4. During a fall, a fresh index pinch with either tracked hand, or A or X on a controller, skips ahead to Results. **Settings** includes reduced-motion falls, warning intensity and volume. **Restart** rebuilds the board and resets the score.

## Build and install

In **File → Build Profiles**, select the saved **Meta Quest** profile. It uses Vulkan, ARM64, the project's Mobile URP quality level, Android API 32 minimum and API 34 target. The global scene list contains the playable FloorFracture scene. Choose **Build** or **Build And Run**. For a command-line build on this Mac, run the following from the project directory. Set `FLOOR_FRACTURE_DEV=1` for the development menu and local measurement recording.

```sh
/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath "$PWD" \
  -activeBuildProfile "Assets/Settings/Build Profiles/Meta Quest.asset" \
  -executeMethod FloorFracture.Editor.GameProjectSetup.BuildAndroid \
  -logFile "$TMPDIR/FloorFracture-build.log"
```

The default output is `Builds/Quest/FloorFracture.apk`. A standalone batch build needs an available checkout. To sideload, enable developer mode on the Quest, authorize USB debugging, and use Android Platform Tools. With one authorized headset connected, run `adb install -r Builds/Quest/FloorFracture.apk`, then open Floor Fracture from Unknown Sources.

## Reproduce and verify

- **Meta XR Simulator.** Enable the standalone simulator, open `Assets/FloorFracture/Scenes/FloorFracture.unity` in Unity, and enter Play Mode. The game appears in the separate Meta XR Simulator **Left eye** viewport. Unity keeps rendering when that window has focus. The Mac simulator uses its own scanned room and Meta’s invisible furniture occluder. [Simulator verification](<./Docs/Verification/Brick floor simulator.json>)
- **Offline Editor fixture.** With the simulator disconnected, enter Play Mode. It uses MRUK's Rec Room fixture with visible floor and furniture outlines. With desktop input active, walk with WASD, look with the arrow keys, use Q/E for left/right grip, and click or use the numbered menu choices.
- **Seeded rounds.** In an Editor or development build choose **Development → Seed 379 · level 1**. On a 4×4 footprint, start in cell 0 (local negative X, negative Z). Follow cells 1, 2, 3, 7, 6 to see Classic, Memory and Last Tile. With the level 3 control, wave 2 may be reproduced on the same footprint from cell 1 with a slider over the old cell 0 and stationary safety in cell 2. Stay over the opening while the moving pane is away to test a miss.
- **Endings.** Choose **Development → Next ending** until the desired name appears, then **Preview loss**. For the player path, stay on the starting pane through zero. A retail build selects endings from its independent shuffle.
- **Checks.** Use **Tools → Floor Fracture → Run board rule checks** for the deterministic planner and **Verify gameplay in Play Mode** for the fixture, real physics, deadlines and replay flow. Reports appear under `Builds/Verification/`. On Quest use the [playtest sheet](<./Docs/Quest playtest.md>) and [performance guide](<./Docs/Quest performance.md>).

## Verification and roadmap

The first headset trial verified aligned passthrough glass, hand input, safe movement, shattering and a deliberate loss. Wais found the board cramped and the descent unconvincing. The subsequent 7 × 2 water prototype left spare space beside the board and the blue floor flickered on Quest. Wais confirmed brick on the floor with real furniture visible in front on the signed Quest build. This revision also favors more sideways movement, recalls an open menu after a system recenter and accelerates the five-metre fall at 9.8 m/s². After the gameplay review, Wais will choose a voice for tutorial and results lines that will play offline. A results explanation is a later AI experiment. [Physical test record](<./Docs/Verification/Quest 3 playtest.md>) · [Building Blocks review](<./Docs/Meta Building Blocks.md>)

## Brick floor preview

![Brick paving meeting the glass at player height in the MRUK Rec Room](./Docs/Media/brick-player-height.png)

The blue lines in this offline fixture mark the simulated room geometry. In the headset, passthrough supplies the view of the furniture. [Brick and furniture checks](<./Docs/Verification/Brick floor Editor playthrough.json>) · [Brick Quest APK](<./Builds/Quest/Brick-floor/FloorFracture.apk>)

## Views from the revised Unity playthrough

| Classic wave at the player's eye position | Looking down the shaft before the Water landing |
|---|---|
| ![Cracking floor panels in the Unity Rec Room fixture](./Docs/Media/classic-player-view.png) | ![Water still below the player midway down the virtual shaft](./Docs/Media/fall-water-full-mid-shaft-down.png) |

The neighboring opening and the Lava descent are also captured from the simulated eye. These images use the Editor's room fixture. [Neighboring broken pane](./Docs/Media/neighboring-hole-player-view.png) · [Lava shaft during descent](./Docs/Media/fall-lava-full-mid-shaft-forward.png) · [Playthrough measurements](<./Docs/Verification/Adaptive Editor playthrough.json>)

## Credits and AI usage

Wais Almakaleh is the solo student developer. Unity provides the engine and URP. Meta provides the Quest runtime, Meta XR SDK, MRUK and the licensed Floor Zone sample. Codex assisted with SDK comparison, troubleshooting, design documentation, implementation and automated verification. The course design includes the original project pitch and a written first-minute walkthrough. [Asset credits and licenses](<./Docs/Asset credits.md>)
