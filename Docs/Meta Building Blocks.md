# Meta Building Blocks for Floor Fracture

![Building Blocks entry in the Unity Meta tools, supplied by Wais](./Media/Meta-Building-Blocks.png)

The November 19, 2026 review covered all 68 serialized block definitions in the project's installed SDK 209 packages. This count includes hidden dependencies, deprecated definitions and internal prototypes. The public catalog lists the blocks that players and developers commonly encounter. A Unity Interaction SDK Quick Action or an official sample can also supply a complete implementation. [Meta Building Blocks overview](https://developers.meta.com/vr/documentation/unity/unity-building-blocks-overview/)

## What we can use

| Player experience | Meta feature | Project decision and verification |
|---|---|---|
| Enter the real room | Camera Rig, Passthrough, MR Utility Kit | Keep one tracked rig and one passthrough underlay. MRUK loads the saved floor, walls and furniture and keeps the board anchored. Wais saw the grid align on Quest 3. The system's Space Setup supplies the room scan. |
| Find an open play area | Find Spawn Positions, official Floor Zone sample | Use the installed block with its floor filter to propose nearby ground positions. Its stock marker objects are cleared before they render. The sample fits a fishing pond to bounds around clusters of samples. Those bounds can include an unscanned gap between points or furniture in a concave corner. The game checks the whole rotated rectangle, including its interior and 40 cm margin, and chooses pane size and count from what fits. A search over the scanned geometry also works when none of the sampled positions fit. [Floor Zone](https://developers.meta.com/vr/documentation/unity/unity-sample-mruk-floor-zone/) · [MRUK room queries](https://developers.meta.com/vr/documentation/unity/unity-mr-utility-kit-manage-scene-data/) |
| Understand the fit | Built-in room and anchor events, Scene Debugger | Put the actual dimensions in Welcome. Leave another spot and a new Space Setup scan available under Play area. In development, Scene Debugger can explain a mismatch between the saved anchors and the room. A moved chair requires current room data. |
| Use hands or controllers | Interactions Rig, Controller Tracking, Grab Interaction, Ray Canvas and Poke Canvas Quick Actions | The two SDK surfaces feed the same menu buttons. The panel sits 56 cm ahead and spans 39 cm. An index fingertip can press directly, or a hand or controller can point at the same choice. The stone uses the SDK grab prefab. Wais used hand input successfully in the first build. Verify each physical controller and prioritize a hand-accessible interaction wherever the game asks an unarmed player to act. |
| Feel nearby glass break | Spatial Audio, Haptics | Meta’s Spatial Audio installer creates the prefab for ten positional sources. Groups of three play the impact at the broken glass, fragments just below it and an echo deeper in the shaft. The tenth places a landing cue at the point of impact. Menu sounds and countdown ticks are centered. Check localization while standing on a safe neighbor. [Meta spatial audio](https://developers.meta.com/vr/documentation/unity/meta-xr-audio-sdk-unity-spatialize/) |
| Let real objects hide virtual glass | Effect Mesh, Occlusion, Room Guardian | The stock Room Guardian and its Effect Mesh mark proximity to scanned geometry at 1 m. The practice stone uses Meta's environment-depth material. The glass openings use the floor stencil so the shaft stays visible. The physical trial checks the outline, whether a foreground object hides the stone and the cost during play. |
| Fall below the floor | Floor Zone water, splash and floor-mask example | Reuse the approved water material, splash assets and impact sound. The sample animates the size and position of a fishing pond. The installed catalog has no continuous first-person shaft descent or timed hazard reveal. The game supplies the moving shaft and arrival. Judge their timing in the headset. |
| Give the game a natural voice | Text To Speech | Wais chose to synthesize a few fixed tutorial and results lines once, then include the approved clips for offline Quest playback. SDK 209 has OpenAI and ElevenLabs speech providers. Wais will choose the provider and audition the voice after the revised gameplay test. A later optional results coach can use the recorded wave outcome. [Meta AI Building Blocks](https://developers.meta.com/vr/documentation/unity/unity-ai-building-blocks-overview/) |

## Blocks Wais added in Unity

The saved `Assets/Scenes/SampleScene.unity` contains Wais’s experiments. The standalone build opens `Assets/FloorFracture/Scenes/FloorFracture.unity`. Open the first scene to inspect the sample objects or the second to test gameplay.

![Find Spawn Positions in the installed Meta catalog, supplied by Wais](./Media/Meta-Find-Spawn-Positions.png)

The gameplay scene has one **Find Spawn Positions** block. On room load it requests eight candidate positions on surfaces labeled **Floor**. In the Editor's Rec Room, those candidates and the footprint checks produced six columns by three rows at 65.24 cm per pane. The Welcome screen shows the fit calculated for the player's own room. [Editor playthrough](<./Verification/Adaptive Editor playthrough.json>) · [Meta Floor Zone example](https://developers.meta.com/vr/documentation/unity/unity-sample-mruk-floor-zone/)

| Wais’s block | How it fits the game |
|---|---|
| Instant Content Placement | Its Point And Locate and Grab And Locate samples use depth to position a known-size object. Automatic board placement uses the room scan and Find Spawn Positions. A future optional “point here” control could feed a chosen floor location into the same full-grid validation. |
| Occlusion | Wais selected it for the practice stone. A real foreground object should hide part of the stone. The floor stencil controls holes, so the shaft stays visible through broken glass. The Android OpenXR loader uses Unity OpenXR: Meta for the environment depth subsystem. [Meta’s occlusion setup](https://developers.meta.com/vr/documentation/unity/unity-depthapi-occlusions-get-started/) |
| Room Guardian | Wais selected the stock proximity mesh for the headset trial. Near a scanned wall or piece of furniture, its outline should appear and then fade as the player moves away. The Meta system boundary remains independently available. |
| Shared Spatial Anchor Core | Wais’s original block is saved as a prefab for a future same-room multiplayer board. The solo game uses MRUK world locking. Keeping the example outside both running scenes lets the solo APK omit shared-map transfer permission. [Preserved example](<../Assets/Scenes/Shared Spatial Anchor Example.prefab>) · [Meta shared anchors](https://developers.meta.com/vr/documentation/unity/unity-shared-spatial-anchors/) |
| Poke Interaction | Direct button presses complement the SDK ray. The Poke Canvas Quick Action adds a second input surface to the same menu. The existing comprehensive rig supplies left and right hand and controller poke interactors. Test one physical touch per choice on Quest. [Meta Poke Canvas](https://developers.meta.com/vr/documentation/unity/unity-isdk-poke-quick-action/) |

### What remains after the scene cleanup

| Part | Sample scene | Game loaded on Quest |
|---|---|---|
| Floor placement | One floor-filtered Find Spawn Positions example, set to show three sample markers. | One floor helper supplies up to eight candidates. The game displays the fitted glass. |
| Environment depth | One provider under Occlusion Dependencies. The separate Environment Raycast Manager can use it for instant placement. | One provider for the practice stone. |
| Placement cubes | One Point And Locate cube with its controller callback and ray visualizer. One Grab And Locate cube with its hand and controller grab interactables. | Zero placement-demo cubes. The tutorial uses the practice stone. |
| Spatial Audio | One Meta source with its 48 kHz mono `Interaction_Object_Impact_01` clip. Preview it in Unity. Play On Awake is off. | Ten pooled Meta sources place shattering glass, echoes and impacts at their world positions. |
| Tracked room | One each of Camera Rig, Passthrough, MRUK and Room Guardian. | One each of the same four blocks. |

The saved scenes were reopened for inspection. Meta's Android checks passed for clip assignment and mono audio. Its manual App ID and Data Use Checkup recommendations remain open. The local Android package is `com.wa003.floorfracture`. The Unity project contains no configured Platform Settings asset for a numeric Quest App ID. Dashboard status requires the app owner's account. [Scene and package audit](<./Verification/Meta scene cleanup.json>) · [Meta's Data Use Checkup](https://developers.meta.com/vr/resources/publish-data-use/)

### Inference Engine for the LLM block

Meta SDK 209 checks for the exact package `com.unity.ai.inference@2.3.0`. The stock release uses two APIs that fail to compile on Unity 6000.6.9f1. This project embeds 2.3.0 under `Packages/com.unity.ai.inference/`, with Unity's corresponding 2.6.1 updates to `Runtime/Core/PluginInterfaces.cs` and `Editor/Visualizer/Editor/ModelAssetOpenHandler.cs`. Package Manager reported the embedded version, Meta's own dependency check returned true, and script compilation completed with zero errors. This installs the engine. An on-device language model would require a separate model and tokenizer decision. The planned fixed narration uses offline audio clips. [Unity embedded package workflow](https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/dependencies-lp/upm-embed) · [Unity 6.6 object ID migration](https://docs.unity.com/en-us/engine/6000.6/manual/scripting/get-started/instanceid-to-entityid-migration)

Meta’s MCP server supplies the official documentation and current device information. Its Quest tools can also read app logs, capture a headset view and profile a device. Unity’s installed SDK supplies the actual block definitions, assets and scene installer. On this Mac the standalone Meta XR Simulator is installed. The former `com.meta.xr.simulator` 81.0.0 package produced four Editor compile errors with Core 209. Meta moved the Unity controls into Core and directs developers to remove that package. [Meta’s current simulator setup](https://developers.meta.com/vr/documentation/unity/unity-simulate-xrsim/)

## Installed catalog

Each name below corresponds to an SDK block definition. In the installed Core package, **Room Model** and **Scene Mesh** carry a deprecated tag. The current MRUK package supplies the room model and Effect Mesh tools. **Instant Content Placement** fits a known-size object at a pointed or grabbed location using depth. **Anchor Prefab Spawner** attaches decorations to labeled surfaces. These are available for features that need them. [MRUK setup and blocks](https://developers.meta.com/vr/documentation/unity/unity-mr-utility-kit-gs/)

| Installed package and family | Block names |
|---|---|
| MRUK · 8 | MR Utility Kit, Find Spawn Positions, Effect Mesh, Scene Debugger, Room Guardian, Anchor Prefab Spawner, Instant Content Placement, Environment Raycast Manager |
| Core · tracking, rendering and feedback | Camera Rig, Controller Tracking, Hand Tracking, Eye Gaze, Passthrough, Passthrough Camera Access, Passthrough Camera Visualizer, Passthrough Window, Occlusion, Occlusion Dependencies, Room Model, Scene Mesh, Spatial Audio, Haptics, Alert View HUD, Controller Buttons Mapper, Cube |
| Core · AI and voice | Large Language Models, Speech To Text, Text To Speech, Object Detection, Image Segmentation, Dictation, Speak Text |
| Core · anchors, avatars and networking | Spatial Anchor Core, Sample Spatial Anchor Controller, Shared Spatial Anchor Core, Colocation, Character Retargeter, Networked Character Retargeter, Networked Avatar, Networked Grabbable Object, Player Name Tag, Player Voice Chat, Local Matchmaking, Friends Matchmaking, Auto Matchmaking, Custom Matchmaking, Network Manager, Platform Init |
| Interaction OVR · 19 | Interactions Rig, Grab Interaction, Ray Interaction, Poke Interaction, Distance Grab, Touch Hand Grab, Gaze Interaction, Real Hands, Controller Interactions, Hand Interactions, Grab Interactor, Grabbable Item, Hand Poke Interactor, Hand Poke Limiter, Hand Ray Interactor, Synthetic Hands Colliders, Teleport, Throwable Item, Virtual Hands |
| Platform · 1 | EntitlementCheck |

Physical stepping rules out teleport during a round. Multiplayer, avatar retargeting and shared anchors belong to a future shared version. Raw camera access, object recognition, image segmentation and speech input would introduce data pathways outside the current mechanics. The present placement uses the saved scene and tracked headset position.

For every new feature, check the installed definition, prefab, relevant Quick Action and official sample first. Record what it delivers, what Floor Fracture still needs, and how the integration was verified. Ask Wais before choosing a new asset or AI provider.
