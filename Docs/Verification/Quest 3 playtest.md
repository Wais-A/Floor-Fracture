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
- [ ] Compare the fitted board with the space available in the current scan. Look along the previously unused long side and try one comfortable step.
- [x] Wais reviewed furniture in his physical room and reported, “Brick on the floor, furniture clearly in front.” The installed APK matched the local SHA-256, the spatial-data grant was active, and the app recorded Vulkan and the expected build GUID.
- [ ] Move the bin with the headset off and recheck its old and new positions. Record any halo or brick on the object.
- [ ] Look into an adjacent broken pane. The mouth should remain clear of brick.
- [ ] Open Settings, face another direction and hold the Meta button. Record whether the same menu moves into reach and the glass stays anchored. Repeat on Results.
- [ ] With full motion enabled, stay on a marked square. Note whether the shaft now passes quickly before the Water or Lava impact. Compare Reduced and try Skip.

## Earlier adaptive build

The preceding Editor version fitted 6 × 3 panes at 65.24 cm in Meta’s Rec Room fixture and took about 3.15 seconds to descend. [Earlier Editor playthrough](<./Adaptive Editor playthrough.json>) · [First Quest metrics](<./First Quest run metrics.json>)

- [x] Record the first adaptive APK and launch it on Quest. The signed development build was 127,327,002 bytes. [Build and install record](<./Adaptive Quest build.json>)
- [ ] In the same scan, write down the dimensions and pane width shown on Welcome. Check whether the board uses the available space without prompting for a clearance verdict.
- [ ] Walk the guaranteed route at a comfortable pace. Record whether the larger panes feel right and whether another usable patch offers a larger board.
- [ ] Use a full-motion loss for Water and Lava. Record whether the opening, passing shaft features, approaching surface and moment of contact read in that order, with sinking or flames afterward.
- [ ] Try Reduced separately, then Skip and Restart. Record which input was used and whether the real room returns cleanly.
- [ ] When available, try the two Touch controllers independently, then collect the separate larger-board performance and outside-player observations.
