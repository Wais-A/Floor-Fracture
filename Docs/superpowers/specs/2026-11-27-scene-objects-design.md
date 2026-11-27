# Scene objects: everything the game shows is saved in the scene

## Why
Wais wants to click any game object in the Hierarchy, inspect it and change it himself, without
pressing Play. Today `Assets/FloorFracture/Scenes/FloorFracture.unity` holds 26 objects, almost
all Meta SDK rig pieces. The **Floor Fracture** object carries only `GameFlow`, and the menu
canvas is empty. Everything else (menu contents, glass board, fog, lamps, lightning, the fall
chamber and 21 sound sources) is created by scripts at Play, with meshes and materials made in
code and colours pushed onto renderers from constants.

## What Wais decided
- Every game object is saved in the scene and visible without Play.
- He can edit those objects himself.
- The glass board is saved as a 6 × 6 **preview board**. At Play the game re-fits that same
  object to the scanned room. He chose this over keeping the board in a prefab only.
- Effects that are off while the game is idle are **saved off**: fog slices, lamps, beams, light
  pools, the sky flash and flash veil, the standing ring and pane cracks. They can be selected
  and inspected but show nothing in the Scene view. He will switch them on by hand when he wants
  to look at one. No edit-mode preview of them is built.

## Calls made for him (he can overrule any of these)
- The saved menu shows the Welcome page, because the buttons change per page.
- Colours and menu styling move into Inspector fields. Without this, saved objects would be
  visible but almost nothing on them would be editable, because scripts overwrite the look.
- Game rules, timings, sound loudness and mesh shapes stay in code.
- No object is renamed, re-parented or grouped into folders.

## Goals
1. Opening the scene without Play shows every object the game displays or plays sound through,
   under the names and nesting it has at Play today.
2. At Play the game uses those saved objects. It creates only what the room requires beyond
   what is saved (extra panes, extra menu rows, a pane's crack sound).
3. For everything in "What becomes editable", an edit made in the Editor is what the game uses.
4. With nothing edited, the game looks, sounds and plays exactly as it does now.

## Non-goals
- Editing the colours of fog, lamp beams and lightning. Those live in shader source.
- Editing mesh shapes, per-effect loudness and pitch, or gameplay rules and timings.
- Previewing a chosen ending in edit mode. The fall chamber is saved switched off and shows
  untinted when switched on, because its colours are driven per ending at Play.
- Converting editor-only desktop helpers ("Editor left/right grip", "Editor left/right menu
  ray"), the room placement debug drawings ("Placement outlines", "Placement diagnostics") and
  the cameras the checks create.

## Anti-goals
- Two copies of anything at Play, one saved and one generated.
- An edit to something listed as editable that looks right in the scene and is silently
  overwritten at Play.
- Any change to gameplay, look or frame rate on Quest.
- Play in the Editor writing to files on disk (a material or mesh asset changed by Play).

## Constraints
- **The room decides the board.** Pane count, pane size (0.50–0.75 m, 0.40 m compact) and board
  shape are unknown until the headset scan, have no upper bound, and can change during a
  session when the room is re-confirmed. Cells the layout excludes have no pane.
- **The Play Mode gameplay checks must keep passing** (`Assets/FloorFracture/Editor/
  Verification/GameplaySmoke*.cs`, run from Tools → Floor Fracture → Verify gameplay in Play
  Mode, about 8.5 minutes). They depend on today's structure:
  - Exact names and paths, for example `Fall chamber/Hazard surface`, `Floor fog/Floor fog 0`,
    `Overhead lights/Lamp 0`, `Lightning/Sky flash`, `Glass 1, 1/Glass sides and bottom`,
    `Floor Fracture Menu/Choice 3`, `Title/Warning preview`, and `Start count/Count` under the
    head while it is shown.
  - Sound sources are direct children of the `GameFlow` object. Exactly 15 of them are spatial,
    each carrying Meta's Spatial Audio block. `GameSound`, `OfflineNarration` and `FallSequence`
    sit on the `GameFlow` object itself.
  - `GameObject.Find("Fall chamber")` must find nothing while the chamber is not in use, so the
    chamber is deactivated then, not merely hidden. Only one object may have that name.
  - One check builds its own 9 × 9 board with `new GameObject`, `AddComponent<GlassBoardView>()`
    and `Build(...)` with no sound. `Build` must still work on an empty object.
  - Some checks compare a pane part's mesh with the generated one, for example
    `sharedMesh == GlassGeometry.Rim`, and require all panes to share one mesh and material.
  - A scan that includes inactive objects requires every pane part under the board to lie at
    floor height and to have a mesh. Spare panes cannot be kept switched off under the board.
  - While no hazard shows, every renderer under `Floor fog`, `Overhead lights` and `Lightning`
    is disabled. Idle crack and lamp sounds sit on inactive objects.
  - Any error logged from Floor Fracture code fails the run.
- **Nothing is saved under Meta's camera rig**, and nothing on layer 30 (the checks render on it).
- **Tools → Floor Fracture → Prepare game scene** must keep working and must not undo this work.
  Today it resets the menu canvas to a scale twice the one used at Play.
- **A saved component must have the same saved fields in every build.** `FallSequence` has a
  saved field, `developmentEnding`, inside `#if UNITY_EDITOR || DEBUG`. Once the component is
  saved in the scene, that field has to exist in release builds as well.
- Unity 6000.6.10f1. Game scripts compile into `Assembly-CSharp`, Editor scripts into
  `Assembly-CSharp-Editor`. The Edit Mode tests (`Tests/Rules`, `Tests/Room`) do not touch any
  of these classes, apart from one test that constructs `RoomOutlineView`, which is unchanged.

## Approaches considered
1. **Save once, adopt at Play (chosen).** An Editor command runs the game's own builders in
   edit mode and saves the result. Each builder changes from "always create" to "use the saved
   object, create what is missing". Names and nesting stay identical, so the checks keep
   working, and a bare object still gets a complete board. Wais left the method to Claude.
2. **Rebuild the scene by hand and delete the builders.** The most conventional project, but
   the board, fog and lamps still have to be resized to the room, so most builder code stays,
   and the check that builds its own 9 × 9 board would have to be rewritten.
3. **Edit-mode preview only.** Least work, but every edit would be discarded at Play, which
   fails the second half of the request.

## Design

### One rule for every builder: adopt or create
A builder looks for its object by name under the same parent it uses today.
- **Found:** use it, and leave its one-time settings alone (font, text colour, sound 3D
  settings, rotation, renderer flags).
- **Not found:** create it with today's values.

Either way, the builder then applies what only the game can decide: anything fitted to the
room, anything animated, text content and which objects are showing.

Components follow the same rule. `GameFlow` uses the `GameSound`, `OfflineNarration`,
`FallSequence` and `GamePanel` already on its object and adds one only when it is missing.

### What the scene holds afterwards
```
Floor Fracture              GameFlow, GameSound, OfflineNarration, FallSequence, GamePanel
├─ Floor Fracture Menu      canvas, background image
│  ├─ Title                 (child: Warning preview, off)
│  ├─ Instructions
│  ├─ Choice list / Content
│  ├─ Choice 1 … Choice 5   Welcome page: Start, Choose level, Settings, Play area, Development
│  └─ the two Meta canvas interaction objects (already saved)
├─ Start count / Count      off; moved under the head when shown, as today. Saved beside the
│                           menu, not inside it, so it has its real size when switched on.
├─ 5 plain sound sources    Menu and countdown, Background music, Shaft air, Shaft roar,
│                           Ending ambience
├─ 15 spatial sound sources Opening draft, Ending impact, Tar bubbling, Pane impact 0–2,
│                           Glass fragments 0–2, Shaft echo 0–2, Thunder 0–2
└─ Offline narration
Confirmed glass board       GlassBoardView, ShardPool; 6 × 6 preview at the origin
├─ Glass 0, 0 … Glass 5, 5  36 instances of the pane prefab
├─ Occupied square, Opening stencil, Perimeter, Falling fragments
├─ Floor fog                Floor fog 0–3
├─ Overhead lights          Lamp 0–2 (each with Lamp hum), Spotlight beam 0–2, Spotlight pool 0–2
└─ Lightning                Sky flash, Flash veil
Fall chamber                saved switched off
└─ Shaft walls, Shaft landmarks, Left brick column, Right brick column, Chamber darkness,
   Hazard surface, Floor Zone water ripples, Lava flames, Particles, Water spray,
   Entry bubbles, Fall dust, View fade / Submerged view, Water contact splash
```
The spatial sound sources stay linked instances of `Resources/GlassSpatialVoice.prefab`. The
panes are linked instances of `Resources/GlassTile.prefab`, which gains its four parts as
children (Smoky pane, Glass sides and bottom, Rim, Fissures), so opening the prefab shows a pane.

### How the objects get into the scene
A new Editor command, **Tools → Floor Fracture → Save game objects into scene**, runs the
builders in edit mode and saves the scene. It builds the sound sources, the menu on its Welcome
page, the fall chamber, and the board for a 6 × 6 layout at 0.62 m pitch with the floor at
height 0. Because builders adopt, running it again adds only what is missing and keeps edits.

Everything is saved in the state the idle game has it. Whatever Wais switches on by hand in the
scene, the game must still switch off at startup, since it already sets these states itself.

### Meshes and materials
- **Meshes stay generated in code at Play.** The command saves a snapshot of each as a file
  under `Assets/FloorFracture/Generated/`, so saved objects are visible in edit mode. At Play
  the builders assign the generated meshes over the snapshots, as they do now. Swapping or
  editing a snapshot has no effect on the game.
- **Materials stay generated in code at Play too.** The command saves a snapshot of each beside
  the meshes and assigns it to the saved renderer, so the object shows in edit mode. Play makes
  every material from its shader as today and never reads or writes the snapshots. (Changed
  while building: the first design had Play copy the saved file. A snapshot carries values of
  the run that made it, such as the mapped floor height, and feeding those back into Play could
  change how the game renders. Editing a snapshot therefore has no effect on the game; colours
  are edited through the Inspector fields in "What becomes editable".)
- **Resting colours in edit mode.** Per-renderer colours are not stored in a scene. Scripts
  that tint renderers re-apply the resting colours in edit mode, so the Scene view shows the
  game's resting look and a changed colour field shows at once.

### Sound
- `GameSound` now wakes with the scene instead of when `GameFlow` adds it. Music must not be
  audible before the player's saved volume is applied, and no plain source may play on wake.
- An adopted source keeps its 3D settings. Code keeps driving clip, volume, pitch and position.
- The three Lamp hum sources are saved under their lamps. A pane's "Glass strain" source is
  still created the first time that pane cracks.

### Menu
- The canvas is saved at the scale used at Play (0.00058), and Prepare game scene uses the same
  value, so the scene shows the menu at its real size.
- Adopted Title, Instructions and row labels keep their font, size, style and alignment.
- Rows beyond the saved ones are copies of the saved **Choice 1**, so they inherit its styling.
- **Every menu colour has one source: six fields on `GamePanel`**, holding today's six constants
  (background, button idle, hovered and pressed, text, secondary text). The game already
  repaints the title, the instructions, each row and its label and the count from those six on
  every repaint, including the pressed and disabled cases, so a colour set on a saved text
  object would be overwritten. The panel applies the same six in edit mode, so the scene shows
  them. (Claude's call.)
- Code keeps driving the text, which rows show and the layout. The layout sizes it uses come
  from fields on `GamePanel` (see the table).

### Fall chamber
- `FallSequence` holds a saved reference to the chamber, because an inactive object cannot be
  found by name. With no reference it builds the chamber as today.
- "Floor Zone water ripples" is saved. On a device whose shader is unsupported it is switched
  off instead of not being created.
- "View fade" moves under the head during a fall and back afterwards, as today.

### Glass board
- `GameFlow` holds a saved reference to the board and hides it at startup, before the first
  frame, until the room is confirmed. With no reference it creates a board as today.
- When the room is confirmed, `GameFlow` re-fits the same object instead of destroying it and
  creating another. A re-fit:
  - reuses the panes already there, adds panes from the pane prefab, and destroys the surplus;
  - renames panes to `Glass x, y` for the new width and places and scales them;
  - rebuilds the room-sized meshes (perimeter, fog slices, light pools, lightning) and releases
    the previous ones;
  - clears everything a new board would not have: shown hazards and fog, lamp and lightning
    state, shards, the practice hole, open panes, and each pane's warning state and crack sound.
- The Settings page's "Warning preview" pane, built on an empty object with no sides, must keep
  working. It is not an instance of the pane prefab, so it takes its six colours from the
  prefab, and the sample always matches the board. (Claude's call.)
- A warned pane's face pulses between two colours that are separate from the rim and crack
  colours. Both become fields too, so changing the warning look changes all of it.

### What becomes editable
| What | Where to edit it |
|---|---|
| Pane colours: resting, rim, warning, hot, and the two colours the pane face pulses between while warned | fields on `Resources/GlassTile.prefab` |
| Standing-ring colour and board outline colour | **Confirmed glass board** → `GlassBoardView` |
| Menu fonts, font sizes, style and alignment | the saved Title, Instructions and each saved Choice's label (rows beyond the saved ones copy Choice 1) |
| Menu colours: background, button idle, hovered and pressed, text, secondary text | **Floor Fracture** → `GamePanel` |
| Menu width, margin, row height, row gap | **Floor Fracture** → `GamePanel` |
| Wall and sky colour of each of the six endings | **Floor Fracture** → `FallSequence` |
| Sound 3D settings: distances, priority, loop | each saved sound source |

Every field keeps today's value as its default. A check that compares a colour against a
constant reads the field instead.

### What the game still decides at Play
Positions and sizes of the board, panes, fog, lamps, lightning and chamber; mesh shapes; which
ending plays and its animation; menu text and layout; sound clips, loudness and pitch.

## Checks
- The existing gameplay checks pass with no change to what they assert, apart from the colour
  comparisons above.
- One new assertion in the gameplay checks: the saved scene contains the board, chamber, menu
  contents and sound sources before Play, and after Play starts there is still exactly one of
  each (the saved one), so a return to runtime creation is caught.
- While working, verify only the area just changed. Run the full gameplay checks once at the
  end, and tell Wais first. They need `XR_RUNTIME_JSON` pointed at a missing file in the Editor
  process, cleared again afterwards.
- The Editor cannot confirm look, comfort or frame rate on the headset. After the Editor checks
  pass, make a Quest development build with the project's existing build command. The work is
  finished when Wais has played that build once on his Quest 3 and seen nothing changed.
  (Claude's call.)

## Order of work
One area at a time, each ending with the command re-run, a targeted check and a commit:
sound sources, menu, fall chamber, glass board with the pane prefab, the Inspector fields, then
`FLOOR_FRACTURE.md` and `README.md`.

Commits go to a new branch, `scene-objects`, pushed to `origin` after each step. `main` is left
alone until the full gameplay checks pass.

## Left to the builder
- Names and locations of the adopt helper and the Editor command, and whether Prepare game
  scene calls the command.
- How Editor code reaches non-public builder methods.
- How resting colours are re-applied in edit mode.
- File names and folders for the saved meshes and materials.
- Where the saved menu sits in the scene (suggested: where it would open for a head at 1.6 m
  looking along +Z from the origin).
- The exact state cleared on a re-fit, found by reading each board script.
