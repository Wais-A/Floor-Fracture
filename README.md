# Floor Fracture

A solo mixed-reality survival game for Meta Quest 3. Step off cracking glass before the countdown reaches zero. Staggered shaft-style brick covers the scanned floor around the glass. Live Quest depth keeps real furniture in front of the masonry. Real walls and furniture stay in view as neighboring panes open into shafts.

[Main project document](./FLOOR_FRACTURE.md) · [Implementation checklist](<./Floor Fracture — Master Project Checklist.md>) · [Course working PDF](<./Docs/Floor Fracture Final Design Working Draft.pdf>) · [Watch the 3:08 Editor rehearsal](<./Docs/Media/Floor Fracture gameplay rehearsal.mp4>)

## Open the project

Install Unity `6000.6.9f1` with Android Build Support, the Android SDK and NDK, and OpenJDK. Clone with Git LFS enabled, run `git lfs pull`, and open this directory in Unity Hub. Unity resolves Meta XR `209.0.0` and Unity OpenXR `1.19.0` and Unity OpenXR: Meta `2.7.0` from the package lockfile.

Open `Assets/FloorFracture/Scenes/FloorFracture.unity`. The Android build list points to this scene. The signed ARM64 package is `com.wa003.floorfracture`. Git `3265e4b` is the earlier template-scene packaging baseline. Game source is under `Assets/FloorFracture/`.

## Play

1. Open the game and allow spatial data. If there is no saved room, use Quest Space Setup to capture the floor and furniture. The game fits the board to the scan and shows its size on Welcome. The gold outline follows the playable glass perimeter. Use **Play area** to try another spot or update the scan.
2. Press a menu button with a fingertip, point and pinch with a hand, or point and press the controller trigger. Choose **Tutorial** and stand on the glass. When your pane cracks, step onto a clear neighbor. After practicing, choose **Start wave**. **Try the stone (optional)** lets you grab a rock and drop or toss it onto any intact pane.
3. Stand on a pane, choose **Start wave**, and step to intact glass. Your starting pane always breaks. The ring shows your current support. Look through an opening from your safe square to see the destination 80 metres below. If you lose later in that run, you fall toward that same surface. In Memory, remember the cracks before they disappear. Last Tile completes the level.
4. During a fall, a fresh index pinch with either tracked hand, or A or X on a controller, skips ahead to Results. **Settings** includes reduced-motion falls, warning intensity, volume and an optional offline narrator. Results identifies the actual cause of a loss. **Restart** rebuilds the board and resets the score.

## Build and install

In **File → Build Profiles**, select the saved **Meta Quest** profile. It uses Vulkan, ARM64, the project's Mobile URP quality level, Android API 32 minimum and API 34 target. The global scene list contains the playable FloorFracture scene. Choose **Build** or **Build And Run**. For a command-line build on this Mac, run the following from the project directory. Set `FLOOR_FRACTURE_DEV=1` for the development menu and local measurement recording.

```sh
/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -quitTimeout 1200 -projectPath "$PWD" \
  -activeBuildProfile "Assets/Settings/Build Profiles/Meta Quest.asset" \
  -executeMethod FloorFracture.Editor.GameProjectSetup.BuildAndroid \
  -logFile "$TMPDIR/FloorFracture-build.log"
```

The default output is `Builds/Quest/FloorFracture.apk`. Use a separate checkout for a clean build. The command allows twenty minutes for Unity to finish any queued work. The batch build closes Meta’s Interaction SDK telemetry session before the Mac Editor exits. [Release verification](<./Docs/Verification/Latest Quest release.json>) · [Unity command-line arguments](https://docs.unity.com/en-us/engine/6000.6/manual/unity-editor/command-line-arguments/editor)

To sideload, enable developer mode on the Quest, authorize USB debugging, and use Android Platform Tools. With one authorized headset connected, run `adb install -r Builds/Quest/FloorFracture.apk`, then open Floor Fracture from Unknown Sources.

On the project Mac, the class build is `Builds/CleanRelease/FloorFracture-72faf40.apk`. Its history-free source ZIP and verification record are in `Builds/Handoff/` with filenames beginning `FloorFracture-Source-72faf40-`. Earlier local commits and older APKs contain a Meta editor pairing credential. The current build keeps the local editor configuration on this Mac and packages an empty, disabled assistant configuration. Rotate the earlier credential before sharing any historical copy. [Packaging check](<./Docs/Verification/Editor pairing build checks.json>)

## Reproduce and verify

- **Meta XR Simulator.** Enable the standalone simulator, select a synthetic environment, open `Assets/FloorFracture/Scenes/FloorFracture.unity` in Unity, and enter Play Mode. In the separate **Left eye** viewport, Welcome appears once the room and eye height are ready. If the scan arrives late, choose **Check again**. The saved desktop profile uses MultiPass on Metal. [Current Mac window](<./Docs/Media/Latest Mac simulator.jpg>) · [Settings on Mac](<./Docs/Media/Settings in the Mac simulator.jpg>) · [Five room fits](<./Docs/Verification/Overnight simulator.json>)
- **Offline Editor fixture.** With the simulator disconnected, enter Play Mode. It uses MRUK's Rec Room fixture. The gold perimeter identifies the playable board. With desktop input active, walk with WASD, look with the arrow keys, use Q/E for left/right grip, and click or use the numbered menu choices.
- **Seeded rounds.** In an Editor or development build choose **Development → Seed 379 · level 1**. On a 4×4 footprint, start in cell 0 (local negative X, negative Z). Follow cells 1, 2, 3, 7, 6 to see Classic, Memory and Last Tile. With the level 3 control, follow the guaranteed stationary neighbors until two adjacent holes can hold one full-width moving pane and a stationary route remains alongside it. Move onto the glass to lock it. Stay over the exposed hole while it is away to test a miss. The automated game replay exercises both outcomes at seed 379.
- **Endings.** Choose **Development → Next ending** until the desired name appears, then **Preview loss**. For the player path, stay on the starting pane through zero. A retail build selects endings from its independent shuffle.
- **Checks.** Use **Tools → Floor Fracture → Run board rule checks** for the deterministic planner and **Verify gameplay in Play Mode** for the fixture, real physics, deadlines and replay flow. Reports appear under `Builds/Verification/`. On Quest use the [playtest sheet](<./Docs/Quest playtest.md>) and [performance guide](<./Docs/Quest performance.md>).

## Verification and roadmap

The first headset trial verified aligned glass, hand input and surviving a wave. The wearer also confirmed that physical furniture appeared in front of the brick and that holding the Meta button brought the menu back. In a later scan, the previous build fitted 2 × 2 at 57.3 cm. The wearer found that board cramped and the five-metre drop too short. [Physical test record](<./Docs/Verification/Quest 3 playtest.md>)

The expanded-room build used up to 64 panes at 50–67 cm with no fixed border around the glass. In the separate Meta simulator, the same High-ceiling Room grew from 5 × 8 to 6 × 9. The tutorial starts with stepping. A full-motion loss pauses for 0.40 seconds and then accelerates from rest at 9.8 m/s² down an 80 m shaft. Water and Lava each showed more than three seconds of passing walls in the Editor. The final fade lasts two seconds. On Quest it fitted 5 × 3 panes at 53.9 cm. Wais described the board as better and requested a deeper view from a neighboring pane. [Quest installation](<./Docs/Verification/Expanded room Quest build.json>) · [Editor measurements](<./Docs/Verification/Expanded room Editor playthrough.json>) · [Simulator observation](<./Docs/Verification/Expanded room simulator.json>)

The shared chamber build hid the optional blue scan outlines. The approved Meta floor mask reveals one brick-lined chamber through every broken pane. Unity rendered all six destinations through the glass from both eye positions. Snow remained visible through later waves and was the landing on the next natural loss. The updated development build was installed and launched on Quest 3. [Unity playthrough](<./Docs/Verification/Shared chamber playthrough.json>) · [Quest installation](<./Docs/Verification/Shared chamber Quest build.json>) · [Wearer review](<./Docs/Verification/Quest 3 playtest.md>)

The earlier November 20 development build was installed on Quest 3. A loss opens under the player and carries the surrounding glass and scanned bricks overhead. Water retains its rippled surface down the shaft. The Space landing covered the room in all 60 two-eye arrival views. The ring and verdict share one stance estimate. Level 3 glass uses the same surface, crack warning and shards even while moving. Results names the cause recorded at the deadline. The 54 Edit Mode tests and 34 playthrough checks passed. [Unity playthrough](<./Docs/Verification/Floor entry playthrough.json>) · [Signed package and device check](<./Docs/Verification/Overnight Quest build.json>) · [Offline narration](<./Docs/Offline narration.md>)

In a separate run, 96 consecutive losses and restarts returned to one registered board and one chamber every time. Across five native simulator rooms, boards ranged from 16 to 54 panes. The independent footprint check found each board inside its floor and clear of recorded obstacles within 1 mm. [Restart observations](<./Docs/Verification/Overnight replay checks.json>) · [Room dimensions](<./Docs/Verification/Overnight simulator.json>)

The physical Quest also rendered a controlled 64-pane run with 32 panes breaking together. Unity averaged 72.04 FPS across 740 gameplay frames. Individual frames exceeded the 13.9 ms target. All six endings reached Results in Full and Reduced on the headset. In the separate Mac simulator, both controller rays and both fingertip pokes operated the actual menus. [Quest performance measurements](<./Docs/Quest performance.md>) · [Native simulator inputs](<./Docs/Verification/Overnight simulator.json>)

The room-fitting build used source `084cf29`. It fits glass across adjoining sections of a scanned floor and pauses when room tracking is lost. A canceled moving-tile wave gives its offer back and starts with a full countdown after tracking returns. On the physical Quest, a two-section fixture fitted 18 panes across 8 m². In Unity, overlapping sections rendered the surrounding brick once in each eye. Settings shows a real cracking pane beside the menu, and a dark outline makes the white support ring easier to see on light floors. The game passed 67 Edit Mode tests and 40 Play Mode checks. [Build and device record](<./Docs/Verification/Room and pause review.json>) · [Two-eye ring comparison](<./Docs/Media/occupancy-ring-comparison.png>) · [Settings preview](<./Docs/Media/Settings warning preview.png>)

The installed Quest build uses source `72faf40`. Intact panes have a cyan and slate rim that stays visible on pale floors. Warning panes retain the full amber edge and branch cracks, including on moving glass. At 2.56 m in the bright-floor stereo test, 464–474 of 640 sampled edge positions had a pixel at 3:1 local contrast, up from 19. The latest game passed the complete 67-test rule and room suite and the 40-check playthrough. [Latest build and installation](<./Docs/Verification/Latest Quest release.json>) · [Rendered comparison](<./Docs/Media/safe-rim-comparison.png>)

### Known issues and next playtests

The wearer reported losing on safe glass and seeing the edge of furniture flicker against the brick while turning. The revised stance and depth rendering have passed Editor comparisons. The next real-room trial repeats an ordinary step at a seam, a head turn beside a chair and an off-center fall through the Water and Space chambers. Two first-time players will then try the tutorial, an advanced wave and Restart. [Wearer observations and retest steps](<./Docs/Verification/Quest 3 playtest.md>) · [Outside playtest sheet](<./Docs/Quest playtest.md>)

Frame pacing is uneven at the first large shatter. On a 64-pane Quest fixture at normal game volume, the first 32-pane break reached 96.58 ms. Repeating it in the same session reached 14.73 ms in the matched window. The remaining performance test repeats this comparison in the wearer’s saved room. [Quest measurements](<./Docs/Quest performance.md>)

## Views from the Unity playthrough

| Just before the glass breaks | Looking back up during a fall |
|---|---|
| ![The ring, timer and cracked glass on the brick arena](./Docs/Media/floor-entry-classic.png) | ![Surviving glass panes and the blue brick floor above the player's original opening](./Docs/Media/floor-entry-overhead.png) |

Both views come from a Unity Play Mode run in Meta’s Rec Room fixture. The local rehearsal opens with a safe shatter, shows the shared chamber, and includes game sound and captions. The actual Quest recording is a separate class deliverable. [Watch the 3:08 rehearsal](<./Docs/Media/Floor Fracture gameplay rehearsal.mp4>) · [Quest development build details](<./Docs/Verification/Latest Quest release.json>) · [Headset shot list](<./Docs/Verification/Quest demo shot list.md>)

## Credits and AI usage

Wais Almakaleh is the solo student developer. Unity provides the engine and URP. Meta provides the Quest runtime, Meta XR SDK, MRUK and the licensed Floor Zone sample. Codex assisted with SDK comparison, troubleshooting, design documentation, implementation and automated verification. The course design includes the original project pitch and a written first-minute walkthrough. [Asset credits and licenses](<./Docs/Asset credits.md>)
