# Floor Fracture · Quest 3 playtest

Use one copy per APK and test room. Record observations from the headset and leave unrun checks blank.

| Session | Record |
| --- | --- |
| Date, operator, room | |
| APK filename, version, commit or SHA-256 | |
| Headset OS, refresh rate, controller model | |
| Main scene, build type, OVR Metrics capture filename | |
| Board dimensions, tile pitch, glass footprint, countdown, gap grace | |

### Room and controls

| Check | What to try | Observed result, dimensions, evidence |
| --- | --- | --- |
| 020, 026 | Launch from the Quest library. Move and turn your head, move each controller separately, and compare the floor and scanned furniture against the real room. | |
| 023–024, 032–034 | Deny spatial permission, grant it, and try a room with no saved scan. On a valid scan, check automatic placement and the dimensions on Welcome. Choose Play area → Try another spot and inspect the replacement or recovery message. | |
| 028–034 | Spacious floor. Record rows, columns and centimetres per pane. Walk an ordinary one-step route. Record the extent of clear floor beyond the board and any object or scan edge there. Keep the same saved scan when comparing builds. | |
| 028–034 | Narrow floor. Record the rectangular board dimensions, centimetres per pane and whether each tile is reachable. | |
| 028–034 | Compare the gold edge of the playable glass with furniture beside a corner or crossing the center. Look at a chair and wall during gameplay and record whether blue scan lines appear. | |
| 033–034 | Insufficient room. Record the message, passthrough visibility, and result of rescanning. | |
| 037–044, 120 | Use each ray from seated and standing menu positions. Finish the untimed stepping tutorial. Enter optional stone practice, try both grips, miss, hit any pane, and leave for the game. Describe any coaching. | |

Compare **Up to 16 tiles** and **Entire connected floor** on the same saved scan. Record playable tile count, pitch, and FPS during the countdown and first shatter for each mode. Check that each manual start shows 3, 2, 1 and GO! in both eyes and clears the display during play. Check the room for any leftover clock or stand.

### Movement, visibility, and endings

| Check | What to try | Observed result and clip timestamp |
| --- | --- | --- |
| 049–056 | Step across a seam and near a corner. Lean and optionally hop in place. Compare the ring and the resolved tile. Cross a gap quickly and slowly, then step off the board. | |
| 058–070 | Play the first level without coaching. Watch the single amber pulse, wait for the cracks to clear, then move onto a pane that stayed clear. Pause once during reveal and once during recall. Measure elapsed time from GO to the first shatter. Start another run and stay on the starting tile until zero. | |
| 071–082 | Play levels 6 and 9. Compare simultaneous breaks with two- and three-step rows. Try alternate one-gap landings and consecutive jumps. Catch moving glass from both sides, step off, miss, recover, and retry. Record how the one-second minimum feels. | |
| 083–094, 117–118 | From intact glass, examine a neighboring opening from above and at a shallow angle. Identify its destination and compare it with a loss in the same run. Try light and dark real floors, muted audio and reduced warning intensity. | |
| 055, 107, 119–120 | Leave the board, remove and replace the headset, regain tracking, then resume from a valid tile. Compare head movement, passthrough, countdown, and walking distance. | |

Start in Full motion with **Sand** and look straight up through eye crossing. Next look straight down underwater, then turn toward the overhead light and bubbles. Try **Lava** for the red cover and **Tar Pit** for complete darkness at eye level. Look through a neighboring hole before each fall. Listen for the tar impact, two pulls, and muffled bubbles. Check for continuous movement through the splash and shaft exit. In Development → Later levels, try 5, 7 and 12 after playing level 1. Listen for the three start knocks and the soft marimba note on GO. Turn your head while several panes glow and check that each stress crackle stays with its pane, fades with the fissures, and sounds distinct from the later shatter. Check another automatic wave, a marked jump, and master mute. Listen for the wind building as you drop. Compare the muffled snow impact, lava hiss and tar glop with the abrupt silence at the Space exit. Repeat in Reduced motion and with master mute. Check that every hole and wave in a level shows the same trap, including after a pause. Across six levels, record the six different traps.

For each ending, note the motion comfort, sound, skip response, restored room view, score reset, and any visuals left behind. Press the left controller Menu button during a countdown and resume with the hand menu gesture. Pause a fall, use Resume, and select Main menu from Results. Reach Level 2, quit on its Ready screen and relaunch. Open Choose level, verify Level 2 is available and Level 3 is locked, then lose on Level 2. Retry Level 2 should restore wave 1 and a fresh board on the same floor.

| Ending | Full fall | Reduced motion | Skip, results, restart | Evidence |
| --- | --- | --- | --- | --- |
| Water | | | | |
| Lava | | | | |
| Tar Pit | | | | |
| Space | | | | |
| Sand | | | | |
| Snow | | | | |

### Performance

Record on-device measurements on the largest confirmed board. The trial target is at least 72 FPS and under 13.9 ms per frame. Record the capture interval and how frame-time values were summarized.

| Scenario, timestamp | FPS | CPU / GPU frame ms | Draw calls / triangles | Capture |
| --- | --- | --- | --- | --- |
| Largest simultaneous shatter | | | | |
| Fog crossing the board | | | | |
| Moving pane | | | | |
| Busiest ending | | | | |

### Outside players

Observe the first attempt before coaching. Ask what felt unfair or uncomfortable and which moment the player would show someone else. Use participant codes.

| Participant | Board fit, stepping lesson, first wave and optional stone | Advanced wave, loss, restart | Where they looked or needed help | Words of the player |
| --- | --- | --- | --- | --- |
| A | | | | |
| B | | | | |

| Follow-up | Record |
| --- | --- |
| Change selected, affected checklist item | |
| Before and after on the same scenario, build identifiers | |
| Link to measurements, clips, and approved video | |
