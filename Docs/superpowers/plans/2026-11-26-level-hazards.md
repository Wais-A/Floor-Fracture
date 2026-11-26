# Level Hazards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make later levels harder through moving glass and visibility hazards (more sliders, patrol panes, ride finales, real fog, sweeping spotlights, lightning with spatial thunder) without touching break timing.

**Architecture:** All decisions live in the pure-C# rules assembly (`FloorFracture.Rules`), so EditMode tests cover them. That covers which hazards a wave gets, strike times, flash curves, fades, lamp height, slider lanes, patrol paths and ride finales. `WavePlan` carries a new `WaveHazards`. `GlassBoardView` owns three new view components (`FloorFog`, `OverheadLights`, `Lightning`), which `GameFlow` drives through one `SetHazards` call per wave. Audio goes through `GameSound` with the existing spatial voice prefab.

**Tech Stack:** Unity 6000.6.10f1, URP 17 (Mobile renderer, no post-processing), Meta XR SDK 209 (passthrough, depth occlusion, Meta XR Audio), NUnit EditMode tests, the GameplaySmoke Play Mode suite.

**Spec:** `Docs/superpowers/specs/2026-11-26-level-hazards-design.md`

## Global Constraints

- Break timing is frozen. Do not edit `WavePacing`, `SwitchSeconds`, reveal windows, `GapGrace`/`JumpGapGrace`, or any `BreaksAt`/`Duration`/`Deadline` computation.
- Hazard rolls use their own random stream (`new WavePlanner.Generator(seed ^ 0x5F3759DF)`). New board draws (patrol choice, ride choice) are appended after the existing `ChooseSliders` draws. `ExistingShapesKeepTheirSeededPatterns` must keep passing unchanged.
- Fog stays below 0.35 m above the mapped floor. Combined fog opacity on any pixel is ≤ 0.70.
- Lightning: no more than 3 flashes in any 1 s window. With Reduced motion on, one soft glow (peak ≤ 0.35) and no flicker.
- Unlock levels: fog 2, fog patches 3, spotlights 4, lightning 5, patrol panes 5, ride finale 6.
- Slider cap per wave: 1 at levels 3–5, 2 at 6–8, 3 from 9.
- New audio is mono 16-bit WAV at 48 kHz in `Assets/FloorFracture/Resources/Endings/Audio/`. Import settings: Decompress On Load, PCM, 3D, matching `HoleDraft.wav.meta`.
- Every effect follows `GameSound.MasterVolume`, and `SetVolume(0)` silences it.
- Run EditMode tests from `Floor Fracture/` with the Editor closed: `unity test . --mode EditMode --filter <Name> --output "$TMPDIR/ff-tests.xml"`. If the Editor is open, run the same filter in Window → General → Test Runner instead. Run the full Play Mode suite (Tools → Floor Fracture → Verify gameplay in Play Mode) once only, in Task 9.
- Commit and push after each task, on branch `floor-fracture-game`.

## Review Focus

1. **Pause mid-hazard** (Menu button or headset off). Visuals freeze on the gameplay clock, a strike never plays twice after resume, and hum and thunder stop while paused. *Test: Task 7 `VerifyHazardPauseAndExit`.*
2. **Reduced motion on.** A strike is one soft glow with no flicker. *Test: Task 1 `ReducedMotionStrikeIsOneSoftGlow`.*
3. **Low ceiling or no ceiling anchor.** Lamps never go inside the real ceiling or below 2.0 m. *Test: Task 1 `LampHeightStaysUnderTheCeiling`.*
4. **Volume 0 or a volume change while a lamp hums.** The hum rescales or stops. *Test: Task 6 smoke assertion in `VerifyHazardVisuals`.*
5. **Leaving play mid-hazard** (Main menu, a fall, Results). Hazards fade out, hums stop, and nothing flashes during the fall. *Test: Task 7 `VerifyHazardPauseAndExit`.*

---

### Task 1: WaveHazards roll and timing rules

**Files:**
- Create: `Assets/FloorFracture/Scripts/Rules/WaveHazards.cs`
- Modify: `Assets/FloorFracture/Scripts/Rules/WavePlanner.cs:45,84-85` (replace the Fog formula) and `:572` (make `Generator` internal)
- Modify: `Assets/FloorFracture/Tests/Rules/BoardRuleChecks.cs:500-532` (`ModesAndReset` fog expectations)
- Modify: `Assets/FloorFracture/Editor/Verification/GameplaySmoke.cs:609-619,655,676` (`LevelThree` must no longer require fog on seed 379, level 3, wave 1)
- Test: `Assets/FloorFracture/Tests/Rules/WaveHazardsTests.cs`

**Interfaces:**
- Produces, in namespace `FloorFracture`:
  - `public sealed class WaveHazards` with:
    - `public static readonly WaveHazards None`
    - `public WaveHazards(float fogDensity, int fogPatches, int spotlights, IReadOnlyList<double> strikes, int visualSeed)` (public so smoke tests can force hazards)
    - `float FogDensity` (0 or 0.35–1), `int FogPatches` (0–3), `int Spotlights` (0–3), `IReadOnlyList<double> Strikes` (absolute gameplay times), `int VisualSeed`, `bool Any`
    - `public static WaveHazards Roll(int level, int number, int seed, double startsAt, double duration)`
    - `public static float FlashIntensity(double sinceStrike, bool reduced)` (returns 0–1)
    - `public static double ThunderDelay(int visualSeed, int strike)` (0.3–1.2 s)
    - `public static float Fade(double now, double shownAt, double hiddenAt)` (0.35 s fade-in, 0.6 s fade-out; `hiddenAt = double.PositiveInfinity` while shown)
    - `public static float LampHeight(float floorY, float? ceilingY)`
  - `WavePlan.Hazards` (`WaveHazards`, never null) and `WavePlan.Fog => Hazards.FogDensity > 0 || Hazards.FogPatches > 0`

- [ ] **Step 1: Write the failing tests** in `WaveHazardsTests.cs`:

```csharp
[Test] public void LevelOneHasNoHazards() {
    for (int s = 0; s < 500; s++) for (int n = 1; n < 8; n++)
        Assert.That(WaveHazards.Roll(1, n, s, 10, 2).Any, Is.False);
}
[Test] public void HazardsUnlockByLevel() {
    for (int s = 0; s < 500; s++) {
        var l2 = WaveHazards.Roll(2, 3, s, 10, 2); var l3 = WaveHazards.Roll(3, 3, s, 10, 2); var l4 = WaveHazards.Roll(4, 3, s, 10, 2);
        Assert.That(l2.FogPatches + l2.Spotlights + l2.Strikes.Count, Is.Zero);
        Assert.That(l3.Spotlights + l3.Strikes.Count, Is.Zero);
        Assert.That(l4.Strikes.Count, Is.Zero);
    }
}
[Test] public void HigherLevelsCombineHazards() {
    int combined = 0, fog = 0, patches = 0, lights = 0, strikes = 0;
    for (int s = 0; s < 1000; s++) {
        var h = WaveHazards.Roll(12, 2, s, 10, 1);
        int kinds = (h.FogDensity > 0 ? 1 : 0) + (h.FogPatches > 0 ? 1 : 0) + (h.Spotlights > 0 ? 1 : 0) + (h.Strikes.Count > 0 ? 1 : 0);
        if (kinds >= 2) combined++;
        if (h.FogDensity > 0) fog++; if (h.FogPatches > 0) patches++; if (h.Spotlights > 0) lights++; if (h.Strikes.Count > 0) strikes++;
    }
    Assert.That(combined, Is.EqualTo(1000));
    Assert.That(new[] { fog, patches, lights, strikes }, Has.All.GreaterThan(200));
}
[Test] public void RollIsDeterministic() =>
    Assert.That(WaveHazards.Roll(9, 4, 379, 3.5, 1.2).Strikes, Is.EqualTo(WaveHazards.Roll(9, 4, 379, 3.5, 1.2).Strikes));
[Test] public void StrikesStayInTheirWindowAndApart() {
    for (int s = 0; s < 500; s++) {
        var h = WaveHazards.Roll(10, 2, s, 20, 1);
        foreach (double t in h.Strikes) Assert.That(t, Is.InRange(20.25, 22.5));
        for (int i = 1; i < h.Strikes.Count; i++) Assert.That(h.Strikes[i] - h.Strikes[i - 1], Is.GreaterThanOrEqualTo(1.2));
    }
}
[Test] public void FlashesNeverExceedThreePerSecond() {
    // Count rising crossings of 0.5 in any 1 s window across a two-strike wave sampled at 1 ms.
}
[Test] public void ReducedMotionStrikeIsOneSoftGlow() {
    // Sample FlashIntensity(t, true) for t in [0, 1] at 1 ms: max <= 0.35, exactly one local maximum, 0 at t >= 0.6.
}
[Test] public void ThunderFollowsTheFlash() {
    for (int s = 0; s < 200; s++) for (int k = 0; k < 2; k++) Assert.That(WaveHazards.ThunderDelay(s, k), Is.InRange(0.3, 1.2));
}
[Test] public void FadeRisesThenFalls() {
    Assert.That(WaveHazards.Fade(0, 0, double.PositiveInfinity), Is.Zero);
    Assert.That(WaveHazards.Fade(.35, 0, double.PositiveInfinity), Is.EqualTo(1f));
    Assert.That(WaveHazards.Fade(5.3, 0, 5), Is.EqualTo(.5f).Within(.01f));
    Assert.That(WaveHazards.Fade(5.6, 0, 5), Is.Zero);
}
[Test] public void LampHeightStaysUnderTheCeiling() {
    Assert.That(WaveHazards.LampHeight(0, null), Is.EqualTo(2.4f).Within(1e-4f));
    Assert.That(WaveHazards.LampHeight(0, 2.5f), Is.EqualTo(2.35f).Within(1e-4f));
    Assert.That(WaveHazards.LampHeight(0, 3.5f), Is.EqualTo(2.6f).Within(1e-4f));
    Assert.That(WaveHazards.LampHeight(0, 2.1f), Is.EqualTo(2.0f).Within(1e-4f));
    Assert.That(WaveHazards.LampHeight(-1.2f, null), Is.EqualTo(1.2f).Within(1e-4f));
}
```

Also add to `BoardRulesEditorTests.cs`:
```csharp
[Test] public void EveryPlanCarriesHazardsAndFogMatchesThem() // walk the ProgressiveWaves fixture: plan.Hazards != null && plan.Fog == (plan.Hazards.FogDensity > 0 || plan.Hazards.FogPatches > 0); level 1 plans have !plan.Hazards.Any
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `unity test . --mode EditMode --filter "WaveHazardsTests|EveryPlanCarriesHazards"`
Expected: compile failure, because `WaveHazards` does not exist.

- [ ] **Step 3: Implement `WaveHazards`** with these values:
  - Count by level:
    - L1: 0
    - L2–3: `roll(2)` (0 or 1)
    - L4–6: `1 + (roll(10) < 3 ? 1 : 0)`
    - L7–9: `1 + roll(2)`
    - L10+: `2 + roll(2)`
  - Pick that many distinct kinds uniformly from the unlocked pool.
  - Intensity:
    - `FogDensity = 0.35f + 0.65f * Mathf.Clamp01((level - 2) / 8f)`
    - patches `1 + (level >= 7) + (level >= 10)`
    - spotlights `1 + (level >= 8) + (level >= 11)`
    - strikes `level >= 9 ? 2 : 1`
  - Strike times: uniform in `[startsAt + .25, startsAt + duration + 1.5]`, at least 1.2 s apart (redraw the second until it fits).
  - Full-motion flash: two pulses, 1.0 at 0–0.06 s and 0.7 at 0.16–0.22 s, then a decay to 0 by 0.5 s.
  - Reduced flash: one smooth bump, peak 0.35 at 0.1 s, 0 at 0.6 s.
  - `LampHeight`: `Mathf.Clamp((ceilingY ?? floorY + 2.55f) - .15f, floorY + 2.0f, floorY + 2.6f)`.
- In `WavePlanner`:
  - Make `Generator` internal.
  - Set `Hazards = WaveHazards.Roll(level, number, seed, startsAt, duration)` in the `WavePlan` constructor, using the plan's existing `Seed`.
  - Replace the field `Fog` with the expression-bodied property above.
- Update `ModesAndReset` to assert the new relation, not "fog on L2 wave 1".
- In `LevelThree`, replace the fog `Require` with `Require(fog.enabled == wave.Fog, ...)`. The renderer is renamed in Task 5, so look it up by whatever name Task 5 gives it; for now keep "Low fog".

- [ ] **Step 4: Run the tests to verify they pass**

Run: `unity test . --mode EditMode`
Expected: all pass, including `ExistingShapesKeepTheirSeededPatterns`.

- [ ] **Step 5: Commit and push.** Commit message: `Roll per-wave hazards from their own random stream`

---

### Task 2: More sliders

**Files:**
- Modify: `Assets/FloorFracture/Scripts/Rules/WavePlanner.cs:290-292` (gate and limit), `:476-521` (`ChooseSliders` Unavailable set)
- Modify: `Assets/FloorFracture/Scripts/Rules/BoardState.cs:164-173` (keep prior sliders alongside new offers) and `:161-162,180-194` (lock bitmask over the merged list)
- Modify: `Assets/FloorFracture/Tests/Rules/BoardRuleChecks.cs:569` (`ProgressiveWaves` caps become 1/2/3)
- Test: `Assets/FloorFracture/Tests/Rules/BoardRulesEditorTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `board.Sliders` may hold last wave's sliders (breaking this wave) plus this wave's offers. `WavePlan.OfferedSliders.Length` ≤ the cap. `SliderFor(tile)` is unchanged.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test] public void SlidersAppearOnMostEligibleAdvancedWaves() {
    // For levels 6..12, seeds 0..39, 6x6 board: follow plan.GuaranteedTile through each level as ProgressiveWaves does.
    // Count waves that are not LastTile, not IsJump, RowRoute.Count == 0. Assert offered.Length > 0 on at least 50% of them.
    // Print the measured fraction (TestContext.WriteLine) so the commit can record before and after.
}
[Test] public void OldSliderKeepsAPlayerWhileNewOnesAreOffered() {
    // Land on a slider in wave N (TryLand via State.Sample at its CenterAt), survive the deadline,
    // BeginWave N+1 from that slider: assert the sample is still Glass with IsSlider, the new plan may offer sliders,
    // and CanStepFrom(old.Tile, exit) is true for some intact exit.
}
[Test] public void PausingRestoresOldAndNewSliderLocks() {
    // As above, then State.Pause(): old slider stays locked, new offers are gone, IntactCount equals the value before BeginWave.
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `unity test . --mode EditMode --filter "SlidersAppearOnMost|OldSliderKeeps|PausingRestoresOldAndNew"`
Expected: `SlidersAppearOnMostEligibleAdvancedWaves` fails with a fraction below 0.5. The others fail because no new slider is offered while one exists.

- [ ] **Step 3: Implement**
  - In `WavePlanner`:
    - Drop `board.Sliders.Count == 0` from the slider gate only. Jumps and rows keep their gates.
    - The limit becomes `level >= 9 ? 3 : level >= 6 ? 2 : 1`.
    - Add every cell on an existing slider's path (`Tile` through `OtherHole`), plus the cell under its current `CenterAt(now)`, to `Unavailable`.
  - In `BoardState.BeginWave`: `sliders = waveStartSliders.Concat(plan.OfferedSliders).ToArray()`. Prior sliders are still breaking this wave (W:98) and leave at the deadline, as they do now at B:231.
  - Extend the lock bitmask to the merged length.
  - Update the `ProgressiveWaves` caps.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `unity test . --mode EditMode`
Expected: all pass. If `EverySeededSurvivalPath` or `LargerBoardSurvivalPaths` fail, the merged list broke a survival invariant. Fix the planner; do not weaken the check.

- [ ] **Step 5: Commit and push.** Commit message: `Offer new sliders while last wave's slider is still on the board` (include the measured fractions)

---

### Task 3: Patrol panes

**Files:**
- Modify: `Assets/FloorFracture/Scripts/Rules/PlayerTileTracker.cs:24-81` (`SlidingPane` path)
- Modify: `Assets/FloorFracture/Scripts/Rules/WavePlanner.cs:476-521` (patrol search from level 5)
- Modify: tests that read the straight lane: `Tests/Rules/BoardRulesEditorTests.cs:198-206`, `Tests/Rules/BoardRuleChecks.cs:72,762,774,835-837`, `Editor/Verification/GameplaySmoke.cs:692,739`
- Test: `Assets/FloorFracture/Tests/Rules/BoardRulesEditorTests.cs`

**Interfaces:**
- Produces:
  - `SlidingPane.Path` (`IReadOnlyList<int>`, adjacent cells; `Path[0] == Tile`, `Path[^1] == OtherHole`)
  - `internal SlidingPane(BoardState board, IReadOnlyList<int> path, double start, double duration)`. The old constructor delegates with the straight lane's cells.
  - `TravelCells == Path.Count - 1`
  - `Direction` is the first segment's direction.
  - `CenterAt(now)` ping-pongs along the polyline at today's pace of `1.20 * pace` seconds per cell, and stays a pure function of `now`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test] public void PatrolPaneFollowsItsCornersAtTheSliderPace() {
    // L-shaped path of 4 cells on a 4x4 fixture: at MotionStartsAt + k * (OneWaySeconds / 3) the center equals Center(Path[k]) within 1e-4,
    // and at k + .5 it lies on the segment between Path[k] and Path[k+1]; after OneWaySeconds it heads back.
}
[Test] public void PatrolPaneLocksWhereCaughtOnItsSecondSegment() {
    // TryLand at the point halfway along segment 2: Locked is true and CenterAt(later) equals the landing center.
}
[Test] public void EveryPlannedPatrolHasAnExitAlongItsWholePath() {
    // Levels 5..12, seeds 0..99, 6x6: for every offered pane with a turn, sample 20 points per segment;
    // a pane locked there must have some intact tile t with CanStepFrom(pane.Tile, t).
}
[Test] public void PatrolsTurnOnlyFromLevelFive() {
    // No offered pane has a turn below level 5. At least one has a turn across levels 5..12, seeds 0..99.
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `unity test . --mode EditMode --filter "Patrol"`
Expected: compile failure on `SlidingPane.Path`.

- [ ] **Step 3: Implement**
  - Store the path's cell centers and the cumulative length.
  - `TryLand`, `CanExitTo`, `Locked` and `RestoreLock` are unchanged.
  - Patrol search, from level 5: DFS over hole cells from the source pane.
    - The path is 3–5 cells, including the source.
    - It has at least one turn.
    - It never revisits a cell and avoids `Unavailable`.
    - For each segment, every cell on it has a survivor perpendicular to that segment. This is today's `HasSideExit`, applied per segment.
  - When both a straight lane and a patrol exist, take the patrol if `random.Next(2) == 0`. This draw comes after all existing draws.
  - Make the `Visit` state key (C:72) hash every slider's path.
  - Update the listed consumers to use `Path`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `unity test . --mode EditMode`
Expected: all pass.

- [ ] **Step 5: Commit and push.** Commit message: `Add patrol panes that turn corners and lock where caught`

---

### Task 4: Ride finale

**Files:**
- Modify: `Assets/FloorFracture/Scripts/Rules/WavePlanner.cs:286-292` (finale choice), `WavePlan` (new members)
- Modify: `Assets/FloorFracture/Tests/Rules/BoardRuleChecks.cs:263-273,290-292` (exempt ride finales from the stationary-guarantee checks)
- Modify: `Assets/FloorFracture/Scripts/Game/GameFlow.cs:216-221` (Level 6 Ready hint)
- Test: `Assets/FloorFracture/Tests/Rules/BoardRulesEditorTests.cs`

**Interfaces:**
- Produces:
  - `WavePlan.RideSlider` (`int`, an index into `OfferedSliders`, or -1)
  - `WavePlan.IsRide => RideSlider >= 0`
  - `WavePlan.RideCell` (`int`, the lane cell next to `StartingTile`)
  - A ride finale has `Mode == WaveMode.LastTile`, `StationarySafe.Count == 0` and `GuaranteedTile == OfferedSliders[RideSlider].Tile`.
  - The existing `BoardState.cs:245` rule already clears the level when a LastTile wave is survived. Standing on the locked pane at the deadline is survival.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test] public void RideFinaleAppearsOnlyFromLevelSix() // levels 3..12, seeds 0..199: IsRide never below 6; at least 10 ride finales across 6..12
[Test] public void RideFinaleBreaksEveryStationaryPane() // for each ride plan: StationarySafe empty and BreakingTiles contains every intact stationary tile
[Test] public void RidePaneStartsAwayAndPassesBesideThePlayer() {
    // pane.Tile is not a neighbor of StartingTile; RideCell is a neighbor; sampling CenterAt every 10 ms,
    // the time with max(|dx|,|dz|) <= .25 * Pitch from Center(RideCell) inside [StartsAt + .4, Deadline - .3] totals >= .35 s.
}
[Test] public void LandingOnTheRidePaneClearsTheLevelAndStayingPutLoses() {
    // Same ride plan twice (rebuild the board): (a) Tick at Deadline with the stance on StartingTile -> Lost;
    // (b) Sample at the pane's center during the window (it locks), Tick at Deadline there -> LevelCleared.
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `unity test . --mode EditMode --filter "Ride"`
Expected: compile failure on `IsRide`.

- [ ] **Step 3: Implement**
  - The ride finale is eligible when all of these hold:
    - `remaining == 1` (a LastTile finale)
    - `level >= 6`
    - `board.Sliders.Count == 0`
    - a lane exists that passes a hole next to the start and meets the window rule in the test above
  - The lane search for rides ignores side exits, because the level ends on landing. It allows straight lanes and, from level 5, patrol paths.
  - Draw `random.Next(2) == 0` after all existing draws. On a hit, drop the lone survivor from the survivor set, and make the pane the only offered slider and the guarantee.
  - Exempt `plan.IsRide` from C:272-273 and C:290-292.
  - Level 6 Ready text: append `" When no glass is left in reach, step onto the moving pane as it passes."` when `Level == 6 && WaveNumber == 1`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `unity test . --mode EditMode`
Expected: all pass.

- [ ] **Step 5: Commit and push.** Commit message: `End some levels with a ride onto a moving pane`

---

### Task 5: Fog rewrite

**Files:**
- Create: `Assets/FloorFracture/Scripts/Glass/FloorFog.cs`
- Rewrite: `Assets/FloorFracture/Resources/FloorFractureFog.shader`
- Modify: `Assets/FloorFracture/Scripts/Glass/GlassBoardView.cs:65,90-102,155,234` (remove the old fog plane; build `FloorFog`; add `SetHazards`)
- Modify: `Assets/FloorFracture/Scripts/Game/GameFlow.cs:21-34` (Stage setter) and `:271-276` (`StartWave`)
- Modify: `Assets/FloorFracture/Editor/Verification/GameplaySmoke.Floor.cs:125-129` (fog may rise to 0.35 m) and `GameplaySmoke.cs:609-619,655,676` (look up the new renderer name)
- Create: `Assets/FloorFracture/Editor/Verification/GameplaySmoke.Hazards.cs` (`VerifyHazardVisuals`, called from `Scenario` after `VerifyTileCracks`)

**Interfaces:**
- Consumes: `WaveHazards`, `WaveHazards.Fade`.
- Produces:
  - `public void GlassBoardView.SetHazards(WaveHazards hazards, double now)`. A new non-`None` value replaces the current one and fades in; `WaveHazards.None` fades the current one out.
  - `public bool GlassBoardView.ReducedMotion { get; set; }`
  - `internal sealed class FloorFog : MonoBehaviour` with `Build(BoardState board, Material material)`, `Show(WaveHazards hazards)`, `Tick(double now, float fade)`. Its child renderers are named "Floor fog 0" to "Floor fog 3".

- [ ] **Step 1: Write the failing smoke check** in `GameplaySmoke.Hazards.cs`:
  - Force `new WaveHazards(1f, 0, 0, Array.Empty<double>(), 7)` through `SetHazards` during a Countdown, then wait 0.5 s.
  - Use the render-and-read-back pattern from `GameplaySmoke.Floor.cs:135-158` to require that the mean luminance over the board rises by at least 8/255 against `WaveHazards.None`.
  - Force `new WaveHazards(0f, 1, 0, ...)` and require that the brightest pane is at least 12/255 brighter than the dimmest.
  - Capture "hazard-fog" with `SmokeView.CenterEye`.
  - Require every fog vertex to be at most 0.35 m above the mapped floor, and the fog renderers to be disabled 0.7 s after `SetHazards(WaveHazards.None, ...)`.

- [ ] **Step 2: Verify it fails to compile.** Run a recompile (`unity recompile` with the Editor open, or the EditMode run). Expected: `SetHazards` is not defined.

- [ ] **Step 3: Implement**
  - Geometry: four board-sized slices at 0.02, 0.10, 0.20 and 0.32 m, each 0.30 m wider than the board bounds on every side.
  - Noise: a 128² tileable R8 value-noise texture built once in `Build`, from `VisualSeed`.
  - Each slice's alpha is `density × noise × heightFalloff × fade` plus its patch terms, capped at 0.26. Four slices at 0.26 give at most 1 − 0.74⁴ ≈ 0.70 combined.
  - Patches: up to 3 drifting discs (`_Patches[3]`, as local x, z, radius 0.45 × Pitch, strength 0.9) on Lissajous paths over the board bounds with 9–14 s periods.
  - Colour (0.80, 0.84, 0.86).
  - Keep `GlassFloorOcclusion.hlsl` visibility, `Cull Off ZWrite Off`, `Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha`.
  - Set the render queue one above the crack "Fissures" material's queue (read it in `GlassTile`), so the fog dims the amber cracks instead of sitting under them.
  - `GameFlow`:
    - In `StartWave`, call `BoardView.SetHazards(plan.Hazards, GameplayTime)`.
    - In the Stage setter, when the new stage is not Countdown, BetweenWaves or Paused, call `BoardView?.SetHazards(WaveHazards.None, GameplayTime)`.
    - Set `BoardView.ReducedMotion` wherever `reduced` is read or toggled (GameFlow.cs:81-83, 196-198).

- [ ] **Step 4: Verify.** Enter Play Mode with the offline fixture, choose Development → Seed 379 · level 1 (or use `EditorChoose` to start level 7), and check that fog drifts below the knees and the patches cover panes. Run `unity test . --mode EditMode` and confirm everything is still green.

- [ ] **Step 5: Commit and push.** Commit message: `Replace the fog decal with drifting floor fog and fog patches`

---

### Task 6: Sweeping spotlights with spatial hum

**Files:**
- Create: `Assets/FloorFracture/Scripts/Glass/OverheadLights.cs`
- Create: `Assets/FloorFracture/Resources/FloorFractureBeam.shader` (beam cone and pool decal, two passes or two materials)
- Create: `Assets/FloorFracture/Resources/Endings/Audio/BeamHum.wav` and `.meta`
- Modify: `Assets/FloorFracture/Scripts/Presentation/Endings/GameSound.cs` (lamp voices)
- Modify: `Assets/FloorFracture/Scripts/Glass/GlassBoardView.cs` (build and tick `OverheadLights`; pass the lamp height in `Build`)
- Modify: `Assets/FloorFracture/Scripts/Game/GameFlow.cs:132` (compute the lamp height from `Room.CurrentRoom?.CeilingAnchors`)
- Modify: `Docs/Asset credits.md`
- Modify: `Editor/Verification/GameplaySmoke.Hazards.cs`

**Interfaces:**
- Consumes: `WaveHazards.Spotlights`, `WaveHazards.LampHeight`, `GlassBoardView.SetHazards`.
- Produces:
  - `GlassBoardView.Build(BoardState board, Pose pose, GameSound sound, float lampHeight)`. `lampHeight` is the world height from `WaveHazards.LampHeight(fit.FloorHeight, ceilingY)`, where `ceilingY` is the lowest `CeilingAnchors` y, or null.
  - `internal sealed class OverheadLights : MonoBehaviour` with `Build(BoardState board, float lampHeightLocal, GameSound sound)`, `Show(WaveHazards hazards)`, `Tick(double now, float fade)`
  - `public AudioSource GameSound.CreateLampVoice(Transform lamp)` (loops "BeamHum", spatial, starts at gain 0)
  - `public void GameSound.SetLampLevel(AudioSource voice, float level)` (final gain `0.18 × level × MasterVolume`; stops at 0)

- [ ] **Step 1: Get the clip (user action).**
  - Ask the user to download Freesound 553075, "Foley_Mechanism_Light_Buzz_Short_Loop_Mono_DR05" by Nox_Sound (CC0, a free Freesound account is needed), into the scratchpad.
  - Convert it: `afconvert -f WAVE -d LEI16@48000 -c 1 <download>.wav Assets/FloorFracture/Resources/Endings/Audio/BeamHum.wav`.
  - Copy the import settings from `HoleDraft.wav.meta` with a new guid. Loop is set on the AudioSource, not the clip.
  - Add a credits line: `BeamHum.wav — "Foley_Mechanism_Light_Buzz_Short_Loop_Mono_DR05" by Nox_Sound, Freesound 553075, CC0.`

- [ ] **Step 2: Write the failing smoke check** in `VerifyHazardVisuals`:
  - Force `new WaveHazards(0, 0, 2, Array.Empty<double>(), 11)` and wait 0.5 s.
  - Require 2 lamp objects at `LampHeight` (± 1 cm) above the board.
  - Require the brightest board pixel to rise by at least 25/255 against no hazards, and require the pool centers to move at least 0.3 m in 2 s.
  - Require each lamp's hum AudioSource to be playing, spatialized, with gain above 0.
  - After `sound.SetVolume(0)`, require it to have stopped. Restore the volume.
  - Capture "hazard-spotlights".

- [ ] **Step 3: Verify it fails to compile.** Expected: `CreateLampVoice` is not defined.

- [ ] **Step 4: Implement**
  - Lamps sit 0.8 m outside alternating board edges, at the lamp height.
  - Beam: an open cone with 24 segments from the lamp to a pool of radius 0.55 × Pitch. Straight alpha, 0.18 along the axis falling to 0 at the rim, with depth occlusion.
  - Pool: a decal at 0.004 m, Queue `Transparent+20` (above the fissures). Warm white (1, 0.96, 0.88), alpha up to 0.55 at the center with a smooth falloff.
  - Each pool center moves on `center + extents × (sin(ωt + φ), sin(1.3ωt + φ2))`. ω is chosen so one crossing of the board's longer side takes 3–5 s; φ comes from `VisualSeed`.

- [ ] **Step 5: Verify.** In Play Mode, beams sweep, pools wash out cracks, and the hum comes from each lamp's direction. Run `unity test . --mode EditMode`; it should still pass.

- [ ] **Step 6: Commit and push.** Commit message: `Add sweeping overhead spotlights with a spatial lamp hum`

---

### Task 7: Lightning with spatial thunder

**Files:**
- Create: `Assets/FloorFracture/Scripts/Glass/Lightning.cs`
- Create: `Assets/FloorFracture/Resources/Endings/Audio/Thunder.wav` and `.meta`
- Modify: `Assets/FloorFracture/Scripts/Presentation/Endings/GameSound.cs` (`PlayThunder`; `ResetSounds` stops thunder)
- Modify: `Assets/FloorFracture/Resources/FloorFractureSurface.shader`, `FloorFractureFog.shader`, `FloorFractureBeam.shader` (add the global `_FloorFractureFlash` as additive brightness)
- Modify: `Assets/FloorFracture/Scripts/Glass/GlassBoardView.cs` (build and tick `Lightning`)
- Modify: `Docs/Asset credits.md`, `Editor/Verification/GameplaySmoke.Hazards.cs`

**Interfaces:**
- Consumes: `WaveHazards.Strikes`, `FlashIntensity`, `ThunderDelay`, `GlassBoardView.ReducedMotion`.
- Produces:
  - `internal sealed class Lightning : MonoBehaviour` with `Build(Transform board, GameSound sound)`, `Show(WaveHazards hazards)`, `Tick(double now, float fade, bool reduced)`
  - `public void GameSound.PlayThunder(Vector3 worldPosition, double delaySeconds, float pitch)` (a pooled spatial voice; minDistance 6, maxDistance 40; `PlayScheduled` at `dspTime + delaySeconds`)

- [ ] **Step 1: Get the clip (user action).**
  - Ask the user to download Freesound 534023, "Thunderclap" by Fission9 (CC0).
  - Convert it with `afconvert -f WAVE -d LEI16@48000 -c 1`.
  - Trim it to 7.0 s with a 0.5 s linear fade-out, using a stdlib `wave` script in the scratchpad, into `Resources/Endings/Audio/Thunder.wav`.
  - Copy the meta from `HoleDraft.wav.meta` with a new guid.
  - Add the credits line.

- [ ] **Step 2: Write the failing smoke checks** in `GameplaySmoke.Hazards.cs`:
  - `VerifyLightning`:
    - Force `new WaveHazards(0, 0, 0, new[] { now + .3 }, 5)`.
    - Within 0.35 s, `Shader.GetGlobalFloat("_FloorFractureFlash")` must exceed 0.9.
    - It must be 0 by `now + .9`.
    - Exactly one thunder voice must be scheduled, positioned at least 8 m from the board center and at least 2 m above it.
    - With `ReducedMotion = true`, the peak must be at most 0.35.
  - `VerifyHazardPauseAndExit` (Review Focus 1 and 5):
    - Force a strike 0.4 s ahead, then pause with `MenuInput(touch)` before it fires and wait 1 s.
    - Require: no flash, no thunder playing, hum stopped.
    - Resume, and require exactly one strike in total.
    - Then force spotlights and choose "Main menu". Require all fog, beam and lightning renderers to be disabled within 0.7 s, every lamp voice to have stopped, and the flash to be 0.

- [ ] **Step 3: Verify it fails to compile.** Expected: `PlayThunder` is not defined.

- [ ] **Step 4: Implement**
  - Each strike fires once, tracked by the index of the next strike, which `Show` resets.
  - Set `Shader.SetGlobalFloat("_FloorFractureFlash", FlashIntensity(now - strike, reduced) × fade)`.
  - Sky flash: a soft disc 3 m across, 0.5 m above the lamp height, alpha `0.45 × intensity`. No full-screen white.
  - Strike direction: azimuth from `VisualSeed`, elevation 55°, 9 m from the board center.
  - Thunder pitch: 0.92–1.06 from the seed.
  - `ResetSounds` stops thunder voices. Pause is already routed through it at GameFlow.cs:397.

- [ ] **Step 5: Verify.** In Play Mode at level 9 or higher, check a double flash, then thunder from that side. With Reduced motion on, check one soft glow. Run `unity test . --mode EditMode`; it should still pass.

- [ ] **Step 6: Commit and push.** Commit message: `Add lightning flashes with spatial thunder`

---

### Task 8: Readme and design doc

**Files:**
- Modify: `README.md` (Play step 3: a sentence each on fog, spotlights, lightning, patrol panes and the ride finale, with their unlock levels)
- Modify: `FLOOR_FRACTURE.md` (the level progression section, if one lists per-level features)

- [ ] **Step 1: Update both files** with the unlock levels from Global Constraints.
- [ ] **Step 2: Commit and push.** Commit message: `Document the new level hazards`

---

### Task 9: Full verification on Mac and Quest

- [ ] **Step 1:** Run the full EditMode suite. Expected: all green.
- [ ] **Step 2:** Run the full Play Mode suite once (Tools → Floor Fracture → Verify gameplay in Play Mode; in batch, set `XR_RUNTIME_JSON` to a missing file for that process only). Expected: a report under `Builds/Verification/` with `"result": "passed"`.
- [ ] **Step 3:** Build the Quest APK with the README command, install it, and play levels 6–10 using the level picker (Development → Seed 379 in a development build).
  - Check against `Docs/Quest performance.md`. Frame rate must hold at 72 Hz with fog, 3 spotlights and lightning together. If it doesn't, lower the fog slice count to 3 before anything else.
  - Check that thunder comes from the flash's direction.
- [ ] **Step 4:** Commit any fixes from Step 3 and push.
