# Floor Fracture — Master Project Checklist

This is the working checklist for the [Floor Fracture Unity project](<./>). It contains **140 numbered implementation items** and a preflight record. Review the evidence for the current task before advancing. Ask Wais about asset choices, account details, or project decisions that need his input.

## Resume here

| Field | Current state |
|---|---|
| Project | *Floor Fracture*, a solo mixed-reality survival game for Meta Quest 3 |
| Current phase | Implementation, November 19, 2026. Wais approved the tested Unity version and asked to proceed through the entire plan with verification. |
| Selected tools | Unity 6000.6.9f1, Universal Render Pipeline, Meta XR SDK, Meta Interaction SDK, and Mixed Reality Utility Kit (MRUK), using OpenXR underneath |
| Last completed decision | Wais approved `com.wa003.floorfracture` as the Android package name. For gameplay, prioritize shrinking glass, Memory, Last Tile, fog and sliding tiles. Build water and lava first. |
| Next action | Integrate and verify the tracked rig, real room, stone and first wave. Wais will wear the connected headset when the playable build is ready. |
| First headset milestone | One playable wave in the real room by December 2. The player can see a warning, move to safety, survive at zero, or deliberately stay and lose. |
| Evidence available | The live Editor compiled with Android OpenXR. A clean import and a graphical reopen of a project copy both loaded Meta XR Audio. The signed APK contains the approved package name. Meta's Android report passed 52 of 52 required checks. The course draft and PDF have eight pages in the template revision and the submission record maps features by page. Physical Quest 3 checks are open. The deterministic rules and floor-fitter checks are recorded below. |
| How to read the checkboxes | `[DOC]` means documented, `[EDITOR]` means observed or tested in Unity, `[BUILD]` means verified in a standalone Android build, and `[HEADSET]` means observed on the physical Quest 3. Check each separately. |

### Preflight before item 001 · November 19, 2026

- [x] **[EDITOR] Identify the installed stack.** Unity `6000.6.9f1`, URP `17.6.0`, Meta All-in-One, Core, Interaction, MRUK, Audio, Haptics and Platform `209.0.0`, Voice `85.0.1`. The installed Meta versions matched the Unity registry and Meta's [release notes](https://developers.meta.com/vr/release-notes/).
- [x] **[EDITOR] Check the reported Audio failure.** A project copy with an empty Library compiled on its first launch and selected `Meta XR Audio`. A separate graphical reopen on Android reported `compilerFailed=False`, ran the audio engine and exited normally. Both runs exited 0. Keep Audio installed on this tested baseline. [Meta Audio 209 release notes](https://developers.meta.com/vr/downloads/package/meta-xr-audio-sdk/)
  - If the two Audio compile errors described in HW11 return, capture the first red Console error. The handout's fallback installs `com.meta.xr.sdk.core`, `com.meta.xr.sdk.interaction.ovr` and `com.meta.xr.mrutilitykit` individually. In a test copy, pin those packages, retain any other SDKs the project uses, remove both the All-in-One wrapper and direct Audio dependency, and switch the Meta spatializer and ambisonic decoder to None for Unity's built-in audio. Reopen and rebuild before adopting the change. [HW11 handout, pp. 8–9](</Users/wais/Downloads/hw11 - Mixed Reality II.pdf>)
  - One automated report run on the test copy aborted during Editor shutdown. macOS recorded `SIGABRT` in the Interaction SDK library `ISDKEngineTelemetry.dylib`. The report retry waited for SDK package discovery and exited 0. The graphical reopen also exited 0. If this recurs, compare which library and thread crashed and whether it happened at launch or quit. [macOS crash report](</Users/wais/Library/Logs/DiagnosticReports/Unity-2026-11-19-180809.ips>)
- [x] **[EDITOR] Repair Android XR configuration.** The initial target was macOS and the Android loader list was empty. Installed Unity OpenXR `1.19.0`, assigned the Android OpenXR loader, enabled Meta Quest support and Touch/Touch Plus, and switched the active and selected targets to Android. Applied the SDK's hand-tracking subsystem fix for its enabled data source. Unity's Android OpenXR validation subsequently reported 0 blocking errors. The three advisories concerned mobile ambient occlusion, input-polling latency, and a feature requesting an older OpenXR API patch.
- [x] **[EDITOR] Set the local Android identity.** Wais approved `com.wa003.floorfracture`. Read it back from Android Player Settings. The starting scene remains `Assets/Scenes/SampleScene.unity`.
- [x] **[DOC] Interpret the two manual dashboard recommendations.** Meta Core displays both rows when the Platform SDK is installed. Leave both acknowledgments open. The current template scene uses no Platform feature that requires a Data Use Checkup. Reassess if a feature such as a Meta leaderboard or friends list is added. [Meta's Data Use Checkup guide](https://developers.meta.com/vr/resources/publish-data-use/)
  - The Android package name is set and verified in the APK. If Floor Fracture is registered with Meta for Platform services, use that same package name. Find the app's numeric Quest App ID under **Developer Dashboard → Development → API**. In Unity, enter it under **Window → Meta → Platform → Edit Settings → Meta Quest**. Only acknowledge the row after verifying both values against the registered app. [Meta's App ID instructions](https://developers.meta.com/vr/documentation/unity/unity-platform-entitlements/)
- [x] **[DOC] Check available updates.** Unity's release service lists `6000.6.10f1` on November 19. At the audit, Rider integration was `3.1.0` (registry `3.1.1`), transitive XR Hands was `1.9.0` (registry `1.10.0`), and the Unity CLI was `1.0.0-beta.17` (CLI reported `1.0.0` available). The Editor patch is a separate installation decision. [Unity patch notes](https://storage.googleapis.com/live-platform-resources-prd/templates/assets/6000_6_10f1_0f5edadb71/6000_6_10f1_0f5edadb71.md)
- [x] **[EDITOR] Verify the final Meta Android setup report.** Updated the SDK-generated supported-device list to remove the obsolete first-generation Quest entry. Quest 3 is still present. Meta's tool then evaluated 100 rules on a project copy with the repaired manifest. All 52 required and all 11 optional rules passed. The two dashboard items above are the only open recommendations. [Meta Android setup report](<./Builds/Preflight/MetaAndroidSetup.json>)
- [x] **[BUILD] Build and inspect the signed standalone APK.** Unity `6000.6.9f1` built `SampleScene` from the working project. Android's tools verified the APK signature, `com.wa003.floorfracture`, ARM64, minimum Android API 32 and target API 34. The APK includes Quest 3 in its manifest and contains both the OpenXR loader and the Meta XR Audio native library. The build recorded 0 errors and 637 warnings, including 633 shader warnings and 4 configuration advisories. This APK tests packaging. Passthrough, controllers and floor alignment need a rig and a physical headset test at item 020. [APK](<./Builds/Preflight/FloorFracture.apk>) · [Unity build report](<./Builds/Preflight/UnityAndroidBuild.json>)
- [x] **User review.** Wais approved Unity `6000.6.9f1` and directed implementation to proceed. The tested Android setup is recoverable at local Git commit `3265e4b`. The in-headset passthrough and controller test belongs to item 020 after the rig exists.

### Implementation evidence · November 19, 2026

- The tested baseline is local Git `3265e4b`. The active implementation branch is `floor-fracture-game`. No release has been published.
- Wais approved the Meta Floor Zone water, hole and splash assets with MIT attribution, and the Creative Core fire only where licensing permits. Raw course fire redistribution could not be verified, so the lava effect uses original flames. [Asset credits](<./Docs/Asset credits.md>)
- The working eight-page course draft has all 11 sections. A byte-identical copy of the original professor template is in `Docs/`. Wais chose written descriptions in place of the four previously proposed sketches (items 006–009). The eight-page course draft and repository omit the drawings. See the current course submission record for page references and outstanding Moodle status. [Initial submission record](<./Docs/Initial design submission.md>)
- Standalone C# checks using Unity 6000.6.9f1 assemblies covered 1,680,855 wave patterns, all nine board sizes, every starting cell and Last Tile progression. The room fitter passed 17 cases, including rotated footprints, narrow obstructions and unsafe margins. Live scene and physical tests have separate checkboxes.

**What the finished game should look like.** Put on the Quest and you can still see your walls, couch, and real floor. On an open patch of that floor sits a grid of smoky glass squares. You step onto one, and a small marker shows which square the game thinks you occupy. A countdown begins. Several edges start pulsing and branching cracks spread across the glass. You move to a sound tile. Nothing drops early. At zero, the damaged panes break into pieces that tilt, catch the light, and tumble into dark shafts. You can stand on intact glass and watch the fragments fall beside you. The next countdown plays on the glass that remains. Eventually there is one last safe tile. Reach it and the board rebuilds for a harder level. Lose and the shaft walls rise around you. At the bottom might be water that slowly passes over your eyes, lava that surrounds you with flames, or another surprise. The picture fades to black, then you see your result, Restart, and Quit.

**Agreed rules that future work must preserve.** The starting tile breaks every wave, so standing still cannot win. Ordinary stepping must always work. The tile under the headset determines occupancy. All scheduled glass breaks happen at zero. Holes persist throughout the level. Classic and Memory mix during the shrinking sequence. Last Tile finishes each level. Sliding tiles lock after landing. A brief grace period allows crossing an existing hole. The six endings are independent of the gameplay rules. Multiplayer is a future possibility.

## A. Prepare the design submission and project record

- [x] **001 — [DOC]** Save this checklist in the project root and put a link to it near the top of `FLOOR_FRACTURE.md`, the main design document.
- [x] **002 — [DOC]** Copy the professor’s editable Final Project Design Doc into the project documentation area. Preserve the original template in Downloads.
- [x] **003 — [DOC]** Enter the title, solo developer, course, target headset, and project pitch. Identify the audience as players who can take short physical steps in a clear room, including first-time XR players.
- [x] **004 — [DOC]** Describe why the game belongs in MR. The player recognizes their own room, judges virtual glass relative to their real body, and physically moves between surfaces.
- [x] **005 — [DOC]** Record the SDK comparison. Unity OpenXR with AR Foundation supports passthrough and room data. Meta XR was selected for the familiar HW11 setup and MRUK’s closely matched Floor Zone sample. [Unity OpenXR: Meta](https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.7/manual/index.html) · [Meta Floor Zone sample](https://developers.meta.com/vr/documentation/unity/unity-sample-mruk-floor-zone/)
- [x] **006 — [DOC]** Describe the six moments of the first minute in writing. Page 4 of the course draft covers the room, confirmation, stone, warning, step and break.
- [x] **007 — [DOC]** Describe what the player sees during a wave in writing. The main design document records the ring, safe and cracked panes, persistent openings and countdown at the far edge.
- [x] **008 — [DOC]** Describe the loss sequence in writing. Page 7 of the course draft covers the tile giving way, rising walls, hazard, blackout and results.
- [x] **009 — [DOC]** List the main menu and results controls in writing. Pages 4–5 and 7 identify Start, Settings, Tutorial, Restart and Quit.
- [x] **010 — [DOC]** Map the course requirements to features: controller ray UI, physics-based stone grab, placement on the real floor, settings, haptics, spatial audio, comfort, testing, and headset performance.
- [x] **011 — [DOC]** Enter the official dates into the timeline: initial PDF November 20, headset interaction December 2, final repo and design doc December 9, video December 11.
- [x] **012 — [DOC]** Export and visually inspect the November 20 PDF. Record its filename and submission status. Leave future tester findings and performance measurements explicitly pending. [Course template](</Users/wais/Downloads/Final Project - Design Doc.docx>) · [Course instructions](</Users/wais/Downloads/Final Project - Instructions.pdf>)

## B. Establish a reproducible Quest project

- [x] **013** Create a Universal 3D project using Unity **6000.6.9f1**, the locally installed version used by HW11. Record the editor version in the README and main plan.
- [x] **014** Configure Git, a Unity `.gitignore`, and Git LFS before importing large assets. Verify that `Assets`, `Packages`, `ProjectSettings`, and required `.meta` files are tracked.
- [ ] **015** Organize project-owned scenes, scripts, prefabs, materials, audio, and VFX under one `FloorFracture` asset folder. Keep imported sample content identifiable for updates and credits.
- [x] **016** Use HW11’s inspected **209.0.0** Meta Core, Interaction, and MRUK versions as the starting baseline. Verified all three in the project's `Packages/packages-lock.json` during preflight on November 19.
- [ ] **017** Configure the Android Quest build profile, ARM64, URP, and OpenXR loader. Run Meta’s Project Setup Tool and record any setting that differs from HW11.
- [ ] **018** Create the main scene with one tracked camera rig, passthrough, left and right controller tracking, and controller ray input. Add the scene to the active build profile.
- [x] **019 — [BUILD]** Build the smallest standalone APK. Record the package identifier, build filename, Unity version, and Git commit so this working baseline can be recovered. Verified the packaging baseline at `3265e4b`, Unity `6000.6.9f1`, `com.wa003.floorfracture`, `Builds/Preflight/FloorFracture.apk`. The tracked-rig APK will receive its own record.
- [ ] **020 — [HEADSET]** Launch that APK. Confirm the real room is visible, the camera follows head movement, floor height feels correct, and both controllers track independently.
- [x] **021** Start the asset-credit record with creator, source, version, license, local asset path, and permission to include the source asset in the course repository.
- [x] **022** Inventory the local Floor Zone stencil, water, underwater shading, and splash assets, plus the Creative Core fire particle prefab. Record which dependencies each candidate needs before copying it. [Floor Zone assets](</Users/wais/Projects/Bucknell Projects/CSCI 379/Unity-MRUtilityKitSample/Assets/MRUKSamples/FloorZone>) · [Fire prefab](</Users/wais/Projects/Bucknell Projects/CSCI 379/CreativeCore_VFX_U6.3/Assets/CreativeCore_VFX/VFX/Samples/Fire/Fire_ParticleSystem_Prefab.prefab>)

## C. Find a real patch of floor that can hold the board

- [ ] **023** On launch, show passthrough and a short room-loading message while MRUK waits for spatial permission and a current room.
- [ ] **024** Handle the three startup outcomes separately: a usable room, permission denied, and permission granted with no saved room. Give the player a specific recovery action for each failure.
- [ ] **025 — [EDITOR]** Load an MRUK sample room and display temporary floor and furniture outlines. Use this fixture for development when the headset is unavailable.
- [ ] **026 — [HEADSET]** Complete Space Setup in the intended demo room, launch the app, and verify the detected floor and furniture align with their physical counterparts.
- [ ] **027** Adapt the Floor Zone sample’s open-space search to propose board positions. Use the scanned floor outline and furniture footprints when selecting candidate areas.
- [ ] **028** Start with **0.5 m square tiles**, rectangular boards from **2×2 through 4×4**, and **0.4 m clearance on each side**. Mark these as first-playtest values and record later changes.
- [ ] **029** Try the largest supported footprint first. Evaluate rectangular layouts such as 2×3 or 3×4 when a square board would waste usable space.
- [ ] **030** Validate the full board and its margin. Reject placements where a corner crosses the floor boundary, furniture intersects the middle, or a narrow obstruction divides the play area.
- [ ] **031** Preview accepted placements as a faint full-size grid with an outer clearance outline. Keep the countdown disabled while the player looks around.
- [ ] **032** Require player confirmation that the physical area is clear. If the scan missed a chair, bag, or other object, allow the player to decline and retry setup.
- [ ] **033** If no minimum board fits, keep passthrough visible and explain that the player can clear more floor or rescan. Preserve the full tile size.
- [ ] **034 — [HEADSET]** Test a spacious layout, a narrow layout, furniture near a board corner, and insufficient space. Record the selected dimensions and whether all tiles were comfortable to reach.

## D. Build the first-time experience and controls

- [ ] **035** Place the welcome panel in front of the player at a readable height. It should be reachable by controller ray from a normal seated or standing menu position.
- [ ] **036** Introduce the game with one instruction the player can act on: *Move to glass that will hold before the countdown reaches zero.*
- [ ] **037** Give either controller ray a visible hover state and trigger-to-select input. Confirm selections with a button-state change, short sound, and light haptic pulse.
- [ ] **038** Create a practice stone with a Rigidbody, collider, and direct-grab interaction. Present it within easy reach so the player can learn without bending to the real floor.
- [ ] **039** Put one practice pane in front of the player. Demonstrate the same edge pulse and branching crack pattern that will signal danger during actual rounds.
- [ ] **040** When the player drops the stone onto that pane, shatter the glass and let the stone visibly fall through. Play the break at the pane and an echo from below.
- [ ] **041** If the stone misses, return it to its reachable starting position. Let the player repeat or skip the tutorial without redoing room setup.
- [ ] **042** Teach the occupied-tile marker by asking the player to step from one intact practice tile to another. Start the first countdown only after the player is ready.
- [ ] **043** Add settings for full or reduced-motion falls, warning intensity, and sound volume. Preview the warning setting on a sample tile so its effect is immediately visible.
- [ ] **044 — [HEADSET]** Complete setup using each controller, intentionally miss with the stone, replay the tutorial, skip it, and change each setting. Record any point that requires verbal coaching.

## E. Define what the glass looks like and where the player stands

- [ ] **045** Build one reusable intact tile prefab. Start with a smoky, lightly reflective pane, a distinct rim, and narrow seams that make each square readable against the real room.
- [ ] **046** Keep safe panes visually quiet. Give dangerous panes both a pulsing edge and a branching crack pattern so the player can recognize danger without relying on color alone.
- [ ] **047** Give the occupied tile a separate marker, such as a small ring near its center. It must remain identifiable when that same tile is also flashing and cracking.
- [ ] **048** Anchor the countdown near the board where a player scanning the floor can read it. Keep the number clear of the occupied marker, warnings, and final safe tile.
- [ ] **049** Project the headset’s horizontal position onto the board plane. Use the board’s local coordinates so a rotated grid still selects the correct tile.
- [ ] **050** Determine occupancy from the supporting tile beneath that projected point. Normal steps and optional hops use the same rule.
- [ ] **051** Apply a small border tolerance to the visual occupancy marker. Test it against the actual resolution rule so the game never displays one tile and judges another.
- [ ] **052** Distinguish three conditions: standing on supported glass, crossing an existing hole inside the board, and leaving the confirmed board footprint.
- [ ] **053** For an existing hole, try a **0.3-second crossing grace period**. Reaching supported glass before it expires saves the player. Tune the duration on Quest.
- [ ] **054** At countdown zero, evaluate the actual supporting surface once. Being above a gap at that instant loses, even if some crossing grace remains.
- [ ] **055** Leaving the board or losing tracking pauses play and restores a clear view of the room. Resume from a valid tile with a fresh countdown and a reachable move.
- [ ] **056 — [HEADSET]** Step across a seam, stand near a corner, lean, hop in place, cross a gap, and leave the board. Compare what the marker showed with what the game decided.

## F. Implement the shrinking board and prove it is fair

- [ ] **057** Give every tile a stable board coordinate. Track whether it is intact, scheduled to break, already a hole, or occupied by a moving surface.
- [ ] **058** Build the first wave with a fixed pattern and a **5-second** trial countdown. This provides a repeatable baseline for judging timing and readability.
- [ ] **059** Capture the player’s starting tile at the beginning of each wave and always include it among the tiles that will break. Every wave must require movement.
- [ ] **060** Reserve a stationary safe tile one ordinary step away before randomizing anything else. Verify the route uses connected glass and does not force the player across a hole.
- [ ] **061** On larger boards, show several safe choices in early waves when space permits. Randomize the survivor count while removing at least one tile each wave.
- [ ] **062** Keep the surviving stationary glass connected. Evaluate whether the next wave can still offer a legal move from any tile on which the player could survive.
- [ ] **063** If a random pattern fails those checks, discard it. Use a known-valid smaller survivor set when retries cannot produce a fair pattern.
- [ ] **064** Choose and store the complete wave pattern before revealing warnings. Stepping onto safety early must leave the chosen pattern and countdown intact.
- [ ] **065** Play an audible countdown cue alongside the visible timer. Let a player looking at their next step hear when the deadline is approaching.
- [ ] **066** At zero, capture the player’s supporting tile, resolve survival exactly once, and trigger all scheduled breaks together. Visual animation delays must never change the outcome.
- [ ] **067** Keep shattered positions open for the rest of the level. Build the next wave only from surviving glass and eligible moving surfaces.
- [ ] **068** Award one survived wave per successful resolution. Display the level and wave count without covering the board. Restart clears the current run’s score.
- [ ] **069 — [EDITOR]** Run repeatable seeded patterns on every supported board size from corner, edge, and interior starts. Verify forced movement, legal reach, connected survivors, and eventual level completion.
- [ ] **070 — [HEADSET]** Demonstrate two outcomes by **December 2**: step early and survive when the timer reaches zero, then deliberately remain on the starting tile and lose at zero.

## G. Add Classic, Memory, Last Tile, fog, and sliding glass

- [ ] **071** Make the first playable wave Classic. Keep the unsafe edge pulse and cracks visible until the drop so a new player learns the warning language.
- [ ] **072** Introduce Memory with a brief cue. Reveal which panes are unsafe, then hide both the flashing edges and identifying cracks while the countdown continues.
- [ ] **073** During Memory recall, preserve the occupancy marker, timer, board outline, and existing holes. The challenge is remembering safe glass while moving.
- [ ] **074** Mix Classic and Memory across the shrinking board. Use shuffled ordering after their introductions so repeated levels vary and one type cannot dominate by chance.
- [ ] **075** When the board is ready to leave one survivor, announce **Last Tile**. Select a stationary final tile reachable from the player’s current position.
- [ ] **076** Resolve Last Tile at zero. On survival, give a distinct completion sound and brief visual celebration, rebuild the original confirmed footprint, and wait for readiness.
- [ ] **077** Shorten countdowns as levels advance. Establish the minimum through physical playtests with an ordinary step and record the final timing values in the design document.
- [ ] **078** Introduce fog on a later level after the player has learned the base game. Move a thin bank across part of the board while keeping the physical perimeter and time display legible.
- [ ] **079** Introduce one sliding pane on a later level. Show its direction before it starts moving and constrain its path to the confirmed board above an existing opening.
- [ ] **080** Lock the pane in place when the player reaches its live surface. A near miss exposes the hole and starts the ordinary gap-crossing grace period.
- [ ] **081** Keep a stationary safe option available during every sliding wave. Validate that the locked pane offers an onward step before scheduling it into a wave.
- [ ] **082 — [HEADSET]** Test fog alone, sliding alone, Memory alone, and then combinations. Catch the slider from each side, miss it, recover once, and deliberately fall once.

## H. Make glass shatter and holes look deep

- [ ] **083** Prototype one broken pane beside one intact pane before dressing the entire board. The first visual goal is seeing depth while standing safely next door.
- [ ] **084** Adapt the Floor Zone stencil technique so shaft walls and below-floor scenery are visible only through the intended opening.
- [ ] **085** Give the opening a readable rim and inward-facing shaft walls. Use darker values and repeated wall details to communicate distance below the floor.
- [ ] **086 — [HEADSET]** Look into the opening with both eyes from directly above, from a neighboring tile, and at a shallow angle. Check for floating rims, missing walls, or real-floor occlusion covering the hole.
- [ ] **087** Create one pre-fractured tile variant whose pieces fit the intact pane’s footprint. Match its crack lines to the visible warning pattern so the eventual break feels continuous.
- [ ] **088** At zero, replace the intact pane with fitted shards. Give the pieces slightly different release times, rotations, and downward speeds while keeping the actual loss decision simultaneous.
- [ ] **089** Keep the shards visible long enough for a safe player to follow them into the shaft. Return them to a pool once they are out of sight.
- [ ] **090** Add a sharp local impact at floor level, smaller glass ticks as fragments separate, and a delayed, quieter echo from below. Give simultaneous breaks a sensible combined volume.
- [ ] **091** Check adjoining holes. Shared edges and internal walls should read as a coherent opening, and neighboring intact panes should retain visible support and boundaries.
- [ ] **092** On player loss, take the scene from a tile-sized opening into an enclosing shaft. Keep head tracking active while wall geometry and texture detail rise around the player.
- [ ] **093** Provide a reduced-motion fall that fades into the hazard reveal. Allow the player to skip a loss sequence and reach the same results screen.
- [ ] **094 — [HEADSET]** Watch another tile break while standing safely, then fall through the player’s tile. Verify that these two viewpoints are visually distinct, convincing, and comfortable.

## I. Build the six surprise endings and replay flow

- [ ] **095** Create one ending controller that receives a confirmed loss, stops gameplay input, plays the selected reveal, fades to black, and hands control to the results panel.
- [ ] **096** Shuffle enabled endings independently of Classic, Memory, or Last Tile. Show each enabled ending once per shuffle and prevent a repeat at the boundary of two shuffles.
- [ ] **097** Add a development-only chooser so every ending can be tested and recorded on demand. Keep the normal player experience surprising.
- [ ] **098 — Water arrival.** Reveal a dark water surface at the base of the shaft. Bring it toward the player, then synchronize impact, splash particles, and a change in sound.
- [ ] **099 — Water submersion.** Raise the waterline slowly past eye level. Introduce bubbles, underwater tint, and muffled audio before fading completely to black.
- [ ] **100 — Lava arrival.** Cast orange light onto the lower shaft walls before the glowing lava surface appears. Use the rising glow to foreshadow the ending.
- [ ] **101 — Lava engulfment.** Raise flames around the edges of the player’s view, add drifting embers and heat sound, then fade fully to black.
- [ ] **102 — Crystal cavern.** End the shaft in a ring of tall crystal shapes. Light them in sequence around the player, play a final resonant chime, and extinguish the lights.
- [ ] **103 — Space.** Let the shaft walls fall away into a deep starfield. Drop the falling sound into silence, hold the reveal briefly, then fade the stars.
- [ ] **104 — Sand.** Reveal a buried chamber, send sand down its walls, raise the accumulated sand, and let the opening overhead become the last visible light.
- [ ] **105 — Snow.** Reveal cold blue light, sweep snow across the space, build the wind, briefly white out the scene, then fade to black.
- [ ] **106** After every ending, show level reached, waves survived, Restart, and Quit. Keep the panel readable regardless of which direction the player turned during the fall.
- [ ] **107** Restart clears hazard visuals, audio, shards, holes, slider state, timers, and score. Restore passthrough before asking the player to move back onto the rebuilt board.
- [ ] **108 — [HEADSET]** Play water and lava end to end first. For each additional ending that ships, test full motion, reduced motion, skip, restart, and cleanup. Test Quit from the standalone APK.

## J. Keep Unity responsibilities clear and recoverable

- [ ] **109** Give room loading and board fitting one owner, provisionally `RoomBoardPlacement`. Record its actual script path, scene object, and required MRUK references.
- [ ] **110** Give board state and pattern generation a headset-independent implementation, provisionally `BoardState` and `WavePlanner`, so a recorded seed can reproduce a bad layout.
- [ ] **111** Give occupancy, border handling, sliding support, gap grace, and board exit one owner, provisionally `PlayerTileTracker`. Document which position it samples and when.
- [ ] **112** Give the countdown, wave resolution, scoring, level transitions, pause, loss, and restart one authority, provisionally `GameFlow`. Prevent multiple components from resolving the same timer.
- [ ] **113** Keep tile visuals and ending presentation separate from the survival decision. Record the actual tile prefab, shatter pool, hazard prefabs, and scene references once they exist.
- [ ] **114** Keep the wave seed, survivor list, and deadline available together. This provides reproducible debugging now and preserves a route to shared rounds if multiplayer is explored later.
- [ ] **115** Document how to reproduce one Classic wave, one Memory wave, a Last Tile clear, a slider miss, and a specific ending without relying on random luck.
- [ ] **116** Record the exact scene, active build profile, package identifier, and working build procedure after the first successful Quest build. Update these if they change.

## K. Verify comfort, performance, and outside-player feedback

- [ ] **117 — [HEADSET]** Check that the board is readable on both light and dark real floors. Inspect safe glass, warning glass, the current-tile marker, and an open hole under each condition.
- [ ] **118 — [HEADSET]** Verify recognition with audio muted, then with warning intensity reduced. The player should still distinguish damage, occupancy, and the moment of resolution.
- [ ] **119 — [HEADSET]** Test pausing, removing and replacing the headset, and regaining tracking mid-wave. Confirm that an interruption does not produce an unexplained loss.
- [ ] **120 — [HEADSET]** Check menu reach from seated and standing positions. Check gameplay reach separately by walking the guaranteed route without jumping or taking an unusually long stride.
- [ ] **121 — [HEADSET]** Profile the largest supported board with OVR Metrics Tool. Record FPS, frame time, draw calls, triangle count, build identifier, and the exact scene being measured.
- [ ] **122 — [HEADSET]** Measure the largest simultaneous shatter, fog crossing the board, sliding glass, and the busiest ending. Target **at least 72 FPS** and **under 13.9 ms per frame**.
- [ ] **123** If performance drops, change one expense at a time: overlapping transparent layers, particle counts, active shards, or material complexity. Repeat the same stressful scene after each change.
- [ ] **124** Recruit at least two testers outside the project. Ask each to confirm the board, learn the stone interaction, survive a wave, try an advanced wave, experience a loss, and restart.
- [ ] **125** Observe each tester’s first attempt before explaining the game. Record where they look, which tile they think they occupy, how they interpret cracks, and whether they understand why they lost.
- [ ] **126** Ask which part felt unfair or uncomfortable and which moment they would want to show someone else. Record their actual answers, including negative feedback.
- [ ] **127** Implement the most consequential change that fits the remaining time. Repeat the same scenario and record whether the change improved the observed problem.
- [ ] **128 — [DOC]** Put real tester findings in Section 10 of the course document and real Quest measurements in the optimization section. Identify the build used for each result.

## L. Finish the class deliverables and maintain the handoff

- [ ] **129** Protect the December 2 interaction milestone before expanding the effects. A polished ending cannot substitute for demonstrating a working wave in the real room.
- [ ] **130** If time tightens after water and lava work, protect Memory, Last Tile, fog, and sliding tiles before polishing crystal, space, sand, and snow. Record the status of every ending individually.
- [ ] **131** Complete the README with gameplay description, screenshots, hardware and software requirements, setup, build and installation instructions, known issues, roadmap, credits, and AI usage.
- [ ] **132** Verify the repository from a clean checkout, including LFS assets, package resolution, scene references, and the documented build procedure. Fix missing files or instructions.
- [ ] **133** Update the final design document to reflect the game that actually shipped. Record changed or dropped features, headset measurements, tester feedback, and the repository URL.
- [ ] **134 — [DOC]** Export and visually inspect the final PDF. Verify the repository and final document are ready for the **December 9** submission.
- [ ] **135** Record the **3–5 minute Quest 3 video**. Open with a shatter beside a safe player, then show placement, practice stone, a shrinking board, an advanced wave, a loss, and a comfort setting.
- [ ] **136 — [DOC]** Check video audio, narration, visibility of passthrough and glass, and link access. Put the URL in the README and design document for **December 11**.
- [ ] **137 — Every work session.** Update **Resume here** with the active item number, last verified result, actual Unity scene or file, relevant commit or APK, and next concrete action.
- [ ] **138 — Every incomplete item.** Record the exact stopping point. For example, *“Board preview aligns in the Editor. APK installs. Next: wear Quest in the demo room and check the far-right corner against the couch.”*
- [ ] **139 — Every changed decision.** Record the old behavior, new behavior, reason, and affected item numbers. Mark superseded choices explicitly, especially timing, board persistence, reachability, and sliding-tile behavior.
- [ ] **140 — Every handoff.** Read the resume table and first unfinished item together. Confirm that another person could identify **what to open, what to do, what they should see, and what evidence would let them check it off**.
