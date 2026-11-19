# Floor Fracture

Wais Almakaleh · CSCI 379, Design and Development for XR · Meta Quest 3

[Master project checklist](<./Floor Fracture — Master Project Checklist.md>) · [Course design PDF](<./Docs/Floor Fracture Design Document.pdf>)

## What the player sees

The real room stays visible. On a clear patch of floor, a rectangle of smoky glass panes sits flush with the ground. A small ring marks the pane under the player's head. A number floats near the far edge. Cracked edges pulse while the timer counts down. At zero every marked pane breaks together. From a neighboring safe square, the player sees fitted shards fall into the opening. If their own support gives way, the shaft grows around them, a surprise ending plays, the view fades to black, and a results panel offers Restart and Quit.

## Why this Unity stack

| Approach | What it provides | Project trade-off |
|---|---|---|
| Unity OpenXR, Unity OpenXR: Meta and AR Foundation | OpenXR input and Unity's AR abstractions for passthrough and captured room geometry. Portable gameplay code can run on another supported device with a room provider. | The room setup, floor-fit behavior, interaction assets and sample visuals would need separate integration. Device-specific capabilities still need device-specific verification. |
| Meta XR Core, Interaction and MRUK with Unity's Android OpenXR loader | The previous course exercises use these packages. MRUK supplies room anchors and floor polygons. The Floor Zone sample demonstrates a portal in a real floor. | Scene setup, permission flow and sample assets are Quest-specific. Board rules use independent coordinates and seeds so another room provider could reuse them. |

Wais approved Unity `6000.6.9f1`. The project uses URP `17.6.0`, Meta XR `209.0.0`, Unity OpenXR `1.19.0`, ARM64 and Android package `com.wa003.floorfracture`. [Unity OpenXR: Meta](https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.7/manual/index.html) · [Meta Floor Zone](https://developers.meta.com/vr/documentation/unity/unity-sample-mruk-floor-zone/)

## First minute

1. Load the captured room and ask for spatial data when needed. The player can start Space Setup if the headset has no usable room.
2. Fit the largest clear rectangle from 2×2 through 4×4. Trial dimensions are 0.5 m per pane and 0.4 m around the board. Preview the entire footprint. The player confirms that the real floor is clear or asks for another position.
3. Either controller ray selects menu controls. The optional tutorial presents a grabbable stone and a marked pane. Letting go over the pane demonstrates the shatter. A miss returns the stone. The player then steps between two practice positions to learn the ring.
4. Stand on an intact pane and start the first Classic wave. Its five-second countdown gives the player time to find a stationary one-step route. Moving early keeps the original timer running.

## Wave rules

- Every wave schedules the starting square to break. At least one orthogonally adjacent stationary square survives. Surviving stationary panes stay connected, allowing the next wave to start from any survivor.
- Possible outcomes at zero are supported by surviving glass, supported by breaking glass, and in a gap. Only the first survives. During the countdown a 0.3-second trial grace allows crossing a pre-existing gap. Leaving the board or losing tracking pauses the wave and restores a view of the room.
- Classic shows the warnings until zero. Memory briefly reveals the same cracks and edge pulses, then hides them while the timer, ring and holes stay visible. Last Tile leaves a single reachable stationary square and awards the level. The board rebuilds and waits for readiness.
- Later levels shorten the timer, add a low fog bank, then a direction-marked sliding pane over a hole. The moving pane locks on contact. A stationary escape route remains available. The timing floor and practical reach need physical playtests.

## Loss and replay

The player keeps head tracking throughout. Wall details rise around them to suggest a fall. Reduced motion uses a fade into the reveal, and Skip goes to the same results. A separate shuffle selects water, lava, crystal, space, sand or snow, with no immediate repeat between shuffles. Water passes eye height with bubbles and muffled sound. Lava lights the lower walls before flames and embers appear. Crystal lights travel around the shaft, space opens into stars, sand fills a buried chamber, and snow builds to a whiteout. Each ends in black before results. Restart clears the run and restores passthrough.

## Evidence and delivery

| Milestone | Evidence |
|---|---|
| November 20, initial design | Editable design, eight-page PDF and course template in `Docs/`. Submission is recorded only when confirmed. |
| December 2, interaction | On Quest 3, demonstrate a deliberate early step and a deliberate failure at the deadline. |
| December 9, final design and repository | Verify a clean checkout, record the shipped features, headset measurements, and two outside players' actual feedback. |
| December 11, video | Capture and review 3–5 minutes of real headset footage, with an adjacent shatter, placement, stone, advanced wave, loss and comfort control. |

The approved packaging baseline is Git `3265e4b`. See the checklist for per-feature evidence, the current scene and outstanding physical tests. Decisions about third-party assets and their source redistribution belong in the asset credits before import.
