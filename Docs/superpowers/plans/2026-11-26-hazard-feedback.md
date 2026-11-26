# Hazard Playtest Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the user's playtest findings: the hum is inaudible, the thunder is too quiet, fog pops in and out per wave instead of belonging to the level, flashes are too dim, and tiles are too small.

**Architecture:** Each finding is one task touching its own area: audio constants and clip (GameSound), the fog lifetime (Rules `WaveHazards` + `GlassBoardView` + `FloorFog` + GameFlow call sites + fog shader), flash brightness (`Lightning` + shaders), and pane pitch (`FloorSurvey.Connected` constants + `BoardState.MaxPitch` + jump grace). A final task runs the full verification and the Quest build.

**Tech Stack:** Unity 6000.6.10f1, URP 17 Mobile, Meta XR SDK 209 / Meta XR Audio, NUnit EditMode, GameplaySmoke Play Mode suite.

**Spec:** `Docs/superpowers/specs/2026-11-26-level-hazards-design.md`, especially the section "Playtest feedback changes (2026-11-26)".

## Global Constraints

- Break timing stays frozen: `WavePacing`, `SwitchSeconds`, reveal windows, `GapGrace` 0.3, every `BreaksAt`/`Duration`/`Deadline`. The one ruled exception is `JumpGapGrace` 0.45 → 0.55 (Task 4).
- `ExistingShapesKeepTheirSeededPatterns` keeps passing unchanged.
- Flash limits stay: at most 3 flashes in any 1 s window. With Reduced motion on, a strike is a single soft glow (peak ≤ 0.35) with no veil.
- Fog stays below 0.35 m above the board. The opacity cap of 0.70 (0.86 under a flash) does not change.
- Audio follows `GameSound.MasterVolume`, and `SetVolume(0)` silences it.
- A Unity Editor is open on the project, so use the live-Editor commands:
  - `unity recompile --format json`
  - `unity command test run --mode editor --filter <X> --result-only --timeout 900`
  - `unity command script eval --caller plugin --skill unity-cli '<C#>' --result-only`
- Do not run the full Play Mode smoke suite before Task 5.
- Commit and push after each task on `floor-fracture-game`, ending each message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Fog across a full level:** Ready → count-in → waves → BetweenWaves → mid-level Ready (moved-while-paused, tracking loss) → fall. Fog must never blink or re-roll mid-level. *Test: Task 2 smoke `VerifyFogBelongsToTheLevel`.*
2. **Fog during the fall:** it rides up with the glass and stays visible. *Test: Task 2 smoke `VerifyFogBelongsToTheLevel` (fall part).*
3. **Level change:** fog fades out during the 1.8 s clear and fades in at the next Ready, never overlapping two levels. *Test: Task 2 EditMode `FogFadeOutFitsInsideTheLevelClear`.*
4. **Small rooms still fit a board after the pitch change.** *Test: Task 4 keeps `FloorSurveyTests` compact-room cases green.*
5. **Reduced motion has no veil.** *Test: Task 3 smoke `VerifyLightning`.*

---

### Task 1: Audible hum and louder thunder

**Files:**
- Modify: `Assets/FloorFracture/Scripts/Presentation/Endings/GameSound.cs:18` (`LampGain`), `CreateLampVoice` (~132-137), `PlayThunder` / thunder voice setup (~91)
- Replace: `Assets/FloorFracture/Resources/Endings/Audio/Thunder.wav` (keep its .meta)

- [ ] **Step 1: Lamp hum.**
  - Set `LampGain = .5f`.
  - In `CreateLampVoice`, set `voice.minDistance = 2f`. Keep logarithmic rolloff and maxDistance 16.
- [ ] **Step 2: Thunder voices.** Set `minDistance = 8f`. Keep maxDistance 40, gain 1 × master and logarithmic rolloff.
- [ ] **Step 3: Thunder clip.**
  - Process the current `Thunder.wav` with a stdlib-only Python peak limiter: ceiling −9 dBFS, ~1 ms attack/look-ahead, ~50 ms release. Then apply +8 dB makeup gain.
  - Result: peak ≤ −0.9 dBFS, RMS raised by ≈ 8 dB from −28.5 dBFS.
  - Put the script in the scratchpad, not the repo. Run it with `python3 -I`, and write to a temporary file first.
  - Verify with `afinfo`: mono, 48 kHz, Int16, 7.0 s. Measure and print the before/after peak and RMS.
- [ ] **Step 4: Verify.**
  - `unity recompile` shows 0 errors and 0 warnings.
  - The EditMode suite is green.
  - In the open Editor, with script eval, confirm a lamp voice's `volume` is 0.5 × MasterVolume at level 1 and a thunder voice's `minDistance` is 8.
- [ ] **Step 5: Commit and push.** Commit message: `Make the lamp hum audible and the thunder as loud as the music`

---

### Task 2: Fog belongs to the level

**Files:**
- Modify: `Assets/FloorFracture/Scripts/Rules/WaveHazards.cs` (per-wave pool, new `LevelFog`, new `FogFade`)
- Modify: `Assets/FloorFracture/Scripts/Glass/GlassBoardView.cs:182-203` (separate fog lifetime)
- Modify: `Assets/FloorFracture/Scripts/Glass/FloorFog.cs` (`_BoardY`, edge roll-in, fade)
- Modify: `Assets/FloorFracture/Resources/FloorFractureFog.shader:44-61`
- Modify: `Assets/FloorFracture/Scripts/Game/GameFlow.cs:21-40` (Stage setter), `Ready()` (~224), level-cleared branch (~358-361)
- Modify tests: `Tests/Rules/WaveHazardsTests.cs:25-37,94-100`, `Tests/Rules/BoardRuleChecks.cs:551,586`
- Modify smoke: `Editor/Verification/GameplaySmoke.cs:621,681`, `GameplaySmoke.Hazards.cs:119-137`

**Interfaces:**
- `public static WaveHazards LevelFog(int level, int seed)` returns `new WaveHazards(density, patches, 0, Array.Empty<double>(), visualSeed)`:
  - density 0 below level 2, otherwise `0.35f + 0.65f*Clamp01((level-2)/8f)`
  - patches 0 below level 3, then `1 + (level>=7) + (level>=10)`
  - visualSeed derived from `unchecked(seed ^ level*(int)0x9E3779B1)`
  - Same inputs always give the same result.
- `WaveHazards.Roll` now rolls only spotlights (from level 4) and lightning (from level 5). Its `FogDensity` and `FogPatches` are always 0. Count:
  - L1–3: 0
  - L4–6: `roll(2)`
  - L7–9: 1
  - L10+: `1 + roll(2)`
- `public static float FogFade(double now, double shownAt, double hiddenAt)`: smoothstep 0→1 over 3.0 s after `shownAt`, then 1→0 over 1.5 s after `hiddenAt`. It returns exactly 0 at and after `hiddenAt + 1.5`.
- `WavePlan.Fog` becomes `false`. Remove it if nothing outside metrics needs it; otherwise keep it as a constant `false` and update `QuestRunMetrics`.
- `public void GlassBoardView.SetFog(WaveHazards fog, double now)`:
  - same fog values already shown or fading in: no-op
  - new fog: `FloorFog.Show` and `fogShownAt = now`
  - `WaveHazards.None`: `fogHiddenAt = now`, unless already hidden
- `DrawHazards` ticks the fog with `FogFade`. `SetHazards` no longer touches the fog.
- GameFlow:
  - `Ready()` calls `BoardView.SetFog(WaveHazards.LevelFog(Level, State.Seed), GameplayTime)`.
  - The level-cleared branch calls `SetFog(WaveHazards.None, now)`.
  - The Stage setter hides fog only for Results, Welcome, level select, Settings, PlayArea, Development and Room. In practice: every stage except Ready, CountIn, Countdown, BetweenWaves, Paused and Fall.
- Fog shader:
  - Measure height from `_BoardY` (set each Tick from `transform.position.y`), not `_MappedFloorY`, for the falloff and the noise. `_MappedFloorY` stays for `GlassFloorVisibility`.
  - Edge roll-in: `alpha *= saturate((_Fade*(min(_Extent.x,_Extent.y)+.5) - inward + .3*(noise-.5))/.5)` with `inward = min(edge.x, edge.y)`. It replaces the flat `_Fade` multiply.

- [ ] **Step 1: Write the failing EditMode tests** in `WaveHazardsTests.cs`:

```csharp
[Test] public void LevelFogIsStableAndUnlocksByLevel() {
    Assert.That(WaveHazards.LevelFog(1, 7).FogDensity, Is.Zero);
    Assert.That(WaveHazards.LevelFog(2, 7).FogDensity, Is.EqualTo(.35f).Within(1e-4f));
    Assert.That(WaveHazards.LevelFog(2, 7).FogPatches, Is.Zero);
    Assert.That(WaveHazards.LevelFog(3, 7).FogPatches, Is.EqualTo(1));
    Assert.That(WaveHazards.LevelFog(7, 7).FogPatches, Is.EqualTo(2));
    Assert.That(WaveHazards.LevelFog(10, 7).FogPatches, Is.EqualTo(3));
    Assert.That(WaveHazards.LevelFog(6, 7).VisualSeed, Is.EqualTo(WaveHazards.LevelFog(6, 7).VisualSeed));
    Assert.That(WaveHazards.LevelFog(6, 7).Spotlights + WaveHazards.LevelFog(6, 7).Strikes.Count, Is.Zero);
}
[Test] public void WavesNoLongerRollFog() // Roll(level 1..14, numbers 1..8, seeds 0..199): FogDensity == 0 and FogPatches == 0; levels 1..3 have !Any
[Test] public void FogFadesInSlowlyAndOutWithinTheLevelClear() {
    Assert.That(WaveHazards.FogFade(0, 0, double.PositiveInfinity), Is.Zero);
    Assert.That(WaveHazards.FogFade(1.5, 0, double.PositiveInfinity), Is.InRange(.4f, .6f));
    Assert.That(WaveHazards.FogFade(3.0, 0, double.PositiveInfinity), Is.EqualTo(1f));
    Assert.That(WaveHazards.FogFade(10.0, 0, 10.0), Is.EqualTo(1f));
    Assert.That(WaveHazards.FogFade(11.5, 0, 10.0), Is.Zero);   // 1.5 s < the 1.8 s level-clear pause
}
```
  Update `HigherLevelsCombineHazards`, `HazardsUnlockByLevel` and `EveryPlanCarriesHazards…` to the new per-wave pool. At L12 every wave has ≥ 1 per-wave kind, and both kinds each appear on more than 30% of waves.

- [ ] **Step 2: Run them to verify they fail.** Run `unity command test run --mode editor --filter WaveHazards --result-only`. Expected: compile failure on `LevelFog`.

- [ ] **Step 3: Implement** the Interfaces above.

- [ ] **Step 4: Smoke** in `GameplaySmoke.Hazards.cs`. Replace the forced-fog parts of `VerifyHazardVisuals` with `SetFog`, then add `VerifyFogBelongsToTheLevel`:
  - From Welcome, start a development run at level 3 (use whatever level-picker or development hook the smoke already uses), or call `SetFog(LevelFog(3, State.Seed))` on the Ready board.
  - Record the fog renderers' material `_Fade` at Ready + 3.2 s; it should be ≥ 0.99.
  - Start the wave and play through two waves, standing on the guaranteed tile.
  - Require that the fog renderers stay enabled with `_Fade` ≥ 0.99 on every sampled frame, and that the fog VisualSeed never changes.
  - Then lose a wave by standing still and enter Fall. Require the fog renderers to stay enabled with `_Fade` ≥ 0.99 for the first 1.5 s of the fall, while the board's world y rises.
  - Choose Results → Main menu, and require the fog to be off within 1.6 s.
  - Update `LevelThree` (GameplaySmoke.cs:621,681) to the new rule: fog is on at level 3 from Ready onward.

- [ ] **Step 5: Verify.**
  - `unity recompile` shows 0 errors and 0 warnings.
  - The EditMode suite is green.
  - Quick visual check in the open Editor with off-screen cameras: fog rolls in from the edges over ~3 s.

- [ ] **Step 6: Commit and push.** Commit message: `Give each level one fog that rolls in, lasts through the fall and fades at the end`

---

### Task 3: Brighter lightning

**Files:**
- Modify: `Assets/FloorFracture/Scripts/Glass/Lightning.cs` (veil, sky size and alpha)
- Modify: `Assets/FloorFracture/Resources/FloorFractureSurface.shader:84-85` (glass additive ×2)
- Modify smoke: `Editor/Verification/GameplaySmoke.Hazards.cs` (`VerifyLightning`)

**Interfaces:**
- `Lightning` builds a "Flash veil" renderer: `FallMeshes.Box()` (`Presentation/Endings/FallMeshes.cs:74`) with a `ViewFade` material (`Resources/ViewFade.shader`) and `renderQueue = 4190`, below the fall veil at Overlay+200.
- Each Tick the veil sits at `Camera.main` with scale 1.5 (pattern: `FallScenery.cs:168-171`). `_Tint = (.85, .9, 1, .5 × I)` through a MaterialPropertyBlock, where `I = FlashIntensity × fade`.
- With Reduced motion on, the veil is disabled.
- The veil is enabled only while `I > 0`. It is disabled in `OnDisable` and destroyed in `OnDestroy`.
- Sky disc: `SkyRadius = 4`, alpha `.8 × I`.
- Glass additive: `tint += I*(.68,.74,.84)` (twice today's).

- [ ] **Step 1: Write the failing smoke assertions** in `VerifyLightning` (deterministic `DrawHazards(strike + .03)` reads, as it already does):
  - full motion: the veil renderer is enabled, its `_Tint.a` ≥ 0.45, and `Shader.GetGlobalFloat("_FloorFractureFlash")` > .9
  - at strike + .12: the veil is disabled or its alpha is 0
  - Reduced motion: the veil is disabled throughout the strike, and the peak is ≤ .35
- [ ] **Step 2: Verify it fails to compile, or fails** (the veil does not exist yet).
- [ ] **Step 3: Implement** the Interfaces above.
- [ ] **Step 4: Verify.**
  - Recompile shows 0 errors and 0 warnings, and the EditMode suite is green (`FlashesNeverExceedThreePerSecond` and `ReducedMotionStrikeIsOneSoftGlow` unchanged).
  - Quick visual check in the open Editor.
- [ ] **Step 5: Commit and push.** Commit message: `Make lightning flash the whole view`

---

### Task 4: Bigger tiles

**Files:**
- Modify: `Assets/FloorFracture/Scripts/Room/FloorSurvey.Connected.cs:9-11` (`DefaultPaneLimit`, `PreferredMinPitch`, `ConnectedPitches`)
- Modify: `Assets/FloorFracture/Scripts/Rules/BoardState.cs:13` (`MaxPitch`)
- Modify: `Assets/FloorFracture/Scripts/Rules/PlayerTileTracker.cs:109` (`JumpGapGrace`)
- Modify: `Assets/FloorFracture/Scripts/Game/GameFlow.cs:197` (menu label)
- Modify tests:
  - `Tests/Room/FloorSurveyTests.cs:334-341,396`
  - `Tests/Room/BoardFitterTests.cs:260-261` (and re-check `:17,259-266,314,338`)
  - `Tests/Rules/BoardRulesEditorTests.cs:19-20,46`
  - `Tests/Rules/BoardRuleChecks.cs:374,475-476,812`
- Modify smoke: `Editor/Verification/GameplaySmoke.cs:168,346`
- Modify docs: `FLOOR_FRACTURE.md:23`, `Docs/Quest playtest.md:25` (pitch and tile-count claims)

**Interfaces:**
- `ConnectedPitches = { .75f, .72f, .69f, .65f, .60f, .55f, .50f, .45f, .40f }`
- `PreferredMinPitch = .69f`
- `DefaultPaneLimit = 12`
- `BoardState.MaxPitch = .75f`. `MinPitch` .50 and `CompactMinPitch` .40 are unchanged.
- `JumpGapGrace = .55`
- Menu label "Up to 12 tiles"

- [ ] **Step 1: Write the failing tests.**
  - In `FloorSurveyTests`, change the capped-fit expectations to ≤ 12 panes at pitch .69–.75.
  - Add `BiggerPanesGiveAboutThreeQuartersAsManyPanes`. Use a 4.2 × 2.8 m empty room fixture, built the way the surrounding tests build theirs. Assert the default-mode fit has pitch ≥ .69 and a pane count ≤ 12 and ≥ 6.
  - In `BoardRulesEditorTests.cs:46`, the jump crossing now loses at .56 s instead of .46 s. Keep the Pending check at .38 s, and add one at .52 s (Pending).
- [ ] **Step 2: Run them to verify they fail.**
- [ ] **Step 3: Implement** the Interfaces above. Update every remaining test or smoke expectation that pins the old pitch band or the 16-pane cap. Don't loosen any fit-safety check (FootMargin, CellSize, ChecksEveryTile, Fits).
- [ ] **Step 4: Run the full EditMode suite.**
  - All green. The compact-room cases (`FloorSurveyTests.cs:289,375`) still pass.
  - Record the pane counts and pitches before and after for the fixtures in `BoardFitterTests` and `FloorSurveyTests` that print or assert them.
- [ ] **Step 5: Commit and push.** Commit message: `Fit bigger panes: about three quarters as many, each 15% wider`

---

### Task 5: Full verification and Quest build

- [ ] **Step 1:** Run the full EditMode suite. Expected: all green.
- [ ] **Step 2:** Ask the controller to confirm the user is not using the Editor. Then run the full Play Mode smoke once, from the open Editor: set `XR_RUNTIME_JSON=/nonexistent/openxr_runtime.json` in the Editor process, invoke "Tools/Floor Fracture/Verify gameplay in Play Mode", and ALWAYS clear the variable afterwards. Fix failures with targeted commits; this allows up to 3 runs.
- [ ] **Step 3:** Build the APK from the open Editor (the active target is Android) through `GameProjectSetup.BuildAndroid()`, with `FLOOR_FRACTURE_APK` pointing into the scratchpad, then clear the variable. Report the size and result.
- [ ] **Step 4:** Commit any fixes and push. Write a short headset checklist for the user.
