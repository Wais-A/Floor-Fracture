# Floor Fracture

Wais Almakaleh · CSCI 379, Design and Development for XR · Meta Quest 3

[Master project checklist](<./Floor Fracture — Master Project Checklist.md>) · [Course design PDF](<./Docs/Floor Fracture Design Document.pdf>)

## What the player sees

The player sees their real walls and furniture. Matte brown-gray bricks fill the visible scanned floor around a rectangle of smoky glass. The furniture sits on the masonry, with floor visible between individual chair legs. A small ring marks the pane under the player's head. A number floats near the far edge. Cracked edges pulse while the timer counts down. At zero every marked pane breaks together. From a neighboring safe square, the player sees fitted shards fall into the opening. If their own support gives way, the shaft grows around them, a surprise ending plays, the view fades to black, and a results panel offers Restart and Quit.

## Why this Unity stack

| Approach | What it provides | Project trade-off |
|---|---|---|
| Unity OpenXR, Unity OpenXR: Meta and AR Foundation | OpenXR input and Unity's AR abstractions for passthrough and captured room geometry. Portable gameplay code can run on another supported device with a room provider. | The room setup, floor-fit behavior, interaction assets and sample visuals would need separate integration. Device-specific capabilities still need device-specific verification. |
| Meta XR Core, Interaction and MRUK with Unity's Android OpenXR loader | The previous course exercises use these packages. MRUK supplies room anchors and floor polygons. The Floor Zone sample demonstrates a portal in a real floor. | Scene setup, permission flow and sample assets are Quest-specific. Board rules use independent coordinates and seeds so another room provider could reuse them. |

Wais approved Unity `6000.6.9f1`. The project uses URP `17.6.0`, Meta XR `209.0.0`, Unity OpenXR `1.19.0`, Unity OpenXR: Meta `2.7.0`, ARM64 and Android package `com.wa003.floorfracture`. Use the SDK's Building Blocks for the tracked rig, passthrough, controllers, MRUK and stone. One world-space menu receives Meta ray and fingertip poke events. Room Guardian outlines nearby scanned objects, and Meta's environment-depth material lets a foreground object cover the practice stone. Project code selects valid footprints, decides wave outcomes and presents the glass and ending effects. [Unity OpenXR: Meta](https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.7/manual/index.html) · [Meta Building Blocks](https://developers.meta.com/vr/documentation/unity/unity-building-blocks-overview/) · [Meta Floor Zone](https://developers.meta.com/vr/documentation/unity/unity-sample-mruk-floor-zone/)

## First minute

1. Load the captured room and ask for spatial data when needed. The player can start Space Setup if the headset has no usable room.
2. Meta's Find Spawn Positions block proposes patches in the saved scan. The board fitter checks the whole rectangle against the floor perimeter and scanned obstructions, with 40 cm on all sides. Small rooms can use 50 cm square panes. The next Quest trial grows both pane width (up to 67 cm) and the number of panes (up to eight along each axis). Choose the fit that gives the most room to move between outer pane centers while preserving the 40 cm clearance. Welcome shows the measured dimensions over the fitted glass. Start is available directly. Play area offers another spot and Space Setup.
3. Touch a menu button with one fingertip, point and pinch with a hand, or point and pull the controller trigger. An open menu returns just in front of the player after a Quest system recenter. The optional tutorial presents a grabbable stone and a marked pane. Letting go over the pane demonstrates the shatter. A miss returns the stone. The player then steps between two practice positions to learn the ring.
4. Stand on an intact pane and start the first Classic wave. Its five-second countdown gives the player time to find a stationary one-step route. Moving early keeps the original timer running.

## Wave rules

- Every wave schedules the starting square to break. At least one orthogonally adjacent stationary square survives. Surviving stationary panes stay connected, allowing the next wave to start from any survivor.
- Possible outcomes at zero are supported by surviving glass, supported by breaking glass, and in a gap. Only the first survives. During the countdown a 0.3-second trial grace allows crossing a pre-existing gap. Leaving the board or losing tracking pauses the wave and restores a view of the room.
- Classic shows the warnings until zero. Memory briefly reveals the same cracks and edge pulses, then hides them while the timer, ring and holes stay visible. Last Tile leaves a single reachable stationary square and awards the level. The board rebuilds and waits for readiness.
- Later levels shorten the timer, add a low fog bank, then a direction-marked sliding pane over a hole. The moving pane locks on contact. A stationary escape route remains available. The timing floor and practical reach need physical playtests.

## Loss and replay

On a full-motion loss, the player should see the tile break, successive ledges pass at eye level and the landing surface approach from below. After contact, water slowly passes eye height with bubbles and muffled sound, or flames and embers surround the player. The headset trials made the landing seem immediate or the descent too slow. Full motion now starts from rest and accelerates visually at 9.8 m/s², covering five metres in about 1.01 s. The tracked eyes stay at the player’s physical pose. Reduced motion fades into a stationary reveal and Skip goes to results. A separate shuffle selects water, lava, crystal, space, sand or snow, with no immediate repeat between shuffles. Crystal lights travel around the shaft, space opens into stars, sand fills a buried chamber, and snow builds to a whiteout. Each ends in black before results. Restart clears the run and restores passthrough.

## AI after the core headset milestone

Wais chose fixed spoken tutorial and results lines, generated once and bundled for offline Quest playback. He will choose a provider and voice after the revised gameplay test. Spoken prompts should leave time to watch the stone drop or find the marker. The same instructions and score stay visible. A later optional **Why did I lose?** experiment can use the recorded wave type, deadline and supporting tile while the player is stationary.

Before an AI trial, Wais chooses the feature and provider. Meta's SDK supports different execution locations per provider. Confirm that the chosen model runs on Quest 3 as intended, measure latency and memory, and compare frame times on the same build. An optional explanation can operate on game events without sending a room image. [Meta AI Building Blocks](https://developers.meta.com/vr/documentation/unity/unity-ai-building-blocks-overview/)

The installed 209 `UnityInferenceEngineProvider` implements text-only chat and requires a compatible model and tokenizer. That makes the results coach a concrete offline experiment. The installed Text To Speech block has OpenAI and ElevenLabs providers for generating the clips during authoring. The shipped clips can play from the APK with networking off. Test the coach on a known breaking start, a Memory recall error and an expired gap grace. The answer must match the recorded cause in every case, and the ordinary Results controls must work while it runs.

## Brick around the playable glass

Dry, warm brown-gray brick starts at the glass perimeter and runs to each edge of the scanned floor. The half-brick offset and recessed mortar match the fall shaft. Each course is 27 cm deep and each brick is 54 cm long. Room-scale coordinates keep joints continuous across floor patches. The decorative brick in the clearance margin provides no playable support.

Meta’s MRUK **Effect Mesh** builds the surface from floor anchors. Meta’s **Occlusion** block supplies live per-eye depth so a couch, chair legs and a trash can appear in front of the brick. Open space between chair legs and the spot a bin just vacated reveal the floor finish. Quest shows the real floor while the depth provider is unavailable. The glass footprint, including holes, stays open through the surround. Wall treatment awaits Wais’s review. [Meta floor meshes](https://developers.meta.com/vr/documentation/unity/unity-mr-utility-kit-manage-scene-data/) · [Meta depth mask sample](https://developers.meta.com/vr/documentation/unity/unity-sample-depth-api/)

The Editor checks cover a couch profile, the gap between two chair legs and two locations for a bin. In the standalone simulator, the High-ceiling Room supplied 25 anchors and fitted a 5 × 8 board. The physical Quest trial checks object edges, flicker and the fit in Wais’s room. [Brick at player height](<./Docs/Media/brick-player-height.png>) · [Simulator measurements](<./Docs/Verification/Brick floor simulator.json>) · [Quest build](<./Docs/Verification/Brick floor Quest build.json>)

## Where the game lives

Open `Assets/FloorFracture/Scenes/FloorFracture.unity`. Its **Floor Fracture** object has `GameFlow` and references the room provider and tracked rig. **Tools → Floor Fracture → Prepare game scene** reapplies the configured SDK blocks and keeps the main scene in the Android build list.

| Responsibility | Unity source and verification point |
|---|---|
| Room, permission and footprint | `Scripts/Room/RoomBoardPlacement.cs` on the MRUK instance owns loading, permission recovery, automatic placement and relocation. Meta's Find Spawn Positions offers floor candidates. `MrukFloorReader` reads floor and obstacle shapes. `BoardFitter` validates the rectangle and margin. The Rec Room fixture is Editor-only. [Complete Building Blocks review](<./Docs/Meta Building Blocks.md>) |
| Tracked rig and direct grab | SDK Camera Rig, Passthrough, Controller Tracking, Interactions Rig and MR Utility Kit blocks are serialized in the scene. `Resources/PracticeStone.prefab` carries the SDK Grab Interaction block and a Rigidbody. `Scripts/Game/PracticeStone.cs` handles the lesson, release, gravity, floor crossing and miss recovery. |
| Controller menus | The scene has one world-space canvas with Meta’s Ray Canvas and Poke Canvas Quick Actions. Both SDK surfaces dispatch to the same choices. `Scripts/Menu/GamePanel.cs` owns the menu choices, hover feedback and positioning. Desktop fixture input is Editor-only. |
| Rules and reproducibility | `Scripts/Rules/BoardState.cs` owns intact glass, score, wave pattern and deadline. `WavePlanner.cs` constructs connected, reachable survivors from a seed. `PlayerTileTracker.cs` projects the tracked head into board coordinates, handles support, and samples at the deadline. Each wave logs its seed, starting cell, safety choices and deadline. |
| Timing, decisions and replay | `Scripts/Game/GameFlow.cs` owns countdown, exactly-once scoring, level rebuild, pause, loss and restart. It tells the presentation what happened. Focus or tracking loss during a wave cancels the countdown. |
| Glass and shafts | `Resources/GlassTile.prefab`, `Scripts/Glass/GlassGeometry.cs` and `GlassTile.cs` share fitted warning and shard geometry. `GlassBoardView.cs` combines adjoining shaft walls. `ShardPool.cs` batches falling fragments into one mesh. Shader resources mask geometry beneath the real floor. |
| Endings and audio | `Scripts/Presentation/Endings/FallSequence.cs` selects an ending and sequences blackout and results. `FallScenery.cs` and `FallMeshes.cs` build the shaft and hazards, with original material and audio resources plus the credited water sample. `GameSound.cs` schedules centered ticks, directional impact and echo using the stock Spatial Audio block prefab. |
| Measurements | `Scripts/Diagnostics/QuestRunMetrics.cs` records supported Unity counters in Android development builds. Pair its local CSV with the headset's OVR Metrics capture. [Performance guide](<./Docs/Quest performance.md>) |

Paths in this table are relative to `Assets/FloorFracture/`. The initial tuning values are an 8 mm marker tolerance between equivalent stationary surfaces, 0.3 s to cross a gap, five seconds on level 1, and a 0.4 s reduction per level to a provisional 2.5 s floor. A sliding surface spans 60% of its pane width and travels up to 18% of that width from its hole center. At 50 cm pitch that gives a 30 cm square and 9 cm of travel. The headset determines the final comfort values. [Seeded reproduction instructions](./README.md#reproduce-and-verify)

## Evidence and delivery

| Milestone | Evidence |
|---|---|
| November 20, initial design | Editable design, eight-page PDF and course template in `Docs/`. Submission is recorded only when confirmed. |
| December 2, interaction | Wais demonstrated early survival, neighboring glass shattering and a deliberate loss on Quest 3 on November 19. His feedback opened revisions to the fall, room prompt and board scale. [Headset playtest](<./Docs/Verification/Quest 3 playtest.md>) |
| December 9, final design and repository | Verify a clean checkout, record the shipped features, headset measurements, and two outside players' actual feedback. |
| December 11, video | Capture and review 3–5 minutes of real headset footage, with an adjacent shatter, placement, stone, advanced wave, loss and comfort control. |

The approved packaging baseline is Git `3265e4b`. See the checklist for per-feature evidence, the current scene and outstanding physical tests. Decisions about third-party assets and their source redistribution belong in the asset credits before import.
