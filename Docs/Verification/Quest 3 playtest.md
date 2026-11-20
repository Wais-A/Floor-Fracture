# Quest 3 playtest · November 19, 2026

Wais tested the first playable development APK in his room. Unity build `build_a0e5ac33fd19` used the FloorFracture scene, package `com.wa003.floorfracture`, and APK SHA-256 `5004950a8c2094e3d56c272176bd385e49972984e35736b0373c547ce0464437`. A local copy preserves the exact build. [First headset APK](<../../Builds/Quest/First-headset/FloorFracture.apk>)

| What was tested | What Wais observed | Review decision |
|---|---|---|
| Real room and floor height | “My room and a floor grid that lines up.” The game recorded a 3 × 4 board. At the original 50 cm spacing, the glass covered 1.5 × 2.0 m. | Passthrough and floor alignment passed for this room. Furniture alignment in other layouts still needs observation. |
| Moving early | “I survived and saw the other glass fall.” | The safe-player side of the timed break passed. |
| Staying on the starting tile | The app entered the loss ending. Wais saw the water or lava rise and described feeling instantly at the bottom. | Loss detection passed. The illusion of falling failed. Retest the motion before closing the effect review. |
| Input | “I was using my hands which worked, I didn't try the controllers yet.” | Hand input worked for the actions Wais used. Both physical controllers, hand-only Skip, and Restart still need individual checks. |
| Clearance | “The position looked fine, but I had to judge clearance myself.” | Keep the good position. Show the fit calculated from the saved scan and make another spot or Space Setup optional. |
| Scale | The glass felt confined. Wais wants more floor coverage and wider panes as space grows. He chose a middle width for the next trial and wants to judge it in the headset. | Trial range 50–67 cm per pane. Compare the measured grid size, perceived room to move and single-step reach in the same room. |

## Surrounding-floor review

The signed water-floor development build launched on the Quest 3. The installed APK hash matched the tested file and the app recorded its Unity build GUID using Vulkan. On this build, the Quest recorded Welcome and the tutorial with 7 × 2 panes at 67 cm each, a glass footprint of 4.69 × 1.34 m. The runtime reported depth-image acquisition errors at startup and during later XR session activity. Use the chair check below to assess physical occlusion. [Water build record](<./Surrounding floor Quest build.json>) · [OpenXR depth image acquisition](https://registry.khronos.org/OpenXR/specs/1.1/man/html/xrAcquireEnvironmentDepthImageMETA.html)

Wais saw flickering blue color that did not read as water. He identified unused open space along the long side of the glass, requested a faster fall at 9.8 m/s², and reported that holding the Meta button left the menu in its earlier location. He then chose shaft-style brick around the glass and asked for the real couch, chair and trash can to appear to stand on it. Chair contact and open shafts were not judged in that test.

## Brick build on Quest 3

The signed brick build is `5674695daa384caba05d08902ec9fdc7`. The synthetic High-ceiling Room fitted 5 × 8 panes at 56 cm. The offline Rec Room fitted 5 × 6 panes at 51.74 cm. Both are comparison rooms for the physical trial. [Build receipt](<./Brick floor Quest build.json>) · [Simulator check](<./Brick floor simulator.json>) · [Editor playthrough](<./Brick floor Editor playthrough.json>)

- [x] Welcome recorded 2 × 2 at 57.3 cm per pane on the brick build.
- [x] Wais confirmed the earlier 7 × 2 and this 2 × 2 came from different rooms or scans. He found the 2 × 2 too small to play and requested use of the whole clear patch up to obstructions.
- [x] Wais reviewed furniture in his physical room and reported, “Brick on the floor, furniture clearly in front.” The installed APK matched the local SHA-256, the spatial-data grant was active, and the app recorded Vulkan and the expected build GUID.
- [ ] Move the bin with the headset off and recheck its old and new positions. Record any halo or brick on the object.
- [ ] Look into an adjacent broken pane. The mouth should remain clear of brick.
- [x] Wais held the Meta button with a menu open and reported, “Yes, the menu comes back.” The exact page and independent board-anchor check were not identified.
- [x] Wais reviewed the five-metre full-motion loss. It was faster, but impact could arrive before the walls became apparent. He requested at least three seconds of wall travel and a slower fade. This version failed the fall-feel check.
- [x] Wais found the single-target rock tutorial frustrating. He chose an untimed stepping lesson first and optional stone practice. He requested that exterior brick match the blue shaft masonry.

The Quest recorded ten shatters and all six full-motion endings in the saved small-board session. Countdown frames averaged 71.97 FPS and fall frames averaged 71.98 FPS. The longest individual frames were 36.21 ms and 53.47 ms, respectively. These measurements come from Unity’s development-build recorder. [Run evidence](<./Brick floor Quest build.json>)

## Expanded-room build installed on Quest 3

Use the same saved room that showed 2 × 2 at 57.3 cm to compare the footprint. The separate simulator’s High-ceiling Room changed from 5 × 8 at 56 cm to 6 × 9 at 58.7 cm. The offline Rec Room changed from 5 × 6 at 51.7 cm to 6 × 6 at 62.1 cm. Both are simulations. [Simulator observation](<./Expanded room simulator.json>) · [Room regression cases](<./Expanded room EditMode.json>)

- [x] Installed and launched the revised game on the Quest 3 on November 19 at 10:29 p.m. The installed APK matched the tested SHA-256, spatial data was granted, and the game recorded build GUID `7fdcaf92922947548c00e55b14ae6090` with Vulkan. The first 13 samples were in room setup. [Quest build and startup evidence](<./Expanded room Quest build.json>)
- [x] The expanded-room APK recorded 5 × 3 at 53.9 cm per pane at Welcome and again during gameplay. Wais said the play area was better. The scan identity and comfort of an ordinary step were not specified.
- [x] Wais found the blue outlines on room walls and objects distracting. From a safe pane the opening looked shallower than the loss shaft. He wants the hazard visible before a loss and the landing to match what he saw through the glass.
- [ ] Choose Tutorial, step off the pulsing pane onto an adjacent clear square, and start the first wave. Check whether the ring and the shatter make the rule clear with the rock untouched.
- [ ] In optional stone practice, try a carry, pause and drop, then a short toss. Describe the release and whether the struck pane matches where the stone landed.
- [ ] From a surviving square, compare the exterior brick and the blue wall of a neighboring hole. Look for clear glass openings and real furniture in front.
- [ ] In one full-motion loss, watch the rim, passing walls, impact and final fade. Record whether the three seconds of shaft travel feel convincing. Try Reduced or Skip separately if desired.

In the Editor, all six endings completed in Full and Reduced, presented their black frame and restored the board on Restart. The same checks passed with an early Skip in all twelve combinations. The physical pass still needs a wearer to judge the fall and activate Skip with a hand or controller. [Ending replay measurements](<./Expanded room ending replays.json>)

## Shared chamber wearer review

- [x] Installed the revised development APK on Quest 3 on November 19 at 11:19 p.m. The installed file matched the tested SHA-256. Spatial data was granted and the game activity resumed. The package contains the floor placement and occlusion blocks with the room outline mesh disabled. [Installation and package inspection](<./Shared chamber Quest build.json>)
- [x] In the Unity fixture, twelve eye-position checks found the correct distant surface at 80 m through a neighboring opening. A naturally selected Snow preview persisted through several waves and the loss landed in Snow. [Unity playthrough](<./Shared chamber playthrough.json>)
- [ ] During a wave, look at a familiar chair and wall. Check that the app draws no blue scan grid and that furniture still appears in front of the brick.
- [ ] From intact glass, look into a neighboring opening and identify the distant destination. Stay on a cracking pane in the same run. Record whether you landed in the same place you saw and whether the passing walls still make the descent convincing.

## Earlier adaptive build

The preceding Editor version fitted 6 × 3 panes at 65.24 cm in Meta’s Rec Room fixture and took about 3.15 seconds to descend. [Earlier Editor playthrough](<./Adaptive Editor playthrough.json>) · [First Quest metrics](<./First Quest run metrics.json>)

- [x] Record the first adaptive APK and launch it on Quest. The signed development build was 127,327,002 bytes. [Build and install record](<./Adaptive Quest build.json>)
- [ ] In the same scan, write down the dimensions and pane width shown on Welcome. Check whether the board uses the available space without prompting for a clearance verdict.
- [ ] Walk the guaranteed route at a comfortable pace. Record whether the larger panes feel right and whether another usable patch offers a larger board.
- [ ] Use a full-motion loss for Water and Lava. Record whether the opening, passing shaft features, approaching surface and moment of contact read in that order, with sinking or flames afterward.
- [ ] Try Reduced separately, then Skip and Restart. Record which input was used and whether the real room returns cleanly.
- [ ] When available, try the two Touch controllers independently, then collect the separate larger-board performance and outside-player observations.
