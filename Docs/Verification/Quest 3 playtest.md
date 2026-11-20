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

On the water-floor build, stand on the glass and look across the room toward a wall and behind a chair. Water should fill the scanned floor right up to its edges. After a neighboring pane falls, look into the shaft from a safe pane. Record any water across the shaft, water in front of real furniture, flicker, or uncovered patches. Wearer feedback is pending.

## Repeat on the revised build

The Editor completed the whole revised game loop with a 6 × 3 board at 65.24 cm per pane. At full motion the hazard started five metres below the rim and reached the opening at about 3.15 seconds. Wais’s physical review determines whether this reads as a fall. The first headset build has a separate measured baseline. [Revised Editor playthrough](<./Adaptive Editor playthrough.json>) · [First Quest metrics](<./First Quest run metrics.json>)

- [x] Record the first adaptive APK and launch it on Quest. The signed development build was 127,327,002 bytes. [Build and install record](<./Adaptive Quest build.json>)
- [ ] In the same scan, write down the dimensions and pane width shown on Welcome. Check whether the board uses the available space without prompting for a clearance verdict.
- [ ] Walk the guaranteed route at a comfortable pace. Record whether the larger panes feel right and whether another usable patch offers a larger board.
- [ ] Use a full-motion loss for Water and Lava. Record whether the opening, passing shaft features, approaching surface and moment of contact read in that order, with sinking or flames afterward.
- [ ] Try Reduced separately, then Skip and Restart. Record which input was used and whether the real room returns cleanly.
- [ ] When available, try the two Touch controllers independently, then collect the separate larger-board performance and outside-player observations.
