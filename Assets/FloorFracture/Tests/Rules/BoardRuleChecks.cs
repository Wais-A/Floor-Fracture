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
            DeadlinesAndPause();
            ModesAndReset();
            SlidingAndGrace();
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
            return $"Floor Fracture passed {plans} distinct wave states, {choices} survival choices and {finales} Last Tile clears.";
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
                Resolve(board, prior, path[i]);
                start = path[i].Tile;
            }
            ulong oldMask = IntactMask(board);
            int oldSlider = board.Slider == null ? -1 : board.Slider.Tile;
            ulong state = (uint)oldMask | ((ulong)(uint)start << 16) |
                ((ulong)(uint)(oldSlider + 1) << 20) | ((ulong)(uint)path.Count << 25);
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
                        Resolve(board, wave, new Step(start, false));
                        if (wave.Mode == WaveMode.LastTile) break;
                    }
                }
            }
            Check(visited63 > 0, "The high bit must participate in actual waves.");
            return $"Larger boards cleared {starts} seeded starting positions, tested {plans} waves " +
                   $"and checked onward reachability for {choices} advertised choices.";
        }

        private static void ValidatePlan(BoardState board, WavePlan plan, int start, ulong oldMask)
        {
            Check(plan.StartingTile == start && plan.IsBreaking(start) && !plan.IsSafe(start), "Start must break.");
            Check(Adjacent(plan.GuaranteedTile, start, board.Width) && board.IsIntact(plan.GuaranteedTile) &&
                Has(plan.StationarySafe, plan.GuaranteedTile), "Mandatory step must be orthogonal and permanent.");
            ulong survivors = 0;
            foreach (int tile in plan.StationarySafe) survivors |= 1UL << tile;
            Check(survivors != oldMask && (survivors & ~oldMask) == 0, "Permanent glass must strictly shrink.");
            ulong reachable = 1UL << plan.GuaranteedTile, priorReach;
            do
            {
                priorReach = reachable;
                foreach (int tile in plan.StationarySafe)
                    if ((NeighborBits(tile, board.Width, board.Height) & reachable) != 0) reachable |= 1UL << tile;
            } while (priorReach != reachable);
            Check(reachable == survivors, "Permanent survivors must be a single connected group.");
            for (int i = 0; i < board.Count; ++i)
            {
                bool surface = board.IsIntact(i) || (board.Slider != null && board.Slider.Tile == i);
                Check(surface == (plan.IsBreaking(i) ^ plan.IsSafe(i)), "Plan must partition all supporting panes.");
                if (plan.IsSafe(i) && plan.Mode != WaveMode.LastTile)
                    Check((NeighborBits(i, board.Width, board.Height) & survivors) != 0,
                        "Every choice must have a stationary orthogonal neighbor for the next wave.");
            }
            Check(!plan.IsBreaking(-1) && !plan.IsSafe(-1) && !plan.IsBreaking(64) && !plan.IsSafe(64),
                "Indices outside the 64-bit footprint must not alias valid tile indices.");
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

        private static void Resolve(BoardState board, WavePlan plan, Step step)
        {
            if (step.CatchFirst)
            {
                double landing = plan.StartsAt + 0.1;
                Check(board.Tick(board.Slider.CenterAt(landing), true, landing) == WaveResult.Pending &&
                    board.Slider.Locked, "Early arrival should lock the optional pane.");
            }
            Vector2 position = board.IsIntact(step.Tile) ? board.Center(step.Tile) : board.Slider.CenterAt(plan.Deadline);
            int score = board.WavesSurvived;
            WaveResult expected = plan.Mode == WaveMode.LastTile ? WaveResult.LevelCleared : WaveResult.Survived;
            Check(board.Tick(position, true, plan.Deadline) == expected && board.CurrentSample.Index == step.Tile,
                "Every advertised survivor must work at zero.");
            Check(board.BreaksApplied && board.WavesSurvived == score + 1 && board.ActiveWave == null,
                "Resolve must commit and score exactly once.");
            Check(board.Tick(position, true, plan.Deadline) == WaveResult.Pending && board.WavesSurvived == score + 1,
                "A repeated frame must not award another point.");
            foreach (int tile in plan.StationarySafe) Check(board.IsIntact(tile), "Lost a permanent survivor.");
            foreach (int tile in plan.BreakingTiles) Check(!board.IsIntact(tile), "A scheduled tile failed to break.");
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
            b.Tick(b.Center(2), true, 1);
            Vector2 across = new Vector2(0.003f, 0.25f);
            b.Tick(across, true, 2);
            Check(b.CurrentSample.Index == 2, "Equivalent panes should stabilize the marker near the seam.");
            b.Tick(new Vector2(0.009f, 0.25f), true, 3);
            Check(b.CurrentSample.Index == 3, "Marker should follow a real step.");
            b.Tick(b.Center(0), true, 4);
            b.Tick(new Vector2(0.001f, -0.25f), true, 4.5);
            Check(b.CurrentSample.Index == 1, "A safe/unsafe seam must use its exact footprint.");
            b.Tick(b.Center(2), true, 4.9);
            Check(b.Tick(across, true, wave.Deadline) == WaveResult.Survived && b.CurrentSample.Index == 3,
                "At zero the marker and verdict must use the exact support.");
            var second = b.BeginWave(b.CurrentSample.Index, 1, 2, 6);
            Check(b.Tick(across, true, second.Deadline) == WaveResult.Lost, "Standing still at a seam cannot win the next wave.");
        }

        public static void DeadlinesAndPause()
        {
            var board = new BoardState(4, 4, 3);
            var first = board.BeginWave(0, 1, 1, 10);
            Check(first.Duration == 5 && first.ShowsWarnings(14.99), "Opening wave must show danger for five seconds.");
            ulong full = IntactMask(board);
            Vector2 safe = board.Center(first.GuaranteedTile);
            Check(board.Tick(safe, true, 14.9999) == WaveResult.Pending && IntactMask(board) == full &&
                ReferenceEquals(first, board.ActiveWave), "Early safety must not advance or reroll the wave.");
            Check(board.Tick(board.Center(0), true, 15) == WaveResult.Lost && board.BreaksApplied &&
                board.WavesSurvived == 0, "Remaining on the start at zero must lose.");
            board.Reset(4, 4, 3);
            first = board.BeginWave(0, 1, 1, 10);
            board.Tick(board.Center(0), true, 14.99);
            Check(board.Tick(safe, true, 15.99) == WaveResult.Lost, "A late frame cannot grant an extra step after zero.");
            board.Reset(4, 4, 3);
            first = board.BeginWave(0, 1, 1, 10);
            board.Tick(safe, true, 14.99);
            Check(board.Tick(board.Center(0), true, 15.99) == WaveResult.Survived, "A step after zero cannot revoke survival.");

            foreach (int reason in new[] { 0, 1, 2, 3 })
            {
                board.Reset(4, 4, 3);
                first = board.BeginWave(0, 1, 1, 10);
                if (reason == 3) board.Pause();
                else
                {
                    Vector2 interrupted = reason == 0 ? new Vector2(2, 0) :
                        reason == 1 ? safe : new Vector2(float.NaN, 0);
                    Check(board.Tick(interrupted, reason != 1, 15) == WaveResult.Paused, "Interruption must pause even at zero.");
                }
                Check(!board.BreaksApplied && board.ActiveWave == null && IntactMask(board) == full &&
                    board.WavesSurvived == 0, "Pausing must preserve all previously solid glass and score.");
                var from = board.Sample(board.Center(4), true, 40);
                var fresh = board.BeginWave(from.Index, 1, 1, 40);
                Check(fresh.Deadline == 45 && fresh.IsBreaking(4) && Adjacent(4, fresh.GuaranteedTile, 4),
                    "Resume needs a full timer and a legal move from the current pose.");
            }
        }

        public static void ModesAndReset()
        {
            var board = new BoardState(4, 4, 143);
            var other = new BoardState(4, 4, -146);
            var baseline = other.BeginWave(0, 1, 1, 27);
            var modes = new List<WaveMode>();
            bool sawFog = false;
            for (int level = 1; modes.Count < 22; ++level)
            {
                int start = 0;
                for (int number = 1; ; ++number)
                {
                    var plan = board.BeginWave(start, level, number, number * 10);
                    if (level == 1 && number == 1)
                        Check(Same(plan.BreakingTiles, baseline.BreakingTiles) && plan.Duration == baseline.Duration &&
                            plan.Seed == baseline.Seed, "Opening pattern must be independent of run seed and wall clock.");
                    if (level == 1) Check(!plan.Fog && board.Slider == null, "The first level must introduce base play.");
                    if (level == 2 && number == 1) sawFog = plan.Fog;
                    if (plan.Mode == WaveMode.Memory)
                        Check(plan.ShowsWarnings(plan.StartsAt) && !plan.ShowsWarnings(plan.RevealEndsAt) &&
                            !plan.ShowsWarnings(plan.Deadline), "Memory must hide damage cues while the timer continues.");
                    start = plan.GuaranteedTile;
                    Resolve(board, plan, new Step(start, false));
                    if (plan.Mode == WaveMode.LastTile) break;
                    modes.Add(plan.Mode);
                }
                int score = board.WavesSurvived;
                board.RebuildLevel();
                Check(IntactMask(board) == 65535 && board.Slider == null && board.WavesSurvived == score,
                    "Clearing must restore the original footprint and retain run score.");
            }
            Check(sawFog && modes[0] == WaveMode.Classic && modes[1] == WaveMode.Memory, "Base and fog introductions failed.");
            for (int i = 2; i + 1 < modes.Count; i += 2)
                Check(modes[i] != modes[i + 1], "A shuffled two-card bag must contain one of each mode.");
            board.Reset(2, 3, -55);
            Check(board.WavesSurvived == 0 && board.Slider == null && board.ActiveWave == null &&
                board.CurrentSample.Kind == SupportKind.Untracked && IntactMask(board) == 63, "Restart must clear the run.");
            double previous = 10;
            for (int level = 1; level <= 20; ++level)
            {
                board.RebuildLevel();
                double duration = board.BeginWave(0, level, 1, 0).Duration;
                Check(duration == Math.Max(2.5, 5.0 - (level - 1) * 0.4) && duration <= previous,
                    "Level timing must shorten to a finite ordinary-step floor.");
                previous = duration;
            }
        }

        public static void SlidingAndGrace()
        {
            foreach (float pitch in new[] {0.50f, 0.61f, 0.67f})
            foreach (Vector2 side in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
            {
                BoardState board = SlidingFixture(pitch);
                var pane = board.Slider;
                var wave = board.ActiveWave;
                Check(Math.Abs(pane.HalfSize - 0.30f * pitch) < 1e-6f &&
                    Math.Abs(pane.Travel - 0.18f * pitch) < 1e-6f, "The moving pane must scale with physical pitch.");
                double t = pane.MotionStartsAt + 0.3;
                Vector2 center = pane.CenterAt(t);
                Check(pane.CenterAt(wave.StartsAt).Equals(pane.CenterAt(pane.MotionStartsAt)) &&
                    !center.Equals(pane.CenterAt(t + 0.3)), "The slide must telegraph direction before moving.");
                foreach (double time in new[] { t - 1, t, t + 0.3, t + 1.8, t + 6 })
                {
                    Vector2 d = pane.CenterAt(time) - board.Center(pane.Tile);
                    Check(Math.Abs(d.x) + pane.HalfSize <= pitch * 0.5f && Math.Abs(d.y) + pane.HalfSize <= pitch * 0.5f,
                        "Every moving pane edge must stay inside the old hole.");
                }
                board.Tick(center + side * (pane.HalfSize + 0.002f), true, t);
                Check(board.CurrentSample.Kind == SupportKind.Gap && !pane.Locked, "A near miss must expose the hole.");
                double caught = t + 0.1;
                Vector2 fixedCenter = pane.CenterAt(caught);
                board.Tick(fixedCenter + side * (pane.HalfSize - 0.002f), true, caught);
                Check(board.CurrentSample.IsSlider && pane.Locked && pane.CenterAt(t + 3).Equals(fixedCenter),
                    "Reaching the live surface from any side must lock it exactly in place.");
                Check(board.Tick(fixedCenter, true, wave.Deadline) == WaveResult.Survived && !board.IsIntact(pane.Tile),
                    "A locked slider is safe while its underlying hole persists.");
                var onward = board.BeginWave(pane.Tile, 3, 3, 20);
                Check(onward.IsBreaking(pane.Tile) && Adjacent(pane.Tile, onward.GuaranteedTile, board.Width),
                    "The next wave must force a legal ordinary step off the locked pane.");
                Resolve(board, onward, new Step(onward.GuaranteedTile, false));
                Check(board.Slider == null, "A scheduled locked pane must shatter at zero.");
            }
            foreach (int outcome in new[] { 0, 1, 2, 3 })
            {
                BoardState board = SlidingFixture();
                var wave = board.ActiveWave;
                var pane = board.Slider;
                double t = outcome == 2 ? wave.Deadline - 0.04 : wave.StartsAt + 0.01;
                Vector2 corner = board.Center(pane.Tile) + new Vector2(0.245f, 0.245f);
                Check(board.Tick(corner, true, t) == WaveResult.Pending && board.CurrentSample.Kind == SupportKind.Gap,
                    "The first frame over an existing hole starts grace.");
                Vector2 safe = board.Center(wave.GuaranteedTile);
                if (outcome == 0)
                    Check(board.Tick(safe, true, t + 0.299) == WaveResult.Pending, "A timely step must recover from a miss.");
                else if (outcome == 1)
                    Check(board.Tick(corner, true, t + 0.3) == WaveResult.Lost && !board.BreaksApplied, "Grace expires before the timer.");
                else if (outcome == 2)
                    Check(board.Tick(corner, true, wave.Deadline) == WaveResult.Lost && board.BreaksApplied, "Grace cannot protect a gap at zero.");
                else
                    Check(board.Tick(safe, true, t + 0.3) == WaveResult.Lost, "Landing at grace expiry is too late.");
            }
        }

        private static BoardState SlidingFixture(float pitch = 0.5f)
        {
            var board = new BoardState(4, 4, 143, pitch);
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
