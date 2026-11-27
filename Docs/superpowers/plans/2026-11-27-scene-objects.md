# Scene Objects Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Save every object the game shows or plays sound through into `FloorFracture.unity`, have the game adopt those objects at Play, and move colours and menu styling into Inspector fields.

**Architecture:** Every builder changes from "always create" to "adopt the child with this name, create it if missing", through one helper class. An Editor command runs the same builders in edit mode, snapshots their generated meshes and materials as asset files, and saves the scene. `GameFlow` and `FallSequence` hold saved references to the board and the chamber and switch them off at startup.

**Tech Stack:** Unity 6000.6.10f1, C#, Meta XR SDK. The open Editor is driven with the `unity` command-line tool.

**Spec:** `Docs/superpowers/specs/2026-11-27-scene-objects-design.md`. Read it first; this plan does not repeat its reasons.

## Global Constraints

- No object is renamed, re-parented or grouped. Names and nesting at Play are the ones the game has today.
- `GlassBoardView.Build` must still produce a full board on an empty object with `null` sound.
- Sound sources are direct children of the `GameFlow` object; exactly 15 are spatial, each with Meta's Spatial Audio block. `GameSound`, `OfflineNarration`, `FallSequence` stay on the `GameFlow` object.
- `GameObject.Find("Fall chamber")` finds nothing while the chamber is not in use. Only one object has that name.
- No spare panes are kept switched off under the board.
- While no hazard shows, every renderer under `Floor fog`, `Overhead lights` and `Lightning` is disabled.
- Nothing is saved under Meta's camera rig, and nothing on layer 30.
- A saved component has the same saved fields in every build: no `[SerializeField]` inside `#if`.
- Play in the Editor writes nothing to disk. Never call `Destroy` on, or set a property of, an asset file.
- At Play, generated meshes and code-configured materials stay the source of truth; the saved files are snapshots.
- Verify only the area just changed. The full gameplay checks run once, in Task 6, after telling Wais.
- Commit and push to `scene-objects` after each task. Never commit `RuntimeActionBindings.json`. End commit messages with `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`.

## Review Focus

1. **A saved object was deleted by hand.** The game and the save command must recreate it, not throw. Pinned in Task 1, Step 8.
2. **An idle effect was left switched on in the scene** (a fog slice, a lamp, the chamber). The game must switch it off before the first frame. Pinned in Task 4, Step 9.
3. **The room is re-confirmed with a smaller, then a larger board in one session.** No pane, hazard or sound state may carry over. Pinned in Task 4, Step 3 (`VerifyRefit`).
4. **The player's saved volume is 0.** No music may be audible at startup now that `GameSound` wakes with the scene. Pinned in Task 1, Step 3.
5. **A release Quest build.** Editor-only objects and fields are absent there; the saved scene must load without a serialization mismatch. Pinned in Task 6, Step 5.

## Commands used in every task

```bash
P="/Users/wais/Projects/Bucknell Projects/CSCI 379/Final Project/Floor Fracture"
unity recompile --project-path "$P"                       # expect exit 0 and no errors
EVAL() { unity command script eval --caller plugin --skill unity-cli --project-path "$P" "$1"; }
EVAL 'FloorFracture.Editor.SceneObjectsSetup.Save();'      # the save command
EVAL 'FloorFracture.Editor.GameplaySmoke.CheckSavedScene("sound");'   # edit-mode check, one area or several
```

**Quick Play check** (about a minute; the Mac's OpenXR runtime is the Meta simulator, so it must be masked and unmasked):

```bash
EVAL 'System.Environment.SetEnvironmentVariable("XR_RUNTIME_JSON","/nonexistent/openxr.json");'
EVAL 'FloorFracture.Editor.GameplaySmoke.StartQuick();'
# wait for the newest Builds/Verification/PlayMode-*/report.json to contain "result": "passed"
EVAL 'System.Environment.SetEnvironmentVariable("XR_RUNTIME_JSON",null);'   # always, even after a failure
git -C "$P" status --short Assets                          # expect no output: Play wrote nothing
```

## File Structure

| File | Responsibility |
|---|---|
| Create `Assets/FloorFracture/Scripts/Game/SceneParts.cs` | Adopt-or-create helpers shared by every builder |
| Create `Assets/FloorFracture/Editor/SceneObjectsSetup.cs` | The save command and the mesh and material snapshots |
| Create `Assets/FloorFracture/Editor/Verification/GameplaySmoke.SavedScene.cs` | Edit-mode inventory check, Play-time "one of each" assertion, quick entry point |
| Create `Assets/FloorFracture/Generated/` | Mesh snapshots (written by the command) |
| Create `Assets/FloorFracture/Resources/Materials/` | Material files (written by the command) |
| Modify `Scripts/Presentation/Endings/GameSound.cs`, `Scripts/Presentation/OfflineNarration.cs` | Adopt sound sources |
| Modify `Scripts/Menu/GamePanel.cs`, `Scripts/Menu/Editor/MenuSceneSetup.cs` | Adopt menu objects; build a sample page without a rig |
| Modify `Scripts/Presentation/Endings/FallSequence.cs`, `FallScenery.cs` | Adopt the chamber; ending tints as fields |
| Modify `Scripts/Glass/GlassBoardView.cs`, `GlassTile.cs`, `FloorFog.cs`, `OverheadLights.cs`, `Lightning.cs`, `ShardPool.cs` | Adopt and re-fit the board; pane colours as fields |
| Modify `Scripts/Game/GameFlow.cs` | Use saved components and the saved board |
| Modify `Scenes/FloorFracture.unity`, `Resources/GlassTile.prefab` | Written by the command, never by hand |

Paths without a prefix are under `Assets/FloorFracture/`.

---

### Task 1: Shared helper, save command, checks and sound sources

**Files:**
- Create: `Scripts/Game/SceneParts.cs`, `Editor/SceneObjectsSetup.cs`, `Editor/Verification/GameplaySmoke.SavedScene.cs`
- Modify: `Scripts/Presentation/Endings/GameSound.cs:45-112,116-142`, `Scripts/Presentation/OfflineNarration.cs:16-24`, `Scripts/Game/GameFlow.cs:90-94`, `Editor/Verification/GameplaySmoke.cs:144-170`

**Interfaces:**
- Produces, in `Scripts/Game/SceneParts.cs` (`namespace FloorFracture`, `public static class SceneParts`):
  - `public static Transform Child(Transform parent, string name, out bool created, params System.Type[] components)`: the direct child with that name, else a new one with those components, parented with `worldPositionStays` false.
  - `public static T Require<T>(GameObject host) where T : Component`: the existing component, else a new one.
  - `public static GameObject Spawn(GameObject prefab, Transform parent, string name, out bool created)`: the direct child with that name, else an instance of the prefab renamed to it. In edit mode the instance is made with `PrefabUtility.InstantiatePrefab`, so it stays linked.
  - `public static Material MakeMaterial(string name, string shaderResource)`: a private copy of `Resources/Materials/<name>` when that file exists, else `new Material(Resources.Load<Shader>(shaderResource))`; named `name`, `HideFlags.DontSave`. Throws `InvalidOperationException("Missing Floor Fracture shader " + shaderResource)` when neither exists.
  - `public static void Release(Object item)`: `Destroy` at Play, `DestroyImmediate` in edit mode, nothing when `item` is null or an asset file.
- Produces, on `GameSound` and `OfflineNarration`: `public void Build()`, safe to call twice and in edit mode. `Awake` calls it.
- Produces, in `Editor/SceneObjectsSetup.cs` (`namespace FloorFracture.Editor`, `public static class SceneObjectsSetup`):
  - `[MenuItem("Tools/Floor Fracture/Save game objects into scene")] public static void Save()`
  - `public const string MeshFolder = "Assets/FloorFracture/Generated", MaterialFolder = "Assets/FloorFracture/Resources/Materials";`
  - `internal static void Snapshot(Transform root)`: under `root`, replaces every mesh and material that is not an asset file with an asset of the same name in those folders, creating or overwriting that asset.
- Produces, in `GameplaySmoke.SavedScene.cs` (partial class `FloorFracture.Editor.GameplaySmoke`):
  - `public static void CheckSavedScene(params string[] areas)`: areas are `"sound"`, `"menu"`, `"chamber"`, `"board"`; none means all. Loads the scene asset's contents without Play and throws one `InvalidOperationException` listing every expected path that is missing or present more than once.
  - `[MenuItem("Tools/Floor Fracture/Verify saved scene objects in Play Mode")] public static void StartQuick()`: runs the existing `Start()` with a session flag that ends `Scenario()` right after `VerifySavedSceneAdopted()`.
  - `private static void VerifySavedSceneAdopted()`: called in `Scenario()` once Welcome is reached, in quick and full runs.

- [ ] **Step 1: Write the inventory for sound.** In `GameplaySmoke.SavedScene.cs`, the `"sound"` area expects, as direct children of `Floor Fracture`: `Menu and countdown`, `Background music`, `Shaft air`, `Shaft roar`, `Ending ambience`, `Opening draft`, `Ending impact`, `Tar bubbling`, `Pane impact 0`–`2`, `Glass fragments 0`–`2`, `Shaft echo 0`–`2`, `Thunder 0`–`2`, `Offline narration`. It also expects `GameSound`, `OfflineNarration` on `Floor Fracture`; the 15 spatial ones to be linked instances of `Resources/GlassSpatialVoice.prefab`; no plain source with Play On Awake on; and `Ending ambience`, `Ending impact` and `Offline narration` to have no clip.

- [ ] **Step 2: Run it and see it fail.** `EVAL 'FloorFracture.Editor.GameplaySmoke.CheckSavedScene("sound");'`. Expected: an exception listing all 21 paths as missing.

- [ ] **Step 3: Write the Play assertion.** `VerifySavedSceneAdopted()` requires, counting inactive objects too: each name from Step 1 exists exactly once in the scene; `game.GetComponents<GameSound>().Length == 1`; and `Background music`'s `AudioSource.volume` is at most `GameSound.MasterVolume * 0.12f + 0.001f`. `StartQuick()` sets `PlayerPrefs` `ff.volume` to `0` before entering Play (the harness already restores it), and the quick path additionally requires `MasterVolume == 0` and `Background music` volume `== 0` on the first frame `GameFlow` is found.

- [ ] **Step 4: Implement `SceneParts`** with the five members above.

- [ ] **Step 5: Make sound adopt.** `GameSound.Voice(name, isSpatial)` uses `SceneParts.Spawn` or `SceneParts.Child`; each one-time setting in `Awake` (loop, `minDistance`, `maxDistance`, `priority`, `GainBoostDb`, the fixed clips, `playOnAwake = false`) is applied only when the source was created. `tarFilter` uses `SceneParts.Require`. `CreateLampVoice` adopts a `Lamp hum` child of the lamp. `Build()` begins by setting `volume = PlayerPrefs.GetInt("ff.volume", 3) / 4f`. `OfflineNarration` adopts `Offline narration` the same way. In `GameFlow.Start`, the four `AddComponent` calls become `SceneParts.Require`.

- [ ] **Step 6: Implement `SceneObjectsSetup.Save()` for sound.** Open `Assets/FloorFracture/Scenes/FloorFracture.unity` if it is not the active scene, find the single `GameFlow`, `Require` the four components on it, call `GameSound.Build()` and `OfflineNarration.Build()`, mark the scene dirty and save it. Later tasks add their area to this method.

- [ ] **Step 7: Recompile, save, check.** Run `unity recompile`, the save command, then `CheckSavedScene("sound")`. Expected: no exception. Run the save command a second time; `git diff --stat` must show the scene unchanged by the second run.

- [ ] **Step 8: Deleted object comes back.** `EVAL 'UnityEngine.Object.DestroyImmediate(UnityEngine.GameObject.Find("Thunder 1"));'`, run the save command, then `CheckSavedScene("sound")`. Expected: no exception.

- [ ] **Step 9: Quick Play check.** Expected: `"result": "passed"`, and `git status --short Assets` prints nothing afterwards.

- [ ] **Step 10: Commit and push.** Message: `Save the sound sources in the scene and have the game adopt them`.

---

### Task 2: Menu

**Files:**
- Modify: `Scripts/Menu/GamePanel.cs:90-147,171-197,251-273,376-424`, `Scripts/Menu/Editor/MenuSceneSetup.cs:24-27`, `Editor/SceneObjectsSetup.cs`, `Editor/Verification/GameplaySmoke.SavedScene.cs`

**Interfaces:**
- Consumes: `SceneParts.Child`, `SceneParts.Require`, `SceneObjectsSetup.Snapshot`.
- Produces, on `GamePanel`:
  - `public const float CanvasScale = 0.00058f;`
  - `public void BuildMenu()`: adopts or creates the background image, `Title`, `Instructions`, `Choice list`, `Choice list/Content`, `Title/Warning preview` (off) and `Start count` with `Start count/Count` (off). Needs no rig. `Initialize` calls it.
  - `public void BuildSample(string heading, string message, params string[] labels)`: the object-building and layout parts of `ShowPage` only; no pointers, sound, rig or re-centering. Leaves the panel active.

- [ ] **Step 1: Write the inventory for menu.** Under `Floor Fracture/Floor Fracture Menu`: `Title`, `Title/Warning preview` (inactive), `Instructions`, `Choice list` (inactive), `Choice list/Content`, `Choice 1`–`Choice 5`, each with `Label`. Under `Floor Fracture`: `Start count` (inactive) with `Count`. Also: the canvas has one `Image`; its `localScale.x` is `0.00058` within `1e-6`; `Choice 1/Label` text is `Start` and `Choice 5/Label` is `Development`; the subtree holds exactly one `RayInteractable` and one `PokeInteractable`; no object named `Editor left grip`, `Editor right grip`, `Editor left menu ray` or `Editor right menu ray` exists in the scene.

- [ ] **Step 2: Run `CheckSavedScene("menu")` and see it fail** on every path except the canvas.

- [ ] **Step 3: Extend the Play assertion.** Exactly one each of `Title`, `Instructions`, `Choice list`, `Start count` in the scene; `game.GetComponents<GamePanel>().Length == 1`; `Choice 1` is the row the panel is using (its `Label` text equals the first Welcome choice).

- [ ] **Step 4: Make the menu adopt.**
  - `Label(...)` and `BuildRow(index)` adopt by name; font, size, style, alignment and wrap settings are applied only when created. A row beyond the saved ones is `Instantiate`d from `Choice 1` and renamed. `onClick` and the scroll listener are wired at Play for adopted and created rows alike, exactly once.
  - The canvas `Image` uses `SceneParts.Require`.
  - `Start count` is saved as a direct child of `Floor Fracture` at local position `(0, 1.72, 0.95)`, scale `0.001`, and moved under `Head` with today's local pose on the first `ShowStartCount`.
  - `Initialize` hides `Start count` and `Warning preview` if they were saved switched on.
  - `MenuSceneSetup` sets the canvas scale to `GamePanel.CanvasScale`; `Initialize` uses the same constant.

- [ ] **Step 5: Add the menu to `Save()`.** `BuildMenu()`, then `BuildSample("Floor Fracture", "Watch which panes flash. Step onto one that stayed clear before the glass breaks.", "Start", "Choose level", "Settings", "Play area", "Development")`. Place the canvas at world position `(0, 1.36, 0.56)`, rotation identity. Call `Snapshot` on `Title/Warning preview`.

- [ ] **Step 6: Recompile, save, check.** `CheckSavedScene("sound", "menu")` passes; a second save leaves `git diff` unchanged.

- [ ] **Step 7: Quick Play check** passes; `git status --short Assets` prints nothing.

- [ ] **Step 8: Look at it.** Capture the Scene view or Game view with the Editor not playing and confirm the Welcome page reads correctly at its real size.

- [ ] **Step 9: Commit and push.** Message: `Save the menu's Welcome page in the scene at its real size`.

---

### Task 3: Fall chamber

**Files:**
- Modify: `Scripts/Presentation/Endings/FallSequence.cs:11-15,51,123-126`, `Scripts/Presentation/Endings/FallScenery.cs:58-125,160-170,360-414`, `Editor/SceneObjectsSetup.cs`, `Editor/Verification/GameplaySmoke.SavedScene.cs`

**Interfaces:**
- Consumes: `SceneParts.Child`, `SceneParts.Spawn`, `SceneParts.MakeMaterial`, `SceneParts.Release`, `SceneObjectsSetup.Snapshot`.
- Produces:
  - `internal FallScenery(Transform saved = null)`: uses `saved` as the root when given, else creates `Fall chamber`.
  - On `FallSequence`: `[SerializeField] Transform chamber;` and `public Transform BuildChamber()`, which constructs the scenery once and returns its root. `PrepareChamber` passes `chamber`.
  - `developmentEnding` and its enum move outside `#if UNITY_EDITOR || DEBUG`; only the code that acts on it stays inside.

- [ ] **Step 1: Write the inventory for chamber.** A scene root `Fall chamber`, inactive, with children `Shaft walls`, `Shaft landmarks`, `Left brick column`, `Right brick column`, `Chamber darkness`, `Hazard surface`, `Floor Zone water ripples`, `Lava flames`, `Particles`, `Water spray`, `Entry bubbles`, `Fall dust`, `View fade`, `View fade/Submerged view`, `Water contact splash`. Every `MeshFilter` except `Particles` has a mesh that is an asset file; every renderer's material is an asset file; `FallSequence.chamber` on `Floor Fracture` references that root.

- [ ] **Step 2: Run `CheckSavedScene("chamber")` and see it fail.**

- [ ] **Step 3: Extend the Play assertion.** Counting inactive objects, exactly one object is named `Fall chamber`; it is inactive at Welcome; and it is the object `FallSequence.chamber` referenced before Play.

- [ ] **Step 4: Make the chamber adopt.** `Part` adopts by name under the root and applies renderer flags only when created; it always assigns the generated mesh and the code-made material. The six materials come from `SceneParts.MakeMaterial` under the names `Chamber surface`, `Chamber background`, `Chamber fade`, `Chamber interior`, `Chamber bubbles`, `Chamber dust`, then get today's settings. The splash adopts `Water contact splash` through `SceneParts.Spawn`. `Floor Zone water ripples` is always adopted or created, and deactivated when the shader is unsupported. `Dispose` goes through `SceneParts.Release`. `FallSequence.Awake` deactivates `chamber` when it is set.

- [ ] **Step 5: Add the chamber to `Save()`.** `BuildChamber()`, spawn the splash, `Snapshot` the root, assign `FallSequence.chamber`, leave the root inactive.

- [ ] **Step 6: Recompile, save, check.** `CheckSavedScene("sound", "menu", "chamber")` passes; a second save leaves `git diff` unchanged.

- [ ] **Step 7: Quick Play check** passes; `git status --short Assets` prints nothing.

- [ ] **Step 8: Commit and push.** Message: `Save the fall chamber in the scene, switched off until a fall`.

---

### Task 4: Glass board, pane prefab and re-fit

**Files:**
- Modify: `Scripts/Glass/GlassBoardView.cs:63-112,279-283`, `Scripts/Glass/GlassTile.cs:22-56`, `Scripts/Glass/FloorFog.cs:29-54`, `Scripts/Glass/OverheadLights.cs:30-71,127-133`, `Scripts/Glass/Lightning.cs:28-48`, `Scripts/Glass/ShardPool.cs:38-48`, `Scripts/Game/GameFlow.cs:77,133-147`, `Editor/SceneObjectsSetup.cs`, `Editor/Verification/GameplaySmoke.SavedScene.cs`, `Resources/GlassTile.prefab` (through the command)

**Interfaces:**
- Consumes: everything `SceneParts` produces; `SceneObjectsSetup.Snapshot`.
- Produces:
  - `GlassTile.Part(parent, name, mesh, material)` keeps its signature and adopts by name; `GlassTile.MakeMaterial(path)` becomes `SceneParts.MakeMaterial(path, path)`.
  - `GlassBoardView.Build(BoardState, Pose, GameSound, float)` keeps its signature and may be called any number of times on the same object.
  - On `GameFlow`: `[SerializeField] GlassBoardView savedBoard;` and `void Awake()`, which deactivates `savedBoard`.
  - `private static IEnumerator VerifyRefit()` in `GameplaySmoke.SavedScene.cs`, yielded from `Scenario()` straight after the existing full-floor refit step.

- [ ] **Step 1: Write the inventory for board.** A scene root `Confirmed glass board` with `GlassBoardView` and `ShardPool`; 36 children `Glass 0, 0` … `Glass 5, 5`, each a linked instance of `Resources/GlassTile.prefab` with children `Smoky pane`, `Glass sides and bottom`, `Rim`, `Fissures`; children `Occupied square`, `Opening stencil`, `Perimeter`, `Falling fragments`; `Floor fog` with `Floor fog 0`–`3`; `Overhead lights` with `Lamp 0`–`2` (each inactive, each with a `Lamp hum` child), `Spotlight beam 0`–`2`, `Spotlight pool 0`–`2`; `Lightning` with `Sky flash`, `Flash veil`. Also: every renderer under `Floor fog`, `Overhead lights` and `Lightning` is disabled, as are `Fissures`, `Occupied square` and `Opening stencil`; every mesh and material is an asset file; pane spacing is `0.62` within `1e-4`; `GameFlow.savedBoard` references the root; the prefab asset itself has the four part children.

- [ ] **Step 2: Run `CheckSavedScene("board")` and see it fail.**

- [ ] **Step 3: Extend the Play assertions.**
  - `GameplaySmoke.Start()` now calls `CheckSavedScene()` (all areas) before it enters Play, so a full or quick run refuses a scene with anything missing.
  - `VerifySavedSceneAdopted()`: counting inactive objects, exactly one `GlassBoardView` exists and it is the one `savedBoard` referenced; the number of `GlassTile` children of the board equals `game.State.Layout.PaneCount`; every pane is named `Glass {i % Width}, {i / Width}` for a cell the layout contains; exactly one each of `Floor fog`, `Overhead lights`, `Lightning`, `Opening stencil`, `Falling fragments`, `Occupied square`, `Perimeter`.
  - `VerifyRefit()`: after the larger re-fit, the same pane-count and naming requirements hold; `game.BoardView` is still the saved object; `game.BoardView.Hazards` and `.Fog` are `WaveHazards.None`; no pane has `Fissures` enabled; no `AudioSource` under the board is on an active object; `Falling fragments` is disabled.

- [ ] **Step 4: Make the board parts adopt.** `GlassTile.Part` and `Initialize` adopt children by name and always assign the generated meshes (`GlassGeometry.Face`, `BlockSides`, `SafeRim`, `Cracks`) and the given materials. `FloorFog`, `OverheadLights`, `Lightning` and `ShardPool` adopt their children; each `Build` or `Initialize` first releases the meshes, textures and materials its previous call made, and resets every field a fresh component would have (read each file; at least: fog `patches`, `paths`, `drawn`, `placed`, `hazards`; lamp `litAt`, `dimAt`, `paths`; lightning strike state; shard list and `FullFall`).

- [ ] **Step 5: Make `GlassBoardView.Build` a re-fit.** Gather the existing `GlassTile` children; give one to each cell the layout contains, in index order, instantiating the pane prefab through `SceneParts.Spawn` when they run out; rename, place and scale each; detach and release the surplus in the same call. Reset `hazards`, the four hazard and fog timestamps, `Fog`, `displayedSliders`, `practiceHole`, `openPanes`, and each reused pane's warning state and crack voice. Release the previous perimeter mesh and materials before making new ones.

- [ ] **Step 6: Use the saved board in `GameFlow`.** `Awake` deactivates `savedBoard`. `StartBoard` builds on the existing `BoardView`, else on `savedBoard`, else on a new `Confirmed glass board` object; it no longer destroys a board.

- [ ] **Step 7: Add the board and the prefab to `Save()`.** First give `Resources/GlassTile.prefab` its four parts by loading its contents, calling `Initialize(pane material, body material, 1f)`, `Snapshot`, and saving the prefab. Then adopt or create the scene root `Confirmed glass board`, call `Build(new BoardState(6, 6, 379, 0.62f), Pose.identity, sound, WaveHazards.LampHeight(0, null))`, `Snapshot` the root, assign `GameFlow.savedBoard`, and leave the root active.

- [ ] **Step 8: Recompile, save, check.** `CheckSavedScene()` with no arguments passes; a second save leaves `git diff` unchanged. Then the quick Play check passes and `git status --short Assets` prints nothing. (In the Editor fixture the room fits at most 12 panes, so this run already removes 24 saved panes.)

- [ ] **Step 9: Idle effects left on.** In the open scene, enable the `Floor fog 0` renderer, activate `Lamp 0` and activate `Fall chamber`; save the scene; run the quick Play check. Expected: passed. Then `git checkout -- Assets/FloorFracture/Scenes/FloorFracture.unity` and reload the scene.

- [ ] **Step 10: Look at it.** With the Editor not playing, capture the Scene view from above the origin and confirm a 6 × 6 board of panes with an outline is visible.

- [ ] **Step 11: Commit and push.** Message: `Save a 6 x 6 preview board in the scene and re-fit it to the room at Play`.

---

### Task 5: Colours and menu styling in the Inspector

**Files:**
- Modify: `Scripts/Glass/GlassTile.cs:8-11,89-104`, `Scripts/Glass/GlassBoardView.cs:94,102`, `Scripts/Menu/GamePanel.cs:22-31` and every use of those constants, `Scripts/Presentation/Endings/FallSequence.cs`, `Scripts/Presentation/Endings/FallScenery.cs:28-35`, `Editor/Verification/GameplaySmoke.Floor.cs:103`, `Editor/Verification/GameplaySmoke.SavedScene.cs`

**Interfaces (every default is today's value):**
- `GlassTile`: `[SerializeField] Color resting = Safe, rim = Edge, warning = Danger, hot = Fire, warnedFaceDim = new(.36f,.24f,.23f,.79f), warnedFaceBright = new(1f,.56f,.13f,.90f);`, `public Color Resting => resting;`, `public void CopyColours(GlassTile from)`.
- `GlassBoardView`: `[SerializeField] Color ringColour = new(.90f,1f,.73f), outlineColour = new(.61f,.82f,.85f,.96f);`
- `GamePanel`: `[SerializeField] Color paper = new Color32(13,32,41,249), idle = new Color32(35,66,76,255), hovered = new Color32(55,109,116,255), pressed = new Color32(119,196,183,255), ink = new Color32(237,247,248,255), secondary = new Color32(192,216,221,255);` and `[SerializeField] float width = 672, margin = 32, rowHeight = 72, rowGap = 14;`
- `FallSequence`: `[SerializeField] Color[] wallTints, skyTints;` with the six values each from `FallScenery.cs:28-35` as initialisers; `FallScenery`'s constructor becomes `FallScenery(Transform saved, Color[] wallTints, Color[] skyTints)`, and an array that is not six long falls back to the defaults.

- [ ] **Step 1: Write the failing check.** In `VerifySavedSceneAdopted()`: the `_Color` in the property block of the first pane's `Smoky pane` equals that pane's `Resting`; `Perimeter`'s equals the board's `outlineColour`; the canvas `Image.color` equals the panel's `paper`; `Title`'s text colour equals `ink`. In `CheckSavedScene("board")`: the same pane and outline comparison holds in edit mode, without Play. Expected now: does not compile, the members do not exist.

- [ ] **Step 2: Add the fields and use them.** Replace each constant's uses with its field. The Settings `Warning preview` calls `CopyColours` from the pane prefab's `GlassTile` when it is created or adopted. `GameplaySmoke.Floor.cs:103` compares with the pane's `Resting`.

- [ ] **Step 3: Show resting colours in edit mode.** `GlassTile`, `GlassBoardView` and `GamePanel` each get an `OnValidate` that, only when not playing, applies the resting colours to their adopted children (panes: face, sides and rim; board: ring and outline; panel: background, title, instructions, rows and row labels).

- [ ] **Step 4: Recompile, save, check.** `CheckSavedScene()` passes; the quick Play check passes; `git status --short Assets` prints nothing.

- [ ] **Step 5: An edit carries into the game.** On `Resources/GlassTile.prefab` set `resting` to pure red and on `GamePanel` set `paper` to pure blue; save; run the quick Play check. Expected: passed (the assertions in Step 1 compare against the fields). Then `git checkout -- Assets/FloorFracture/Resources/GlassTile.prefab Assets/FloorFracture/Scenes/FloorFracture.unity` and reload.

- [ ] **Step 6: Commit and push.** Message: `Put pane, board, menu and ending colours and the menu sizes in the Inspector`.

---

### Task 6: Documents, full checks and the Quest build

**Files:**
- Modify: `FLOOR_FRACTURE.md` ("Where the game lives"), `README.md` (the tools paragraph near line 47)

- [ ] **Step 1: Prepare game scene still works.** `EVAL 'FloorFracture.Editor.GameProjectSetup.Prepare();'`, then `CheckSavedScene()`. Expected: no exception, and `git diff --stat` shows no change to the scene or the pane prefab.

- [ ] **Step 2: Update the two documents.** `FLOOR_FRACTURE.md` says which objects are saved in the scene, names the save command, and includes the "What becomes editable" table from the spec. `README.md` lists the two new menu items beside the existing ones.

- [ ] **Step 3: Tell Wais, then run the full gameplay checks once.** Mask `XR_RUNTIME_JSON` as in the quick check, `EVAL 'FloorFracture.Editor.GameplaySmoke.Start();'`, wait about nine minutes for `"result": "passed"` in the newest report, and unmask. If it fails, fix the cause and re-run only the quick check until the cause is gone, then run the full checks once more.

- [ ] **Step 4: Commit and push.** Message: `Describe the saved scene objects and how to rebuild them`.

- [ ] **Step 5: Quest development build.** `EVAL 'FloorFracture.Editor.GameProjectSetup.BuildAndroid();'`. Expected: an APK under `Builds/` and no error in the Editor log mentioning a serialization layout or a missing script. If the Quest is connected, install it and read its log for errors from Floor Fracture code during launch.

- [ ] **Step 6: Hand over to Wais** for one play-through on the headset. `main` is not touched until he says the build is unchanged.
