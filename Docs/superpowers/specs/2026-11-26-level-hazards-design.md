# Level hazards: harder levels without shorter timers

## Problem
Levels feel the same. Difficulty today mostly comes from shorter switch times, and the
break timing is right as it is. Sliders appear from level 3 but are rare later, because a used
slider stays on the board one more wave and blocks new sliders, jumps and rows. Fog exists
(`WavePlan.Fog`) but is a 2 mm decal at about 10% alpha, shown only during the 1–2 s wave,
so nobody sees it.

## Rule: break timing does not change
`WavePacing`, `SwitchSeconds`, reveal windows, grace values and every `BreaksAt` stay
exactly as they are. Hazards only add movement and things that make cracks harder to see.

## Hazards
1. **More sliders.** A wave can offer new sliders while last wave's slider is still on the
   board. The cap on sliders per wave rises with level: 1 at levels 3–5, 2 at 6–8, 3 from 9.
   A slider still locks (stops) when the player lands on it.
2. **Patrol panes** (from level 5). A slider whose lane turns corners: it ping-pongs along a
   path of 3–5 hole cells. It locks where the player lands, like a slider.
3. **Ride finale** (from level 6). Sometimes a level ends with a ride finale instead of the
   Last Tile finale: half the time, when a suitable lane exists. Every stationary pane breaks
   at the deadline. The only way to survive is to step across an opening onto a moving pane
   as it passes, and landing on it clears the level. A locked pane needs no exit, because
   the level ends there. The pane starts away from the player and passes a cell next to the
   player's starting pane. It stays over that cell for at least 0.35 s, inside the span from
   0.4 s after the wave starts to 0.3 s before the deadline.
4. **Fog, rewritten.** (a) A thick drifting floor fog that stays below 35 cm (knee height),
   so real furniture stays visible. Density rises with level and partly hides the amber crack
   warnings. (b) 1–3 drifting fog patches that cover a pane almost completely. Fog never
   covers a pane fully: combined opacity is capped at 0.70.
5. **Sweeping spotlights** (from level 4). 1–3 beams from lamps above the board, below the
   real ceiling. Each beam sweeps across the glass, and its bright pool washes out the
   cracks under it. Each lamp has a quiet spatial hum loop.
6. **Lightning** (from level 5). 1–2 strikes per wave. A strike is a double flash overhead,
   followed by spatial thunder from the strike's direction after a 0.3–1.2 s delay. At most
   3 flashes per second (photosensitive safety). With Reduced motion on, a strike is one
   soft glow with no flicker.

## Mix
Hazards 4–6 are rolled per wave from their own random stream, so the existing wave
patterns stay as they are. Level 1 has none. Levels 2–3: 0 or 1 hazard. Levels 4–6: 1, or
sometimes 2. Levels 7–9: 1 or 2. From level 10: 2 or 3. Hazards 1–3 are board mechanics
chosen by the wave planner, and they combine freely with 4–6.

## Visual lifetime
A wave's hazard visuals fade in over 0.35 s when the wave starts. They stay up through
BetweenWaves until the next wave replaces them, and fade out over 0.6 s when the run leaves
play (Ready, Paused, Fall, Results, menus). They run on the pause-aware gameplay clock.

## Audio
- Thunder: Freesound 534023, "Thunderclap" by Fission9, CC0. Trimmed to about 7 s, mono.
- Lamp hum: Freesound 553075, "Foley_Mechanism_Light_Buzz_Short_Loop_Mono_DR05" by
  Nox_Sound, CC0. A mono loop.

Both clips are spatial through the existing Meta XR Audio voice prefab, and both follow the
game volume setting.

## Rulings during execution
- Ride finale: under the frozen 1.00 s finale timing, a pane that starts away from the player can never pass in time. The ride pane may now start next to the player's starting pane and slide away along its lane. The catch window counts time the pane is over any neighbour of the starting pane: at least 0.35 s, ending at least 0.3 s before the deadline.
- Patrol panes: corners need standing glass on both outer sides. A patrol wave may keep up to 2 extra panes standing to give its corners exits (~11% of moving panes turn from level 7).
