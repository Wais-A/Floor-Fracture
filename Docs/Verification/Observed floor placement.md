# Observed floor placement on Quest 3

On November 20, 2026, the Quest 3 reported a 1.04 m verified square in the wearer’s saved room. The rectangular build required 1.16 m, so this rejection happened before live depth checks. The wearer also identified the uncomfortable hand pointer inside the Quest system scanner.

## Less sensitive floor placement on the physical Quest

The two builds read the same 17,291-vertex saved room. On the first survey pass, orange global-mesh exclusions fell from 4.98 m² to 2.55 m². Eligible floor increased from 4.73 m² to 7.67 m². The native furniture footprint was 4.37 m² in both passes. The new build placed 21 connected 40 cm panes covering 3.36 m². Its initial view supplied zero live hits. The Quest remained at Welcome for at least 78 seconds. [Previous measurements](<./Confident floor/Quest-before.txt>) · [Quest placement events](<./Confident floor/Quest-events.txt>)

All 102 Edit Mode tests passed. All eight native Meta room recordings produced connected playable boards, with their padded panes rechecked and their rendered glass verified in Unity. The signed APK built with zero errors and eight warnings. Its SHA-256 matched the installed package at `597e568a6e13c2739eb19dfc141840bc9aba23a5b074f389fa270fa5854e6830`. [Edit Mode tests](<./Confident floor/EditMode.xml>) · [Recorded layouts](<./Confident floor/Recorded-rooms.tsv>)

The Unity Play Mode check completed all 24 ending exits across six fall effects, including full motion, reduced motion, skip and restart. The run completed 35 checks, then stopped at the off-centre fall test. That assertion checks both the simulated eye’s distance from the pane centre and the shaft rim’s alignment. It does not report which measurement exceeded its limit.

The wearer confirmed that many more panes fit well in the physical room and accepted the observed overlap with a scale and loose papers.

## Scattered patches after Space Setup

The November 20 rescan saved 18,198 mesh vertices. Before live depth ran, the game classified 5.39 m² as raised mesh geometry and 4.66 m² as unobserved. Its widest verified square was 0.32 m. A peak 9 cm below the labelled floor came from an area-weighted triangle histogram. A second headset measurement counted each physical location once and found insufficient agreement to lower the whole room. Meta’s tracked floor was only 2.1 cm below the label.

The fitter now checks for a consistent lower level before comparing mesh heights. If the saved geometry still cannot fit a board on Quest 3, the Environment Raycast Manager measures a local patch of floor twice, aiming at locations 7.5 cm apart. Only neighboring, repeatable hits close to floor height can fill a gap or supersede shallow mesh noise. Furniture anchors, tall mesh geometry and repeated live obstructions still exclude their footprints. The connected-board search runs again with the new evidence. Turning to another open patch can trigger a fresh measurement without opening Space Setup. **Refit saved floor** also requests this measurement when a board already fits.

On the physical Quest, a view of the 18,198-vertex scan increased the verified square from 0.32 m to 0.48 m and still left too little continuous floor. When Space Setup supplied a 17,901-vertex mesh, the saved-only fit also failed. On this scan, live depth returned 756 repeatable floor samples and 1,146 local triangles. It identified 13 exclusion samples and verified three connected 40 cm panes covering 0.48 m². All 75 final pane probes hit, with zero confirmed obstructions. The game advanced from Welcome to Ready. The APK installed over the existing app and the headset reported SHA-256 `ddcac6ff33fb1d1c599a6c2d3aa3e884ba7350f69283647ab0ccaf0b47eba850`. [Signed APK](<../../Builds/Quest/Live-patch/FloorFracture-development.apk>) · [100 Edit Mode tests](<../../Builds/Verification/Live-patch/EditMode.xml>) · [Eight recorded rooms](<../../Builds/Verification/Live-patch/Recorded-rooms.tsv>)

On the successful fit the game counted 3.45 m² of usable floor, 3.94 m² of furniture, 4.99 m² of raised saved geometry, 2.57 m² without verified support and 0.01 m² of live exclusions. The last 75 probes were confined to the selected panes. These totals do not establish which of the rejected locations contained real objects. The Unity gameplay check passed 50 checks and all 24 ending exits. [Gameplay report](<../../Builds/Verification/Live-patch/Gameplay-report.json>)

The November 19 source at `131fe71` used the native floor outline and projected wall and furniture anchors for placement. The November 20 survey additionally requires positive support for each entire padded pane from the saved mesh or repeatable live returns. Missing triangle coverage or a small difference in reconstructed height can therefore reduce the layout even when the semantic room is unchanged. On the latest Quest run, stage telemetry reached Ready and stayed there for at least 42 seconds.

## Live-depth retry on Quest 3

At 17:04:51 on November 20, the previous connected-board build recorded a world-locked room with a 16,791-vertex mesh. The refined scan found a board and 4.11 m² of clear floor. The live pass labeled 1.62 m² obstructed and returned to recovery. That build excluded a whole padded pane for each veto. Its telemetry did not distinguish an actual hit from an occluded box edge.

The updated build uses Meta’s environment rays and checks measured changes at the same spot in a second observation. The game retains the measured obstruction when looking for another fit in that survey. All 95 Edit Mode tests passed, including the mixed-visibility and confirmed-object regression. Seven of eight Meta recordings passed the full saved-footprint check. [Edit Mode results](<../../Builds/Verification/Live-floor/EditMode.xml>) · [Recorded-room results](<../../Builds/Verification/Live-floor/Recorded-rooms.tsv>)

On the physical Quest, the previous run returned from Welcome to room recovery within 0.6 seconds on its first attempt. The corrected ARM64 APK installed over the same package and matched the local SHA-256, `0d7481f4412fd353edb9d7120e1f82a8b35e095b8d7178cc21c98949019fc069`. The native room reported five connected 45 cm panes covering 1.01 m². The first pass had no live samples in view. Stage telemetry showed 76 seconds at Welcome followed by stepping practice, stone practice and Ready. A later refit from Play area sampled the room with 115 hits, three unobserved rays and seven warm-up results. It confirmed the same five 45 cm panes with zero confirmed obstructions at 17:16:19. [Tested Quest APK](<../../Builds/Quest/Live-floor/FloorFracture-development.apk>)

## Connected boards in Meta’s recorded rooms

Seven of the eight installed Meta room recordings produced a board. Bedroom 2, previously rejected, now produced seven connected 40 cm panes in a 3 × 5 footprint. Exactly those seven pane objects appeared in the scene. Office 2 still lacked a continuous strip wide enough for two panes. Every accepted layout and its clearance were checked against its recording, and the exterior brick stayed hidden during ordinary play. [Room measurements](<../../Builds/Verification/Connected-floor/Recorded-rooms.tsv>)

The initial connected-board build passed all 95 Unity Edit Mode tests and 50 Play Mode checks. The Play Mode run used both Meta hand-ray prefabs to select recovery controls and refitted the saved room through the Play area menu. A GPU readback measured zero pixels of exterior brick during ordinary play. The six fall endings and 24 ending exits also ran. [Edit Mode results](<../../Builds/Verification/Connected-floor/EditMode.xml>) · [Gameplay report](<../../Builds/Verification/Connected-floor/PlayMode/report.json>)

The signed ARM64 Quest update built with zero errors and 42 warnings and installed over the existing app with its saved data. The Quest reported the same APK SHA-256 as the signed file, `f0caca1dc4def92b2eacfe49c98c18cada3cbba9c3058ae4512e9f1be595c4a8`. Both scene-permission entries were granted. Android accepted the launch, then USB disconnected before a new room fit or physical hand interaction could be observed. [Installed build](<../../Builds/Quest/Connected-floor/FloorFracture-development.apk>)

## What qualifies a pane

1. MRUK must finish loading. On the headset, the room must be local, world-locked and contain the tracked player on the matching storey. A usable semantic floor and readable global mesh are both required.
2. Meta’s **Find Spawn Positions** suggests floor locations. Floor Zone clusters overlapping spawn markers to size a fishing pond. Floor Fracture uses those suggestions and its existing rectangular fitter, then proves the occupied space. [Meta’s Floor Zone sample](https://developers.meta.com/vr/documentation/unity/unity-sample-mruk-floor-zone/) · [Sample implementation reviewed](https://github.com/oculus-samples/Unity-MRUtilityKitSample/blob/b1e52623f3852c461096668d1796f2948dcae49a/Assets/MRUKSamples/FloorZone/scripts/FishTank.cs)
3. Each pane and its 8 cm foot margin must fit inside Meta’s labelled floor boundary and clear mapped furniture and walls. The floor label supplies the surface across gaps in the global mesh. A raised mesh exclusion needs geometry over 10 cm above both the board and estimated mesh floor, at least two distinct raised vertices connected through the mesh, and either an 8 cm horizontal span along a world axis or 15 cm of upright structure above the cutoff. Repeated copies of one spike contribute one raised vertex. A measured lower surface more than 12 cm down can also exclude a cell where the mesh shows a drop.
4. Meta’s Environment Raycast Manager samples 25 positions on each visible padded pane. Two nearby returns on a raised object or drop exclude that spot. Occluded and out-of-view rays leave the mapped floor eligible. Monitoring continues during play. [Meta environment raycasting](https://developers.meta.com/vr/documentation/unity/unity-mr-utility-kit-environment-raycast/)
5. The fitter compares playable glass area for rectangular and connected layouts. When the coarse rectangular search fails, a finer survey checks additional headings. Every selected pane must still pass the clearance check.
6. The room and mesh are read again before glass appears. An anchor update during fitting discards the old result and schedules a fresh check. If no complete board qualifies, the recovery page reports the width the saved scan supports or that live depth vetoed the placement. The rectangular search needs 1.16 m across. Its fallback fits two or more edge-connected panes down to 40 cm each. The smallest pair needs a 0.96 × 0.56 m clear strip including the same foot clearance. A live depth hit confirmed twice on a playable pane away from the wearer starts a fresh fit around that spot. **Check again** reloads the saved room in Floor Fracture. **Space Setup** opens the Quest system scanner to capture an updated room.

## Read the temporary overlay

In the Editor or a development player, choose **Show / hide placement** on the **Find a clear floor** panel. Once Welcome appears, open **Play area → Placement debug**. Either control toggles the overlay. The Inspector’s **Show Placement Debug** setting and the following development launch option also enable it.

```sh
adb shell am start -S -n com.wa003.floorfracture/com.unity3d.player.UnityPlayerGameActivity --ez floorFracturePlacementDebug true
```

| Mark | Meaning |
|---|---|
| Green square | Outline of an accepted playable pane |
| Red | Furniture or wall anchor |
| Orange | Coherent raised structure or a measured drop in the saved mesh |
| Purple | The room has no usable global mesh |
| Blue | Scanned floor boundary |
| Yellow | Repeated live depth returns indicate an obstruction or drop |
| Pink | Live floor measurement was uncertain |
| Brown | Previously declined area |

Colored contours follow the scan evidence inside the room. The only tile outlines are accepted panes. If the headset cannot locate any floor, the recovery panel explains what is missing. A completed development survey writes the latest stage, supported width and areas for each rejection reason to `Last-floor-survey.txt` in the app’s external files directory. The report contains aggregate measurements. It can be read after reconnecting the Quest.

```sh
adb shell cat /sdcard/Android/data/com.wa003.floorfracture/files/Last-floor-survey.txt
```

In the earlier rectangular build, in Meta’s recorded Bedroom 2, the actual game panel measured 0.84 m on both tries. Toggling the overlay drew the rejected floor, all three controls stayed within 0.5 mm of their starting positions, and the glass stayed hidden. [Recovery screen](<../../Builds/Verification/Room-fit-recovery/Recovery.png>) · [Same screen with rejection reasons](<../../Builds/Verification/Room-fit-recovery/Recovery-overlay.png>)

![Top-down placement diagnostic over a Meta recorded living room](./Observed-floor-overlay.png)

## Earlier rectangular build verification

- Unity `6000.6.9f1`, Meta XR Core and MRUK `209.0.0`, Unity OpenXR `1.19.0`, Unity OpenXR: Meta `2.7.0` and URP `17.6.0`. Read the installed implementations of `MRUKAnchor.GlobalMesh`, `LoadGlobalMeshTriangles`, `Raycast` and `CheckBox` before integrating them.
- All 91 Edit Mode tests passed. Cases include a 1 cm interior gap, duplicate triangles, a chair leg, furniture in the foot margin, a 2.5 cm raised slab, corrupt or empty scans, a relocated mesh, live hit statuses and overlay colors. A 1.28 m wide aisle at 37° now fits. A 6 cm gap between two 0.97 m strips still prevents a board. Separate tests cover a shallow triangle crossing the floor-height limit and an overhead obstruction crossing standing height. [Local Unity test results](<../../Builds/Verification/Room-fit-recovery/EditMode.xml>)
- Signed ARM64 development APK for `com.wa003.floorfracture`, minimum Android API 32, target 34. Unity reported zero errors and eight warnings. APK signature scheme v2 verified. SHA-256 `a9c6143f0d63abd638efbb25aea95fa5655854d589433bcac327745b080dbd93`. [Local APK](<../../Builds/Quest/Room-fit-recovery/FloorFracture-development.apk>)

## Earlier rectangular build, recorded rooms and gameplay

The same placement source used for the APK loaded all eight installed Meta room recordings through MRUK in Unity Play Mode. Each accepted pane and its foot clearance passed the survey again. Bedroom 2 and Office 2 had no complete supported 2 × 2 rectangle. Each returned to room recovery.

| Meta recording | Grid | Pane width |
|---|---:|---:|
| MeshBedroom1 | 3 × 2 | 50.3 cm |
| MeshBedroom2 | 0.84 m clear square measured | — |
| MeshBedroom3 | 2 × 2 | 55.7 cm |
| MeshLivingRoom1 | 3 × 9 | 50.1 cm |
| MeshLivingRoom2 | 3 × 2 | 61.1 cm |
| MeshLivingRoom3 | 5 × 2 | 59.6 cm |
| MeshOffice1 | 2 × 2 | 51.3 cm |
| MeshOffice2 | 0.64 m clear square measured | — |

The integrated gameplay run on the 3 × 9 living-room board passed 48 checks, with 53 captures and 24 ending exits. It exercised both simulated hands, recovery controls, moving glass, scoring and fracture. A regression interrupted a real in-progress fit with an MRUK anchor update. In the previous build the game stayed blocked after the floor was usable. The revised game kept the glass hidden through the update and automatically reached Welcome. The 58 project C# files were byte-identical in the Editor test, Play Mode and Android build copies. The exterior-brick GPU test allows up to two silhouette pixels within 0.1 mm of a scanned edge when comparing shifted, overlapping floors. [Local gameplay report](<../../Builds/Verification/Room-fit-recovery/PlayMode/report.json>) · [Recorded-room measurements](<../../Builds/Verification/Room-fit-recovery/Recorded-rooms.tsv>)

## Earlier rectangular build, Quest installation

Android resumed the game after the update, both scene permission entries were granted, and the Quest's installed package matched the SHA-256 above. At the check with the headset asleep, the SDK reported zero localized rooms. In the wearer's earlier screenshot, the app had already read a room and reached the later “No complete 2 × 2 board fits” step. Its message alone cannot identify which floor evidence was missing.

The wearer observed the hand ray rotating only inside Space Setup. The Quest activity log showed the game handing the screen to `com.oculus.guardian` and receiving it back. Horizon OS supplies the pointer on that screen. Inside the game, menus use Meta’s tracked hand-pointer pose and support **Check again** without opening the system scanner. [Meta SDK 209 Space Setup reference](https://developers.meta.com/vr/reference/unity/v209/class_o_v_r_scene/)

## Next headset check

In the same saved room, face the open floor and select **Check again** once. Record the dimensions at Welcome or the measured-width or live-depth message on recovery. **Show / hide placement** exposes the evidence from that screen. Compare a chair leg, a furniture corner and an incompletely scanned edge with the overlay before starting a wave. Then test a small object moved with the headset off before refitting. A saved room model can miss objects moved since scanning.
