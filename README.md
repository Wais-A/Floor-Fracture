# Floor Fracture

A solo mixed-reality survival game for Meta Quest 3. Step off cracking glass before the countdown reaches zero. Staggered shaft-style brick covers the scanned floor around the glass. Live Quest depth keeps real furniture in front of the masonry. Real walls and furniture stay in view as neighboring panes open into shafts.

[Main project document](./FLOOR_FRACTURE.md) · [Implementation checklist](<./Floor Fracture — Master Project Checklist.md>) · [Course design PDF](<./Docs/Floor Fracture Design Document.pdf>)

## Open the project

Install Unity `6000.6.9f1` with Android Build Support, the Android SDK and NDK, and OpenJDK. Clone with Git LFS enabled, run `git lfs pull`, and open this directory in Unity Hub. Unity resolves Meta XR `209.0.0` and Unity OpenXR `1.19.0` and Unity OpenXR: Meta `2.7.0` from the package lockfile.

Open `Assets/FloorFracture/Scenes/FloorFracture.unity`. The Android build list points to this scene. The signed ARM64 package is `com.wa003.floorfracture`. Git `3265e4b` is the earlier template-scene packaging baseline. Game source is under `Assets/FloorFracture/`.

## Play

1. Open the game and allow spatial data. If there is no saved room, use Quest Space Setup to capture the floor and furniture. The game fits the board to the scan and shows its size on Welcome. The gold outline follows the playable glass perimeter. Use **Play area** to try another spot or update the scan.
2. Press a menu button with a fingertip, point and pinch with a hand, or point and press the controller trigger. Choose **Tutorial** and stand on the glass. When your pane cracks, step onto a clear neighbor. After practicing, choose **Start wave**. **Try the stone (optional)** lets you grab a rock and drop or toss it onto any intact pane.
3. Stand on a pane, choose **Start wave**, and step to intact glass. Your starting pane always breaks. The ring shows your current support. Look through an opening from your safe square to see the destination 80 metres below. If you lose later in that run, you fall toward that same surface. In Memory, remember the cracks before they disappear. Last Tile completes the level.
4. During a fall, a fresh index pinch with either tracked hand, or A or X on a controller, skips ahead to Results. **Settings** includes reduced-motion falls, warning intensity and volume. **Restart** rebuilds the board and resets the score.

## Build and install

In **File → Build Profiles**, select the saved **Meta Quest** profile. It uses Vulkan, ARM64, the project's Mobile URP quality level, Android API 32 minimum and API 34 target. The global scene list contains the playable FloorFracture scene. Choose **Build** or **Build And Run**. For a command-line build on this Mac, run the following from the project directory. Set `FLOOR_FRACTURE_DEV=1` for the development menu and local measurement recording.

```sh
/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -quitTimeout 1200 -projectPath "$PWD" \
  -activeBuildProfile "Assets/Settings/Build Profiles/Meta Quest.asset" \
  -executeMethod FloorFracture.Editor.GameProjectSetup.BuildAndroid \
  -logFile "$TMPDIR/FloorFracture-build.log"
```

The default output is `Builds/Quest/FloorFracture.apk`. For a clean build, use a separate checkout. The command allows 20 minutes for Unity to finish work queued before exit. The project closes Meta Interaction SDK’s telemetry session after a macOS batch build. A fresh checkout produced the signed release and Unity exited normally, with zero build errors and 67 warnings recorded. [Clean build inspection](<./Docs/Verification/Shared chamber clean release.json>) · [Unity command-line arguments](https://docs.unity.com/en-us/engine/6000.6/manual/unity-editor/command-line-arguments/editor)

To sideload, enable developer mode on the Quest, authorize USB debugging, and use Android Platform Tools. With one authorized headset connected, run `adb install -r Builds/Quest/FloorFracture.apk`, then open Floor Fracture from Unknown Sources.

## Reproduce and verify

- **Meta XR Simulator.** Enable the standalone simulator, open `Assets/FloorFracture/Scenes/FloorFracture.unity` in Unity, and enter Play Mode. The game appears in the separate Meta XR Simulator **Left eye** viewport. Unity keeps rendering when that window has focus. The Mac simulator uses its own scanned room and Meta’s invisible furniture occluder. [Simulator verification](<./Docs/Verification/Expanded room simulator.json>)
- **Offline Editor fixture.** With the simulator disconnected, enter Play Mode. It uses MRUK's Rec Room fixture. The gold perimeter identifies the playable board. With desktop input active, walk with WASD, look with the arrow keys, use Q/E for left/right grip, and click or use the numbered menu choices.
- **Seeded rounds.** In an Editor or development build choose **Development → Seed 379 · level 1**. On a 4×4 footprint, start in cell 0 (local negative X, negative Z). Follow cells 1, 2, 3, 7, 6 to see Classic, Memory and Last Tile. With the level 3 control, wave 2 may be reproduced on the same footprint from cell 1 with a slider over the old cell 0 and stationary safety in cell 2. Stay over the opening while the moving pane is away to test a miss.
- **Endings.** Choose **Development → Next ending** until the desired name appears, then **Preview loss**. For the player path, stay on the starting pane through zero. A retail build selects endings from its independent shuffle.
- **Checks.** Use **Tools → Floor Fracture → Run board rule checks** for the deterministic planner and **Verify gameplay in Play Mode** for the fixture, real physics, deadlines and replay flow. Reports appear under `Builds/Verification/`. On Quest use the [playtest sheet](<./Docs/Quest playtest.md>) and [performance guide](<./Docs/Quest performance.md>).

## Verification and roadmap

The first headset trial verified aligned glass, hand input and surviving a wave. The wearer also confirmed that physical furniture appeared in front of the brick and that holding the Meta button brought the menu back. In a later scan, the previous build fitted 2 × 2 at 57.3 cm. The wearer found that board cramped and the five-metre drop too short. [Physical test record](<./Docs/Verification/Quest 3 playtest.md>)

The expanded-room build used up to 64 panes at 50–67 cm with no fixed border around the glass. In the separate Meta simulator, the same High-ceiling Room grew from 5 × 8 to 6 × 9. The tutorial starts with stepping. A full-motion loss pauses for 0.40 seconds and then accelerates from rest at 9.8 m/s² down an 80 m shaft. Water and Lava each showed more than three seconds of passing walls in the Editor. The final fade lasts two seconds. On Quest it fitted 5 × 3 panes at 53.9 cm. Wais described the board as better and requested a deeper view from a neighboring pane. [Quest installation](<./Docs/Verification/Expanded room Quest build.json>) · [Editor measurements](<./Docs/Verification/Expanded room Editor playthrough.json>) · [Simulator observation](<./Docs/Verification/Expanded room simulator.json>)

The current gameplay scene hides the optional blue scan outlines. The approved Meta floor mask reveals one brick-lined chamber through every broken pane. Unity rendered all six destinations through the glass from both eye positions. Snow remained visible through later waves and was the landing on the next natural loss. The updated development build was installed and launched on Quest 3. [Unity playthrough](<./Docs/Verification/Shared chamber playthrough.json>) · [Quest installation](<./Docs/Verification/Shared chamber Quest build.json>) · [Wearer review](<./Docs/Verification/Quest 3 playtest.md>)

After the gameplay review, Wais will choose a voice for the offline tutorial and results lines. [Meta Building Blocks review](<./Docs/Meta Building Blocks.md>)

## Views from the current Unity playthrough

| Water seen from an intact pane | Lava seen from the same pane in another run |
|---|---|
| ![Brick lip and rippled water through a broken pane](./Docs/Media/open-shaft-water.png) | ![Glowing lava visible through the neighboring pane](./Docs/Media/open-shaft-lava.png) |

These images come from a standing viewpoint in the Unity Editor’s MRUK Rec Room. On Quest, passthrough shows the real walls and furniture. [Playable development APK](<./Builds/Quest/Shared-chamber/FloorFracture.apk>)

## Credits and AI usage

Wais Almakaleh is the solo student developer. Unity provides the engine and URP. Meta provides the Quest runtime, Meta XR SDK, MRUK and the licensed Floor Zone sample. Codex assisted with SDK comparison, troubleshooting, design documentation, implementation and automated verification. The course design includes the original project pitch and a written first-minute walkthrough. [Asset credits and licenses](<./Docs/Asset credits.md>)
