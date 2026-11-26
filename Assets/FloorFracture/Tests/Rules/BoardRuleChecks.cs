using System;
using System.Collections.Generic;
using UnityEngine;

namespace FloorFracture.Tests
{
    // Runs against the same public API as GameFlow, with an explicit clock and headset poses.
    public static class BoardRuleChecks
    {
        public static string RunAll()
        {
            CoordinatesAndSeams();
            PlantedStanceAndGapEdges();
            DeadlinesAndPause();
            ReportsResolvedLoss();
            ModesAndReset();
            SlidingAndGrace();
            CancelledSlidingWave();
            VariableSizeLayouts();
            ProgressiveWaves();
            TimedBreaksAndRollback();
            return CheckSequences() + " " + CheckLargerBoards();
        }

        public static string CheckSequences()
        {
            int plans = 0, choices = 0, finales = 0;
            foreach (int seed in new[] { 0, -17, 143, int.MinValue })
            foreach (int level in new[] { 1, 3 })
            for (int width = 2; width <= 4; ++width)
            for (int height = 2; height <= 4; ++height)
            {
                var visited = new HashSet<ulong>();
                var path = new List<Step>();
                for (int initial = 0; initial < width * height; ++initial)
                {
                    try { Visit(width, height, seed, level, initial, path, visited, ref plans, ref choices, ref finales); }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"Board {width}x{height}, seed {seed}, level {level}, start {initial}, route {string.Join(",", path)}", e);
                    }
                }
            }
            return $"Floor Fracture passed {plans} distinct wave states, {choices} survival choices and {finales} level clears.";
        }

        private readonly struct Step
        {
            public readonly int Tile;
            public readonly bool CatchFirst;
            public Step(int tile, bool catchFirst) { Tile = tile; CatchFirst = catchFirst; }
            public override string ToString() => CatchFirst ? Tile + " (caught slider)" : Tile.ToString();
        }

        private static void Visit(int width, int height, int seed, int level, int initial, List<Step> path,
            HashSet<ulong> visited, ref int plans, ref int choices, ref int finales)
        {
            var board = new BoardState(width, height, seed);
            int start = initial;
            for (int i = 0; i < path.Count; ++i)
            {
                WavePlan prior = board.BeginWave(start, level, i + 1, i * 10);
                if (Resolve(board, prior, path[i]) == WaveResult.LevelCleared) { ++finales; return; }
                start = path[i].Tile;
            }
            ulong oldMask = IntactMask(board);
            int oldSlider = board.Slider == null ? -1 : board.Slider.Tile;
            ulong state = (uint)oldMask | ((ulong)(uint)start << 16) |
                ((ulong)(uint)(oldSlider + 1) << 20) | ((ulong)(uint)path.Count << 25);
            if (board.Slider != null)
            {
                state |= (ulong)(uint)(board.Slider.OtherHole+1) << 30;
                if (board.Slider.Locked) state |= 1UL << 35;
            }
            if (!visited.Add(state)) return;
            ++plans;
            WavePlan plan = board.BeginWave(start, level, path.Count + 1, path.Count * 10);
            Check(path.Count < width * height, "Every sequence must terminate.");
            ValidatePlan(board, plan, start, oldMask);
            if (plan.Mode == WaveMode.LastTile)
            {
                Check(plan.SafeTiles.Count == 1 && plan.IsSafe(plan.GuaranteedTile), "Final pane must be stationary.");
                Resolve(board, plan, new Step(plan.GuaranteedTile, false));
                ++choices;
                ++finales;
                return;
            }

            bool optionalSlider = board.Slider != null && plan.IsSafe(board.Slider.Tile);
            if (optionalSlider)
            {
                int slider = board.Slider.Tile;
                bool onward = false;
                foreach (int tile in plan.StationarySafe) onward |= Adjacent(tile, slider, width);
                Check(!board.IsIntact(slider) && onward, "Moving glass requires an old hole and onward step.");
            }
            foreach (int tile in plan.SafeTiles)
            {
                // Catching a slider, then choosing a stationary survivor, also changes the next wave.
                int variants = optionalSlider && board.IsIntact(tile) ? 2 : 1;
                for (int variant = 0; variant < variants; ++variant)
                {
                    ++choices;
                    path.Add(new Step(tile, variant == 1));
                    Visit(width, height, seed, level, initial, path, visited, ref plans, ref choices, ref finales);
                    path.RemoveAt(path.Count - 1);
                }
            }
        }

        public static string CheckLargerBoards()
        {
            int starts = 0, plans = 0, choices = 0, visited63 = 0;
            foreach (int seed in new[] { 0, 379, -17, int.MinValue })
            foreach (int level in new[] { 1, 3 })
            for (int width = 2; width <= 32; ++width)
            for (int height = 2; height <= 64 / width; ++height)
            {
                if (width <= 4 && height <= 4) continue; // All branches are replayed by CheckSequences.
                for (int initial = 0; initial < width * height; ++initial)
                {
                    ++starts;
                    int start = initial;
                    var board = new BoardState(width, height, seed, 0.67f);
                    for (int number = 1; ; ++number)
                    {
                        Check(number < board.Count, "Large-board shrinking must terminate.");
                        ulong oldMask = IntactMask(board);
                        var wave = board.BeginWave(start, level, number, number * 10);
                        ValidatePlan(board, wave, start, oldMask);
                        ++plans;
                        choices += wave.SafeTiles.Count;
                        if (start == 63 || wave.IsSafe(63) || wave.IsBreaking(63)) ++visited63;
                        // Vary routes through the connected group; when offered, also test a
                        // transition from the optional moving platform over its old hole.
                        uint pick = unchecked((uint)(seed + initial * 17 + number * 13));
                        start = board.Slider != null && wave.IsSafe(board.Slider.Tile) && (pick & 1) != 0
                            ? board.Slider.Tile : wave.SafeTiles[(int)(pick % (uint)wave.SafeTiles.Count)];
                        if (Resolve(board, wave, new Step(start, false)) == WaveResult.LevelCleared) break;
                    }
                }
            }
            Check(visited63 > 0, "The high bit must participate in actual waves.");
            return $"Larger boards cleared {starts} seeded starting positions, tested {plans} waves " +
                   $"and checked onward reachability for {choices} advertised choices.";
        }

        public static void VariableSizeLayouts()
        {
            var occupied = new List<int>();
            for (int y = 0; y < 12; ++y)
                for (int x = 0; x < 12; ++x)
                    if (x <= y || x >= 9) occupied.Add(y * 12 + x);
            occupied.Remove(64); // Pane 0 is present while 64 is an interior cutout.
            occupied.Add(143);  // A duplicate must not affect the count.
            var sparse = new BoardLayout(12, 12, occupied);
            Check(sparse.IsPlayable && !sparse.IsRectangle && sparse.PaneCount == occupied.Count - 1 &&
                sparse.Contains(0) && !sparse.Contains(64) && sparse.Contains(128),
                "Occupancy must distinguish positions a word apart, including an interior cutout.");
            occupied.Reverse();
            var repeat = new BoardLayout(12, 12, occupied);
            Check(sparse.Equals(repeat) && sparse.GetHashCode() == repeat.GetHashCode(),
                "Refitting the same occupied cells must produce an equal board regardless of input order.");
            occupied.Remove(128);
            occupied.Add(80);
            Check(!sparse.Equals(new BoardLayout(12, 12, occupied)), "Shape equality must inspect high indices.");
            occupied.Clear();
            Check(sparse.Contains(143), "A constructed layout must be independent of its input collection.");
            Check(!new BoardLayout(130, 1, new[] {0, 1, 64, 65}).IsPlayable &&
                !new BoardLayout(65, 2, new[] {64, 65}).IsPlayable,
                "Disconnected islands and consecutive indices across a row boundary are unplayable.");
            Check(!new BoardLayout(8, 2, new[] {0, 1, 16}).IsPlayable,
                "Out-of-bounds source indices must invalidate a layout.");
            foreach (BoardLayout layout in new[] {sparse, BoardLayout.Rectangle(12, 12),
                BoardLayout.Rectangle(2, 70), BoardLayout.Rectangle(130, 1)})
            {
                Check(layout.IsPlayable, "The large layout must support a wave.");
                var board = new BoardState(layout, 379, .5f);
                int expectedEdges = 0, actualEdges = 0;
                for (int tile = 0; tile < board.Count; ++tile)
                {
                    Check(board.IndexAt(board.Center(tile)) == (layout.Contains(tile) ? tile : -1),
                        "A high-index pane or cutout failed the center/stance round trip.");
                    if (!layout.Contains(tile)) continue;
                    int x = tile % board.Width, y = tile / board.Width;
                    if (!layout.HasPane(x - 1, y)) ++expectedEdges;
                    if (!layout.HasPane(x + 1, y)) ++expectedEdges;
                    if (!layout.HasPane(x, y - 1)) ++expectedEdges;
                    if (!layout.HasPane(x, y + 1)) ++expectedEdges;
                }
                foreach (var edge in layout.Perimeter(board.Pitch)) ++actualEdges;
                Check(actualEdges == expectedEdges, "The perimeter must follow every occupied pane.");
                foreach (int level in new[] {1, 3})
                {
                    board.RebuildLevel();
                    int start = board.Count - 1;
                    int intact = layout.PaneCount;
                    for (int number = 1; ; ++number)
                    {
                        Check(number < layout.PaneCount, "A large board must finish shrinking.");
                        var wave = board.BeginWave(start, level, number, number * 10);
                        Check(wave.IsBreaking(start) && !wave.IsSafe(start) && board.IsIntact(wave.GuaranteedTile) &&
                            CanReachLanding(board, wave) && wave.StationarySafe.Count < intact,
                            "A large board must force a reachable step and strictly shrink.");
                        var marked = new HashSet<int>(wave.BreakingTiles);
                        var safe = new HashSet<int>(wave.SafeTiles);
                        var stationary = new HashSet<int>(wave.StationarySafe);
                        var seen = new HashSet<int>(wave.JumpLandings) {wave.GuaranteedTile};
                        var queue = new Queue<int>(seen);
                        while (queue.Count > 0)
                        {
                            int tile = queue.Dequeue();
                            foreach (int other in new[] { tile - 1, tile + 1, tile - board.Width, tile + board.Width })
                                if (board.Neighbors(tile, other) && stationary.Contains(other) && seen.Add(other))
                                    queue.Enqueue(other);
                        }
                        Check(seen.SetEquals(stationary), "High-index survivor groups must stay edge-connected.");
                        for (int tile = 0; tile < board.Count; ++tile)
                        {
                            bool exists = board.IsIntact(tile) || board.SliderFor(tile) != null;
                            Check(wave.IsBreaking(tile) == marked.Contains(tile) && wave.IsSafe(tile) == safe.Contains(tile) &&
                                exists == (wave.IsSafe(tile) ^ wave.IsBreaking(tile)),
                                "Every existing pane must appear in exactly one wave list, including above index 63.");
                        }
                        Check(!wave.IsBreaking(board.Count) && !wave.IsSafe(-1), "Out-of-bounds wave queries must be false.");
                        start = wave.GuaranteedTile;
                        intact = stationary.Count;
                        if (Resolve(board, wave, new Step(start, false)) == WaveResult.LevelCleared) break;
                    }
                }
            }

            // Exercise a real high-index offer; the first valid lane varies with the survivor curve.
            var sliderBoard = new BoardState(12, 12, 0);
            int from = 143, waveNumber = 1;
            WavePlan offer;
            while (true)
            {
                offer = sliderBoard.BeginWave(from, 3, waveNumber, waveNumber * 10);
                if (sliderBoard.Slider != null && offer.IsSafe(sliderBoard.Slider.Tile) &&
                    sliderBoard.Slider.Tile >= 64 && sliderBoard.Slider.OtherHole >= 64) break;
                Check(offer.Mode != WaveMode.LastTile, "Expected a high-index sliding lane on this seeded board.");
                from = offer.GuaranteedTile;
                Resolve(sliderBoard, offer, new Step(from, false));
                ++waveNumber;
            }
            var pane = sliderBoard.Slider;
            Check(pane != null && pane.Tile >= 64 && pane.OtherHole >= 64 && offer.IsSafe(pane.Tile),
                "The large-board fixture needs a sliding lane entirely above index 63.");
            double middle = pane.MotionStartsAt + pane.OneWaySeconds / 2;
            var caught = pane.CenterAt(middle);
            sliderBoard.Tick(caught, true, middle);
            Check(pane.Locked && sliderBoard.CurrentSample.IsSlider, "A high-index slider must catch over its lane.");
            Resolve(sliderBoard, offer, new Step(pane.Tile, false));
            var exit = sliderBoard.BeginWave(pane.Tile, 3, waveNumber + 1, (waveNumber + 1) * 10);
            Check(exit.IsBreaking(pane.Tile) && sliderBoard.CanStepFrom(pane.Tile, exit.GuaranteedTile) &&
                (sliderBoard.Center(exit.GuaranteedTile) - caught).sqrMagnitude <= sliderBoard.Pitch * sliderBoard.Pitch * 1.2501f,
                "A locked high-index slider must shatter with a reachable stationary exit.");
            Resolve(sliderBoard, exit, new Step(exit.GuaranteedTile, false));
        }

        private static bool CanReachLanding(BoardState board, WavePlan wave) =>
            wave.IsJump ? board.Neighbors(wave.StartingTile, wave.JumpGap) &&
                board.Neighbors(wave.JumpGap, wave.GuaranteedTile) &&
                wave.GuaranteedTile - wave.JumpGap == wave.JumpGap - wave.StartingTile :
                (wave.RowRoute.Count > 0 ? wave.RowRoute[wave.RowRoute.Count-1] == wave.GuaranteedTile :
                board.CanStepFrom(wave.StartingTile, wave.GuaranteedTile));

        private static void ValidatePlan(BoardState board, WavePlan plan, int start, ulong oldMask)
        {
            Check(plan.StartingTile == start && plan.IsBreaking(start) && !plan.IsSafe(start), "Start must break.");
            Check(CanReachLanding(board, plan) && board.IsIntact(plan.GuaranteedTile) &&
                Has(plan.StationarySafe, plan.GuaranteedTile), "The mandatory step must reach permanent glass from the actual support.");
            ulong survivors = 0;
            foreach (int tile in plan.StationarySafe) survivors |= 1UL << tile;
            Check(survivors != oldMask && (survivors & ~oldMask) == 0, "Permanent glass must strictly shrink.");
            ulong reachable = 1UL << plan.GuaranteedTile, priorReach;
            foreach (int landing in plan.JumpLandings) reachable |= 1UL << landing;
            do
            {
                priorReach = reachable;
                foreach (int tile in plan.StationarySafe)
                    if ((NeighborBits(tile, board.Width, board.Height) & reachable) != 0) reachable |= 1UL << tile;
            } while (priorReach != reachable);
            Check(reachable == survivors, "Each group of survivors must be reachable from a landing.");
            for (int i = 0; i < board.Count; ++i)
            {
                bool surface = board.IsIntact(i) || board.SliderFor(i) != null;
                Check(surface == (plan.IsBreaking(i) ^ plan.IsSafe(i)), "Plan must partition all supporting panes.");
                if (plan.IsSafe(i) && plan.Mode != WaveMode.LastTile && !Has(plan.JumpLandings,i))
                    Check((NeighborBits(i, board.Width, board.Height) & survivors) != 0,
                        "Every choice must have a stationary orthogonal neighbor for the next wave.");
            }
            Check(!plan.IsBreaking(-1) && !plan.IsSafe(-1) && !plan.IsBreaking(board.Count) && !plan.IsSafe(board.Count),
                "Indices outside the footprint must not alias valid tile indices.");
            Check((plan.Mode == WaveMode.LastTile) == (plan.StationarySafe.Count == 1), "Finale must leave one permanent pane.");
        }

        private static ulong NeighborBits(int tile, int width, int height)
        {
            ulong bits = 0;
            if (tile % width > 0) bits |= 1UL << (tile - 1);
            if (tile % width < width - 1) bits |= 1UL << (tile + 1);
            if (tile / width > 0) bits |= 1UL << (tile - width);
            if (tile / width < height - 1) bits |= 1UL << (tile + width);
            return bits;
        }

        private static WaveResult Resolve(BoardState board, WavePlan plan, Step step)
        {
            if (step.CatchFirst)
            {
                double landing = plan.StartsAt + 0.1;
                Check(board.Tick(board.Slider.CenterAt(landing), true, landing) == WaveResult.Pending &&
                    board.Slider.Locked, "Early arrival should lock the optional pane.");
            }
            if (!board.IsIntact(step.Tile) && plan.IsJump && !step.CatchFirst && board.AppliedBreakGroupCount == 0)
                board.Tick(board.Center(plan.StartingTile),true,plan.JumpOpensAt);
            Vector2 position = board.IsIntact(step.Tile) ? board.Center(step.Tile) : board.SliderFor(step.Tile).CenterAt(plan.Deadline);
            int score = board.WavesSurvived;
            bool isolated = plan.IsJump && board.IsIntact(step.Tile);
            foreach (int tile in plan.StationarySafe) if (board.Neighbors(step.Tile,tile)) isolated = false;
            WaveResult expected = plan.Mode == WaveMode.LastTile || isolated ? WaveResult.LevelCleared : WaveResult.Survived;
            var result = board.Tick(position, true, plan.Deadline);
            Check(result == expected && board.CurrentSample.Index == step.Tile,
                $"On {board.Width}x{board.Height}, level {plan.Level} wave {plan.Number}, seed {board.Seed}, start {plan.StartingTile}, landing {step.Tile}, got {result}/{board.CurrentSample.Index}, expected {expected}.");
            Check(board.BreaksApplied && board.WavesSurvived == score + 1 && board.ActiveWave == null,
                "Resolve must commit and score exactly once.");
            Check(board.Tick(position, true, plan.Deadline) == WaveResult.Pending && board.WavesSurvived == score + 1,
                "A repeated frame must not award another point.");
            foreach (int tile in plan.StationarySafe) Check(board.IsIntact(tile), "Lost a permanent survivor.");
            foreach (int tile in plan.BreakingTiles) Check(!board.IsIntact(tile), "A scheduled tile failed to break.");
            return expected;
        }

        public static void CoordinatesAndSeams()
        {
            foreach (float pitch in new[] {0.5f, 0.57f, 0.6367f, 0.67f})
            for (int w = 2; w <= 32; ++w)
            for (int h = 2; h <= 64 / w; ++h)
            {
                var board = new BoardState(w, h, 0, pitch);
                float left = -w * pitch * 0.5f, bottom = -h * pitch * 0.5f;
                Check(board.IndexAt(new Vector2(left, bottom)) == 0, "Near outer edges are inclusive.");
                Check(board.IndexAt(new Vector2(Next(left, false), bottom)) == -1 &&
                    board.IndexAt(new Vector2(-left, bottom)) == -1 && board.IndexAt(new Vector2(left, -bottom)) == -1,
                    "Outside the footprint must not receive tolerance.");
                for (int i = 0; i < board.Count; ++i)
                {
                    Vector2 p = board.Center(i);
                    Check(board.IndexAt(p) == i, "Center/index round trip failed.");
                    if (i % w + 1 < w)
                    {
                        float edge = (i % w + 1 - w * 0.5f) * pitch;
                        Check(board.IndexAt(new Vector2(Next(edge, false), p.y)) == i &&
                            board.IndexAt(new Vector2(edge, p.y)) == i + 1 &&
                            board.IndexAt(new Vector2(Next(edge, true), p.y)) == i + 1, "X seam lost one float of precision.");
                    }
                    if (i / w + 1 < h)
                    {
                        float edge = (i / w + 1 - h * 0.5f) * pitch;
                        Check(board.IndexAt(new Vector2(p.x, Next(edge, false))) == i &&
                            board.IndexAt(new Vector2(p.x, edge)) == i + w, "Z seam lost one float of precision.");
                    }
                }
            }
            var rotated = new Matrix4x4 { m02 = -1, m03 = -4, m11 = 1, m20 = 1, m23 = -10, m33 = 1 };
            Vector2 floor = PlayerTileTracker.Project(rotated, new Vector3(10.25f, 0, -4.5f));
            Check(floor.x == 0.5f && floor.y == 0.25f &&
                PlayerTileTracker.Project(rotated, new Vector3(10.25f, 2, -4.5f)).Equals(floor),
                "Walking and hopping must use the same rotated local footprint.");
            var tilted = new Matrix4x4 { m00 = 0.8f, m01 = 0.6f, m10 = -0.6f, m11 = 0.8f, m22 = 1, m33 = 1 };
            var ground = PlayerTileTracker.Project(tilted, new Vector3(1, 0, 1));
            var head = PlayerTileTracker.Project(tilted, new Vector3(1, 1.8f, 1));
            Check((ground - head).sqrMagnitude < 1e-10f, "Projection onto a tilted floor changed with head height.");

            var b = new BoardState(2, 2, 9);
            var wave = b.BeginWave(0, 1, 1, 0);
            b.Tick(b.Center(2), true, .5);
            Vector2 across = new Vector2(0.003f, 0.25f);
            b.Tick(across, true, 1);
            Check(b.CurrentSample.Index == 2, "Equivalent panes should stabilize the marker near the seam.");
            b.Tick(new Vector2(0.05f, 0.25f), true, 1.5);
            Check(b.CurrentSample.Index == 3, "Marker should follow a real step.");
            b.Tick(b.Center(0), true, wave.Deadline - .45);
            b.Tick(new Vector2(0.001f, -0.25f), true, wave.Deadline - .4);
            Check(b.CurrentSample.Index == 0, "A seam must not move a planted stance just because the wave marks a neighbour safe.");
            b.Tick(b.Center(2), true, wave.Deadline - .1);
            Check(b.Tick(across, true, wave.Deadline) == WaveResult.Survived && b.CurrentSample.Index == 2,
                "The marker must retain the same support through zero.");
            var second = b.BeginWave(b.CurrentSample.Index, 1, 2, 6);
            Check(b.Tick(across, true, second.Deadline) == WaveResult.Lost, "Standing still at a seam cannot win the next wave.");
        }

        public static void DeadlinesAndPause()
        {
            var board = new BoardState(4, 4, 3);
            var first = board.BeginWave(0, 1, 1, 10);
            Check(Close(first.Duration, 2.00) && first.RevealEndsAt == 10.8 && first.ShowsWarnings(10.799) &&
                !first.ShowsWarnings(10.8), "Opening danger must be visible for the first 0.8 seconds.");
            ulong full = IntactMask(board);
            Vector2 safe = board.Center(first.GuaranteedTile);
            Check(board.Tick(safe, true, first.Deadline - .0001) == WaveResult.Pending && IntactMask(board) == full &&
                ReferenceEquals(first, board.ActiveWave), "Early safety must not advance or reroll the wave.");
            Check(board.Tick(board.Center(0), true, first.Deadline) == WaveResult.Lost && board.BreaksApplied &&
                board.WavesSurvived == 0 && board.LastLoss == WaveLossReason.CrackedPane, "Remaining on the start at zero must lose to its marked glass.");
            board.Reset(4, 4, 3);
            Check(board.LastLoss == WaveLossReason.None,"A previous loss explanation survived Restart.");
            first = board.BeginWave(0, 1, 1, 10);
            board.Tick(board.Center(0), true, first.Deadline - .01);
            Check(board.Tick(safe, true, first.Deadline + .99) == WaveResult.Lost, "A late frame cannot grant an extra step after zero.");
            board.Reset(4, 4, 3);
            first = board.BeginWave(0, 1, 1, 10);
            board.Tick(safe, true, first.Deadline - .01);
            Check(board.Tick(board.Center(0), true, first.Deadline + .99) == WaveResult.Survived, "A step after zero cannot revoke survival.");

            foreach (int reason in new[] { 0, 1, 2, 3 })
            {
                board.Reset(4, 4, 3);
                first = board.BeginWave(0, 1, 1, 10);
                if (reason == 3) board.Pause();
                else
                {
                    Vector2 interrupted = reason == 0 ? new Vector2(2, 0) :
                        reason == 1 ? safe : new Vector2(float.NaN, 0);
                    Check(board.Tick(interrupted, reason != 1, first.Deadline) == WaveResult.Paused, "Interruption must pause even at zero.");
                }
                Check(!board.BreaksApplied && board.ActiveWave == null && IntactMask(board) == full &&
                    board.WavesSurvived == 0, "Pausing must preserve all previously solid glass and score.");
                var from = board.Sample(board.Center(4), true, 40);
                var fresh = board.BeginWave(from.Index, 1, 1, 40);
                Check(Close(fresh.Deadline, 42.00) && fresh.IsBreaking(4) && Adjacent(4, fresh.GuaranteedTile, 4),
                    "Resume needs a full timer and a legal move from the current pose.");
            }
        }

        public static void PlantedStanceAndGapEdges()
        {
            // Use the 5 x 3 / 53.9 cm physical footprint, then both supported pitch limits.
            foreach (float pitch in new[]{.539f,.50f,.67f})
            {
                foreach (Vector2 side in new[]{Vector2.left,Vector2.right,Vector2.up,Vector2.down,
                    new Vector2(-1,-1),new Vector2(-1,1),new Vector2(1,-1),new Vector2(1,1)})
                {
                    var board=EdgeFixture(pitch,side,out int safe,out int hole);
                    var wave=board.ActiveWave;
                    Vector2 edge=board.Center(safe)+side*(pitch*.5f+.04f);
                    Check(board.IndexAt(edge)==hole && !board.IsIntact(hole),"The simulated lean must project inside an old hole.");
                    board.Tick(board.Center(safe),true,11);
                    board.Tick(edge,true,11.4);
                    Check(board.CurrentSample.Kind==SupportKind.Glass && board.CurrentSample.Index==safe &&
                        board.CurrentSample.Stance==edge,
                        "A planted stance near an inside edge or corner lost its visible support.");
                    Check(board.Tick(edge,true,wave.Deadline)==WaveResult.Survived && board.CurrentSample.Index==safe,
                        "The pane carrying the marker before zero must carry the player through zero.");

                    var missed=EdgeFixture(pitch,side,out safe,out hole);
                    var opening=missed.Center(hole);
                    missed.Tick(missed.Center(safe),true,11);
                    missed.Tick(opening,true,11.4);
                    Check(missed.CurrentSample.Kind==SupportKind.Gap && missed.Tick(opening,true,11.701)==WaveResult.Lost,
                        "An unsupported hole center must still cause a loss.");
                }
                var stepped=EdgeFixture(pitch,Vector2.right,out int glass,out int gap);
                var center=stepped.Center(glass);
                stepped.Tick(stepped.Center(gap),true,11);
                stepped.Tick(stepped.Center(gap),true,11.20);
                Check(stepped.Tick(center,true,11.32)==WaveResult.Pending && stepped.CurrentSample.Index==glass,
                    "A frame after grace expiry must preserve a step that crossed to support before expiry.");
            }
            var neck=new Vector3(.30f,1.50f,-.24f);
            foreach (double degrees in new[]{0d,25d,55d,85d})
            {
                double angle=degrees*Math.PI/360d;
                var rot=new Quaternion((float)Math.Sin(angle),0,0,(float)Math.Cos(angle));
                var eye=neck+rot*new Vector3(0,.075f,.08f);
                var standing=PlayerTileTracker.ProjectStance(Matrix4x4.identity,eye,rot,.08f);
                Check((standing-new Vector2(neck.x,neck.z)).sqrMagnitude<1e-10f,"Looking down around a fixed neck moved the stance.");
            }
        }

        private static BoardState EdgeFixture(float pitch,Vector2 side,out int safe,out int hole)
        {
            for(int start=0;start<15;start++)
            {
                var board=new BoardState(5,3,379,pitch);
                var first=board.BeginWave(start,1,1,0);
                Resolve(board,first,new Step(first.GuaranteedTile,false));
                var next=board.BeginWave(first.GuaranteedTile,1,2,10);
                foreach(int tile in next.StationarySafe)
                {
                    int other=board.IndexAt(board.Center(tile)+side*pitch);
                    if(other<0 || board.IsIntact(other)) continue;
                    safe=tile; hole=other; return board;
                }
            }
            throw new InvalidOperationException("Missing deterministic inside-edge fixture for "+side);
        }

        public static void ModesAndReset()
        {
            var board = new BoardState(4, 4, 143);
            var other = new BoardState(4, 4, -146);
            var baseline = other.BeginWave(0, 1, 1, 27);
            var modes = new List<WaveMode>();
            for (int level = 1; level <= 4; ++level)
            {
                int start = 0;
                for (int number = 1; ; ++number)
                {
                    var plan = board.BeginWave(start, level, number, number * 10);
                    if (level == 1 && number == 1)
                        Check(Same(plan.BreakingTiles, baseline.BreakingTiles) && plan.Duration == baseline.Duration &&
                            plan.Seed == baseline.Seed, "Opening pattern must be independent of run seed and wall clock.");
                    if (level == 1) Check(!plan.Fog && plan.Mode != WaveMode.Memory && board.Slider == null,
                        "The first level must introduce only stationary glass and the finale.");
                    Check(plan.Fog == (plan.Hazards.FogDensity > 0 || plan.Hazards.FogPatches > 0), "Fog must follow the rolled hazards.");
                    if (level < 3) Check(board.Slider == null, "Moving glass was introduced before level three.");
                    Check(plan.ShowsWarnings(plan.StartsAt) && plan.ShowsWarnings(plan.RevealEndsAt - .0001) &&
                        !plan.ShowsWarnings(plan.StartsAt - .0001) && !plan.ShowsWarnings(plan.RevealEndsAt) &&
                        !plan.ShowsWarnings(plan.Deadline), "Damage cues must end at the reveal boundary on every wave.");
                    start = plan.GuaranteedTile;
                    if (Resolve(board,plan,new Step(start,false)) == WaveResult.LevelCleared) break;
                    if (level >= 2 && !plan.IsJump) modes.Add(plan.Mode);
                }
                int score = board.WavesSurvived;
                board.RebuildLevel();
                Check(IntactMask(board) == 65535 && board.Slider == null && board.WavesSurvived == score,
                    "Clearing must restore the original footprint and retain run score.");
            }
            Check(modes[0] == WaveMode.Classic && modes[1] == WaveMode.Memory, "Base introduction failed.");
            for (int i = 0; i + 2 < modes.Count; i += 3)
                Check(modes.GetRange(i, 3).FindAll(mode => mode == WaveMode.Memory).Count == 1,
                    "Each group of three regular advanced waves must offer one Memory round.");
            board.Reset(2, 3, -55);
            Check(board.WavesSurvived == 0 && board.Slider == null && board.ActiveWave == null &&
                board.CurrentSample.Kind == SupportKind.Untracked && IntactMask(board) == 63, "Restart must clear the run.");
        }

        public static void PlansCarryHazards()
        {
            foreach (int seed in new[] {0, 379, -17})
            for (int level = 1; level <= 16; ++level)
            {
                var board = new BoardState(BoardLayout.Rectangle(4, 4), seed, .5f);
                int start = 0;
                for (int number = 1; number <= 16; ++number)
                {
                    var plan = board.BeginWave(start, level, number, number * 10);
                    Check(plan.Hazards != null && plan.Fog == (plan.Hazards.FogDensity > 0 || plan.Hazards.FogPatches > 0),
                        "Fog must follow the rolled hazards.");
                    if (level == 1) Check(!plan.Hazards.Any, "Level one must have no hazards.");
                    start = plan.GuaranteedTile;
                    if (Resolve(board, plan, new Step(start, false)) == WaveResult.LevelCleared) break;
                }
            }
        }

        public static void ProgressiveWaves()
        {
            double[] openings = {2, 1.75, 1.5, 1.32, 1.16, 1};
            double[] clues = {.8, .675, .55, .475, .4, .35, .30, .29, .28, .27, .26, .25};
            double[] fractions = {.70, .65, .60, .56, .52, .485, .45, .425, .40, .4-.05/3, .4-.10/3, .35};
            double[] beats = {1.25, 1.10, 1.00};
            var irregular = new BoardLayout(5, 4, new[] {0,1,2,5,7,8,9,10,11,12,14,17,18,19});
            foreach (var layout in new[] {BoardLayout.Rectangle(2,1), BoardLayout.Rectangle(4,4),
                BoardLayout.Rectangle(12,12), irregular})
            foreach (int seed in new[] {0, 379, -17})
            for (int level = 1; level <= 16; ++level)
            {
                var board = new BoardState(layout, seed, .5f);
                int column = Math.Min(level, 12) - 1;
                int start = 0, intact = layout.PaneCount, jumpCount = 0;
                for (int number = 1; ; ++number)
                {
                    Check(number <= layout.PaneCount, "A level must finish.");
                    var wave = board.BeginWave(start, level, number, number * 10);
                    int rows = wave.RowRoute.Count;
                    double ordinary = Math.Max(openings[Math.Min(level,5)],
                        openings[Math.Min(level,6)-1] - .08 * Math.Min(number-1,3));
                    double duration = wave.IsJump ? wave.JumpOpensAt-wave.StartsAt+Math.Max(1.85,ordinary) :
                        rows == 0 ? ordinary : rows == 1 ? 1.25 : rows == 2 ? 2.35 : 3.35;
                    Check(Close(wave.Duration,duration) && Close(wave.RecoverySeconds,1.28-.68*column/11),
                        "Movement and recovery must follow the level curve.");
                    jumpCount = wave.IsJump ? jumpCount + 1 : 0;
                    Check(jumpCount <= (level >= 9 ? 2 : 1), "Too many consecutive jumps.");
                    int offered = 0;
                    foreach (var pane in board.Sliders) if (wave.IsSafe(pane.Tile)) ++offered;
                    Check(offered <= (level < 3 ? 0 : level < 6 ? 1 : level < 9 ? 2 : 3), "Too many sliding panes.");
                    if (!wave.IsJump && rows == 0 && board.Sliders.Count == 0)
                    {
                        int target = Math.Max(1,(int)Math.Floor(intact * fractions[column] + .500000001));
                        Check(wave.StationarySafe.Count == Math.Min(target, Math.Min(ConnectedCapacity(board,wave),intact-1)),
                            "Ordinary waves must shrink at the configured rate.");
                    }
                    if (level >= 6 && !wave.IsJump && rows == 0)
                        Check(wave.BreakGroups.Count == 1,"All marked glass must fall together on a simultaneous wave.");
                    if (rows != 0)
                        Check(level >= 6 && rows == wave.BreakGroups.Count && rows <= (level >= 9 ? 3 : 2),
                            "Row waves must fit the level and the floor.");
                    var marked = new HashSet<int>();
                    int standing = start;
                    double cue = wave.StartsAt;
                    for (int i = 0; i < wave.BreakGroups.Count; ++i)
                    {
                        var group = wave.BreakGroups[i];
                        Check(group.Tiles.Count > 0 && group.BreaksAt-group.RevealStartsAt >= 1-.00000001 &&
                            Close(group.RevealEndsAt-group.RevealStartsAt,clues[column]), "Every marked pane needs a full second from its first cue.");
                        foreach (int tile in group.Tiles)
                            Check(marked.Add(tile) && wave.BreakGroupFor(tile) == group &&
                                !wave.ShowsWarning(tile,group.RevealStartsAt-.001) && wave.ShowsWarning(tile,group.RevealStartsAt) &&
                                wave.ShowsWarning(tile,group.RevealEndsAt-.001) && !wave.ShowsWarning(tile,group.RevealEndsAt),
                                "Tile warnings must match the visible cue interval.");
                        if (rows != 0)
                        {
                            Check(wave.BreakGroupFor(standing) == group && Close(group.RevealStartsAt,cue) &&
                                Close(group.BreaksAt-cue,beats[i]) && board.Neighbors(standing,wave.RowRoute[i]),
                                "Each consecutive step must receive its complete interval after the previous row.");
                            standing = wave.RowRoute[i];
                            cue = group.BreaksAt;
                        }
                        if (group.BreaksAt >= wave.Deadline) continue;
                        Check(board.Tick(board.Center(standing),true,group.BreaksAt) == WaveResult.Pending &&
                            board.IsIntact(standing),"A timely step must escape the early collapse.");
                        foreach (int tile in group.Tiles) Check(!board.IsIntact(tile),"Due glass stayed intact.");
                    }
                    Check(marked.SetEquals(wave.BreakingTiles) && wave.BreakGroupFor(wave.GuaranteedTile) == null &&
                        CanReachLanding(board,wave),"The planned escape must be reachable and safe.");
                    start = wave.GuaranteedTile;
                    intact = wave.StationarySafe.Count;
                    if (Resolve(board,wave,new Step(start,false)) == WaveResult.LevelCleared) break;
                }
            }
            MemoryOrder();
        }

        private static int ConnectedCapacity(BoardState board, WavePlan wave)
        {
            var reached = new HashSet<int> {wave.GuaranteedTile};
            var frontier = new Queue<int>(reached);
            while (frontier.Count > 0)
            {
                int tile = frontier.Dequeue();
                foreach (int next in new[] {tile - 1, tile + 1, tile - board.Width, tile + board.Width})
                    if (next != wave.StartingTile && board.Neighbors(tile, next) && board.IsIntact(next) &&
                        !(wave.IsJump && Math.Abs(next % board.Width - wave.StartingTile % board.Width) <= 1 &&
                            Math.Abs(next / board.Width - wave.StartingTile / board.Width) <= 1) && reached.Add(next))
                        frontier.Enqueue(next);
            }
            return reached.Count;
        }

        private static BoardState StagedBoard()
        {
            var tiles = new List<int>();
            for (int tile = 0; tile < 81; tile++)
                if (tile != 2 && tile != 18) tiles.Add(tile);
            return new BoardState(new BoardLayout(9, 9, tiles), 379, .6f);
        }

        private static void MemoryOrder()
        {
            // Both boards have the same seed, shape and wave number. Only one has already
            // survived a regular advanced wave and therefore receives the Memory sequence.
            foreach (int level in new[] {2, 3, 5})
            {
                var memory = MemoryFixture(level);
                var replay = MemoryFixture(level);
                var classic = StagedBoard().BeginWave(0, level, 1, 0);
                Check(memory.Mode == WaveMode.Memory && classic.Mode == WaveMode.Classic &&
                    Same(memory.BreakingTiles, classic.BreakingTiles) && memory.BreakGroups.Count >= 2,
                    "Memory must present successive groups over the same underlying pattern.");
                Check(!Same(memory.BreakGroups[0].Tiles, classic.BreakGroups[0].Tiles),
                    "Memory must change the sequence the player memorizes.");
                for (int i = 0; i < memory.BreakGroups.Count; ++i)
                    Check(Same(memory.BreakGroups[i].Tiles, replay.BreakGroups[i].Tiles),
                        "A seeded Memory sequence must replay exactly.");
            }
        }

        private static WavePlan MemoryFixture(int level)
        {
            var board = StagedBoard();
            var prior = board.BeginWave(0, 2, 1, 0);
            Resolve(board, prior, new Step(prior.GuaranteedTile, false));
            board.RebuildLevel();
            return board.BeginWave(0, level, 1, 0);
        }

        public static void TimedBreaksAndRollback()
        {
            var board = new BoardState(4,4,379);
            var wave = board.BeginWave(0,9,1,10);
            var first = wave.BreakGroups[0];
            var point = board.Center(0);
            board.Tick(point,true,first.BreaksAt-.01);
            Check(board.Tick(point,true,wave.Deadline+.5) == WaveResult.Lost &&
                board.LastLoss == WaveLossReason.GapGraceExpired && board.AppliedBreakGroupCount == 1 &&
                !board.BreaksApplied,"A late frame must resolve the missed first row before later beats.");
            foreach (bool onTime in new[] {true,false})
            {
                board.RebuildLevel();
                wave = board.BeginWave(0,9,1,10);
                first = wave.BreakGroups[0];
                board.Tick(point,true,first.BreaksAt-.01);
                board.Tick(point,true,first.BreaksAt+(onTime ? .19 : .2995));
                Check(board.Tick(board.Center(wave.RowRoute[0]),true,first.BreaksAt+.32) ==
                    (onTime ? WaveResult.Pending : WaveResult.Lost),"Recovery depends on the stance at grace expiry.");
            }
            board.RebuildLevel();
            wave=board.BeginWave(0,9,1,10);
            point=board.Center(wave.RowRoute[1]);
            double afterBoth=wave.BreakGroups[1].BreaksAt+.01;
            for (int frame=0; frame<2; ++frame)
                Check(board.Tick(point,true,afterBoth)==WaveResult.Pending && board.AppliedBreakGroupCount==2,
                    "A skipped frame must commit exactly the due rows and a frozen clock must preserve them.");
            Check(board.Tick(point,false,afterBoth+.1)==WaveResult.Paused && board.AppliedBreakGroupCount==0 &&
                board.WavesSurvived==0,"Tracking loss must cancel the unfinished sequence.");
            for(int tile=0;tile<board.Count;++tile) Check(board.IsIntact(tile),"Canceled row glass must return.");
            var retry=board.BeginWave(0,9,1,30);
            Check(Close(retry.Deadline,33.35) && Same(retry.RowRoute,wave.RowRoute),
                "A retry must restore the same rows and all three full intervals.");
            Resolve(board,retry,new Step(retry.GuaranteedTile,false));
            RollBackPriorSlider();
        }

        private static void RollBackPriorSlider()
        {
            foreach (bool locked in new[] {false, true})
            {
                var board = SlidingFixture();
                var offer = board.ActiveWave;
                var pane = board.Slider;
                if (locked) board.Tick(pane.CenterAt(offer.StartsAt + .1), true, offer.StartsAt + .1);
                Resolve(board, offer, new Step(offer.GuaranteedTile, false));
                ulong before = IntactMask(board);
                var wave = board.BeginWave(offer.GuaranteedTile, 12, 3, 20);
                Check(wave.IsBreaking(pane.Tile), "A prior slider must warn before it falls.");
                board.Tick(pane.CenterAt(20.1), true, 20.1);
                Check(pane.Locked, "The previous pane must be catchable in the interrupted wave.");
                board.Pause();
                Check(IntactMask(board) == before && ReferenceEquals(board.Slider, pane) && pane.Locked == locked &&
                    board.AppliedBreakGroupCount == 0 && !board.BreaksApplied && board.WavesSurvived == 2,
                    "Canceling must restore old holes, collapsed panes and the prior slider's lock state.");
            }
        }

        private static bool Close(double a, double b) => Math.Abs(a - b) < 1e-8;

        public static void ReportsResolvedLoss()
        {
            var board = StagedBoard();
            var classic = board.BeginWave(0,2,1,0);
            board.Tick(board.Center(classic.GuaranteedTile),true,classic.Deadline);
            board.RebuildLevel();
            var memory = board.BeginWave(0,2,1,10);
            Check(memory.Mode==WaveMode.Memory && !memory.ShowsWarnings(memory.Deadline),
                "The explanation fixture needs a hidden Memory warning.");
            Check(board.Tick(board.Center(memory.StartingTile),true,memory.Deadline)==WaveResult.Lost &&
                board.LastLoss==WaveLossReason.MemoryPane,"The Memory loss must identify its earlier warning.");

            board = SlidingFixture();
            var wave = board.ActiveWave;
            var pane = board.Slider;
            var point = pane.CenterAt(wave.StartsAt+.1);
            board.Tick(point,true,wave.StartsAt+.1);
            Check(board.Tick(point,true,wave.Deadline)==WaveResult.Survived,"The moving-pane fixture did not catch its pane.");
            var next = board.BeginWave(pane.Tile,3,3,20);
            Check(board.Tick(point,true,next.Deadline)==WaveResult.Lost && board.LastLoss==WaveLossReason.MovingPane,
                "A caught pane that shatters on the next wave needs its own accurate explanation.");
        }

        public static void SlidingAndGrace()
        {
            foreach (float pitch in new[] {0.50f, 0.61f, 0.67f})
            foreach (Vector2 side in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
            {
                BoardState board = SlidingFixture(pitch);
                var pane = board.Slider;
                var wave = board.ActiveWave;
                Check(Math.Abs(pane.HalfSize - BoardState.PaneHalfPitch * pitch) < 1e-6f &&
                    !board.IsIntact(pane.OtherHole) && board.Neighbors(pane.Tile,pane.OtherHole),
                    "The full-sized pane needs two contiguous existing holes.");
                double t = pane.MotionStartsAt + pane.OneWaySeconds / 2;
                Vector2 center = pane.CenterAt(t);
                Check(pane.CenterAt(wave.StartsAt).Equals(pane.CenterAt(pane.MotionStartsAt)) &&
                    !center.Equals(pane.CenterAt(t + 0.3)), "The slide must telegraph direction before moving.");
                for (int frame=0; frame<=180; ++frame)
                {
                    Vector2 p = pane.CenterAt(pane.MotionStartsAt+frame/72.0);
                    foreach (Vector2 corner in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(-1,1),new Vector2(1,1)})
                    {
                        int cell = board.IndexAt(p+corner*pane.HalfSize);
                        Check(cell==pane.Tile || cell==pane.OtherHole,"Moving glass overlaps solid glass or the exterior.");
                    }
                }
                board.Tick(center + side * (pane.HalfSize + 0.002f), true, t);
                Check(!board.CurrentSample.IsSlider && !pane.Locked, "A near miss must not catch the moving glass.");
                double caught = t + 0.1;
                Vector2 fixedCenter = pane.CenterAt(caught);
                board.Tick(fixedCenter + side * (pane.HalfSize - 0.002f), true, caught);
                Check(board.CurrentSample.IsSlider && pane.Locked && pane.CenterAt(t + 3).Equals(fixedCenter),
                    "Reaching the live surface from any side must lock it exactly in place.");
                Check(board.Tick(fixedCenter, true, wave.Deadline) == WaveResult.Survived && !board.IsIntact(pane.Tile),
                    "A locked slider is safe while its underlying hole persists.");
                var onward = board.BeginWave(pane.Tile, 3, 3, 20);
                Check(onward.IsBreaking(pane.Tile) && board.CanStepFrom(pane.Tile,onward.GuaranteedTile) &&
                    (board.Center(onward.GuaranteedTile)-fixedCenter).sqrMagnitude <= pitch*pitch*1.2501f,
                    "The next wave must force a legal ordinary step off the locked pane.");
                Resolve(board, onward, new Step(onward.GuaranteedTile, false));
                Check(board.SliderFor(pane.Tile) == null, "A scheduled locked pane must shatter at zero.");
            }
            foreach (int outcome in new[] { 0, 1, 2, 3 })
            {
                BoardState board = SlidingFixture();
                var wave = board.ActiveWave;
                var pane = board.Slider;
                double t = outcome == 2 ? wave.Deadline - 0.04 : wave.StartsAt + 0.01;
                Vector2 a=board.Center(pane.Tile), b=board.Center(pane.OtherHole), live=pane.CenterAt(t);
                Vector2 corner = (a-live).sqrMagnitude > (b-live).sqrMagnitude ? a : b;
                Check(board.Tick(corner, true, t) == WaveResult.Pending && board.CurrentSample.Kind == SupportKind.Gap,
                    "The first frame over an existing hole starts grace.");
                Vector2 safe = board.Center(wave.GuaranteedTile);
                if (outcome == 0)
                    Check(board.Tick(safe, true, t + 0.299) == WaveResult.Pending, "A timely step must recover from a miss.");
                else if (outcome == 1)
                    Check(board.Tick(corner, true, t + 0.3) == WaveResult.Lost && !board.BreaksApplied &&
                        board.LastLoss == WaveLossReason.GapGraceExpired, "Grace expires before the timer.");
                else if (outcome == 2)
                    Check(board.Tick(corner, true, wave.Deadline) == WaveResult.Lost && board.BreaksApplied &&
                        board.LastLoss == WaveLossReason.GapAtZero, "Grace cannot protect a gap at zero.");
                else
                {
                    board.Tick(corner,true,t+0.295);
                    Check(board.Tick(safe,true,t+0.7)==WaveResult.Lost,"A late step cannot recover after the player was still over the gap at expiry.");
                }
            }
            var ignored=SlidingFixture();
            var first=ignored.ActiveWave;
            var uncaught=ignored.Slider;
            Resolve(ignored,first,new Step(first.GuaranteedTile,false));
            Check(ignored.Slider==uncaught && !uncaught.Locked,"Uncaught glass must remain visible across the deadline.");
            var closing=ignored.BeginWave(first.GuaranteedTile,3,3,20);
            Check(closing.IsBreaking(uncaught.Tile) && closing.ShowsWarnings(closing.StartsAt),"The uncaught pane needs an ordinary warned break.");
            Resolve(ignored,closing,new Step(closing.GuaranteedTile,false));
            Check(ignored.Slider==null,"The uncaught pane must be removed at its warned deadline.");

            foreach (double fraction in new[]{0d,.25d,.5d,.75d,1d})
            {
                var board=SlidingFixture(.539f);
                var pane=board.Slider;
                var plan=board.ActiveWave;
                double at=pane.MotionStartsAt+fraction*pane.OneWaySeconds;
                Vector2 center=pane.CenterAt(at);
                Check((center-(board.Center(pane.Tile)+pane.Direction*(float)fraction*board.Pitch)).sqrMagnitude<1e-10f,
                    "The full pane must travel continuously across the seam between old holes.");
                if (fraction>.5) Check(board.IndexAt(center)==pane.OtherHole,"The catch fixture must reach the other old hole.");
                board.Tick(center,true,at);
                Check(board.CurrentSample.IsSlider && board.CurrentSample.Index==pane.Tile && board.CurrentSample.Stance==center,
                    "The ring and the catch must identify the same live pane on either side of the seam.");
                Resolve(board,plan,new Step(pane.Tile,false));
                var next=board.BeginWave(pane.Tile,3,3,20);
                Check((board.Center(next.GuaranteedTile)-center).sqrMagnitude<=board.Pitch*board.Pitch*1.2501f,
                    "A catch anywhere on the lane needs a reachable onward step.");
            }
        }

        public static void CancelledSlidingWave()
        {
            foreach (bool catchBeforePause in new[] { false, true })
            {
                var board = SlidingFixture();
                var wave = board.ActiveWave;
                var pane = board.Slider;
                double catchTime = pane.MotionStartsAt + pane.OneWaySeconds / 2;
                Vector2 catchPoint = pane.CenterAt(catchTime);
                if (catchBeforePause)
                {
                    board.Tick(catchPoint, true, catchTime);
                    Check(board.CurrentSample.IsSlider && pane.Locked, "The pause fixture must catch the moving pane.");
                    board.Pause();
                }
                else
                    Check(board.Tick(board.Center(wave.StartingTile), false, wave.StartsAt + .2) == WaveResult.Paused,
                        "Losing tracking before the moving pane starts must cancel the wave.");

                Check(board.ActiveWave == null && board.Slider == null && !board.CurrentSample.IsSlider &&
                    board.Tick(catchPoint, true, wave.Deadline + 1) == WaveResult.Pending &&
                    !board.BreaksApplied && board.WavesSurvived == 1,
                    "The canceled wave kept its moving pane, support, deadline, or score.");
                Check(board.IsIntact(pane.Tile) && !board.IsIntact(pane.OtherHole) && !board.Sample(catchPoint,true,39).IsSlider,
                    "Cancellation must restore the original glass in place and withdraw its moving support.");
                var start = board.Sample(board.Center(wave.StartingTile), true, 40);
                var retry = board.BeginWave(start.Index, 3, 2, 40);
                var replacement = board.Slider;
                Check(retry.Seed == wave.Seed && retry.Deadline == 40 + wave.Duration &&
                    replacement != null && !ReferenceEquals(replacement, pane) && !replacement.Locked &&
                    replacement.Tile == pane.Tile && replacement.OtherHole == pane.OtherHole &&
                    retry.IsSafe(pane.Tile) && !retry.IsBreaking(pane.Tile) &&
                    replacement.MotionStartsAt > 40 && replacement.CenterAt(replacement.MotionStartsAt) == board.Center(pane.Tile) &&
                    replacement.CenterAt(replacement.MotionStartsAt + .1) != board.Center(pane.Tile),
                    "Retrying from the same glass must offer the safe slider again with a fresh discovery hold.");
            }

            var earned = SlidingFixture();
            var offer = earned.ActiveWave;
            var locked = earned.Slider;
            double landing = locked.MotionStartsAt + locked.OneWaySeconds / 2;
            Vector2 position = locked.CenterAt(landing);
            earned.Tick(position, true, landing);
            Resolve(earned, offer, new Step(locked.Tile, false));
            var closing = earned.BeginWave(locked.Tile, 3, 3, 20);
            earned.Pause();
            var support = earned.Sample(position, true, 20.5);
            Check(ReferenceEquals(earned.Slider, locked) && locked.Locked && locked.CenterAt(20.5) == position &&
                support.IsSlider && earned.WavesSurvived == 2, "Pausing removed a catch already earned in a completed wave.");
            var resumed = earned.BeginWave(support.Index, 3, 3, 20.5);
            Check(resumed.IsBreaking(locked.Tile) && earned.Tick(position, true, closing.Deadline) == WaveResult.Pending &&
                !earned.BreaksApplied && earned.Slider == locked, "A canceled shatter removed previously earned glass too early.");
            Resolve(earned, resumed, new Step(resumed.GuaranteedTile, false));
            Check(earned.SliderFor(locked.Tile) == null, "The earned pane must still shatter on the replacement wave's deadline.");
        }

        private static BoardState SlidingFixture(float pitch = 0.5f)
        {
            var board = new BoardState(4, 4, 1, pitch);
            var first = board.BeginWave(0, 3, 1, 0);
            Resolve(board, first, new Step(first.GuaranteedTile, false));
            board.BeginWave(first.GuaranteedTile, 3, 2, 10);
            Check(board.Slider != null, "The recorded level-three slider scenario must be reproducible.");
            return board;
        }

        private static bool Adjacent(int a, int b, int width) =>
            Math.Abs(a % width - b % width) + Math.Abs(a / width - b / width) == 1;

        private static ulong IntactMask(BoardState board)
        {
            ulong mask = 0;
            for (int i = 0; i < board.Count; ++i)
                if (board.IsIntact(i)) mask |= 1UL << i;
            return mask;
        }

        private static bool Has(IReadOnlyList<int> values, int tile)
        {
            foreach (int value in values) if (value == tile) return true;
            return false;
        }

        private static bool Same(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; ++i) if (a[i] != b[i]) return false;
            return true;
        }

        private static float Next(float x, bool up)
        {
            if (x == 0) return up ? float.Epsilon : -float.Epsilon;
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(x), 0);
            bits += (x > 0) == up ? 1 : -1;
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
