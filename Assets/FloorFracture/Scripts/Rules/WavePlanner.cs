using System;
using System.Collections.Generic;

namespace FloorFracture
{
    public enum WaveMode { Classic, Memory, LastTile, Jump }
    public enum WaveResult { Pending, Paused, Survived, LevelCleared, Lost }

    public sealed class WaveBreakGroup
    {
        public IReadOnlyList<int> Tiles { get; }
        public double RevealStartsAt { get; }
        public double RevealEndsAt { get; }
        public double BreaksAt { get; }

        internal WaveBreakGroup(List<int> tiles, double revealStart, double clueSeconds, double breaksAt)
        {
            Tiles = tiles.AsReadOnly();
            RevealStartsAt = revealStart;
            RevealEndsAt = revealStart + clueSeconds;
            BreaksAt = breaksAt;
        }

        public bool ShowsWarning(double now) => now >= RevealStartsAt && now < RevealEndsAt;
    }

    public sealed class WavePlan
    {
        public WaveMode Mode { get; }
        public int Level { get; }
        public int Number { get; }
        public int Seed { get; }
        public int StartingTile { get; }
        public int GuaranteedTile { get; }
        public IReadOnlyList<int> JumpLandings { get; }
        public int JumpGap => IsJump ? StartingTile + (GuaranteedTile - StartingTile) / 2 : -1;
        public bool IsJump => JumpLandings.Count != 0;
        public double JumpOpensAt { get; }
        // Each entry is the next reachable pane in the straight crossing, including its safe end.
        public IReadOnlyList<int> RowRoute { get; }
        public double StartsAt { get; }
        public double Duration { get; }
        public double Deadline => StartsAt + Duration;
        public double RevealEndsAt { get; }
        public WaveHazards Hazards { get; }
        public bool Fog => Hazards.FogDensity > 0 || Hazards.FogPatches > 0;
        public double RecoverySeconds { get; }
        public IReadOnlyList<WaveBreakGroup> BreakGroups { get; }
        public IReadOnlyList<int> BreakingTiles { get; }
        public IReadOnlyList<int> SafeTiles { get; }
        public IReadOnlyList<int> StationarySafe { get; }
        internal bool[] Stationary { get; }
        internal SlidingPane[] OfferedSliders { get; }
        // A ride finale breaks every stationary pane; its only offer is the guarantee, passing RideCell beside the start.
        public int RideSlider { get; }
        public bool IsRide => RideSlider >= 0;
        public int RideCell { get; }

        private readonly WaveBreakGroup[] groupByTile;

        internal WavePlan(BoardState board, int level, int number, int seed, int start, int guaranteed,
            bool[] stationary, SlidingPane[] sliders, WaveMode mode, WavePacing pacing, double now,
            IReadOnlyList<int> landings, int[] rowRoute, int rideCell = -1)
        {
            RideCell = rideCell;
            RideSlider = rideCell < 0 ? -1 : 0;
            Level = level;
            Number = number;
            Seed = seed;
            StartingTile = start;
            GuaranteedTile = guaranteed;
            JumpLandings = landings;
            RowRoute = rowRoute;
            StartsAt = now;
            double switchSeconds = WavePacing.SwitchSeconds(level, number);
            JumpOpensAt = now;
            if (IsJump)
            {
                for (int y = Math.Max(0, start / board.Width - 1); y <= Math.Min(board.Height - 1, start / board.Width + 1); ++y)
                    for (int x = Math.Max(0, start % board.Width - 1); x <= Math.Min(board.Width - 1, start % board.Width + 1); ++x)
                        if (y * board.Width + x != start && board.IsIntact(y * board.Width + x))
                            JumpOpensAt = now + WavePacing.MinimumStep;
                Duration = JumpOpensAt - now + Math.Max(switchSeconds, WavePacing.JumpSeconds);
            }
            else if (RowRoute.Count != 0)
            {
                for (int i = 0; i < RowRoute.Count; ++i) Duration += WavePacing.RowStepSeconds(i);
            }
            else Duration = switchSeconds;
            RecoverySeconds = WavePacing.RecoveryAt(level);
            Hazards = WaveHazards.Roll(level, number, seed, now, Duration);
            Mode = mode;
            Stationary = stationary;
            OfferedSliders = sliders;

            groupByTile = new WaveBreakGroup[board.Count];
            var survivors = new List<int>();
            var allSafe = sliders.Length == 0 ? null : new List<int>();
            var breaks = new List<int>();
            for (int tile = 0; tile < board.Count; ++tile)
            {
                if (stationary[tile]) survivors.Add(tile);
                if (IsSafe(tile)) allSafe?.Add(tile);
                else if (board.IsIntact(tile) || board.SliderFor(tile) != null)
                    breaks.Add(tile);
            }
            StationarySafe = survivors.AsReadOnly();
            SafeTiles = allSafe == null ? StationarySafe : allSafe.AsReadOnly();
            BreakingTiles = breaks.AsReadOnly();
            BreakGroups = GroupBreaks(board, breaks, pacing.ClueSeconds);
            RevealEndsAt = BreakGroups[BreakGroups.Count - 1].RevealEndsAt;
        }

        private IReadOnlyList<WaveBreakGroup> GroupBreaks(BoardState board, List<int> breaks, double clueSeconds)
        {
            if (IsJump)
            {
                var opening = new List<int>();
                var final = new List<int>();
                foreach (int tile in breaks)
                {
                    if (tile != StartingTile && WavePlanner.TouchesStart(board, StartingTile, tile)) opening.Add(tile);
                    else final.Add(tile);
                }
                var jumpGroups = new List<WaveBreakGroup>(2);
                if (opening.Count != 0) jumpGroups.Add(Register(opening, StartsAt, clueSeconds, JumpOpensAt));
                jumpGroups.Add(Register(final, JumpOpensAt, clueSeconds, Deadline));
                return jumpGroups.AsReadOnly();
            }
            if (RowRoute.Count != 0)
            {
                int rowCount = RowRoute.Count;
                var rows = new List<int>[rowCount];
                for (int i = 0; i < rowCount; ++i) rows[i] = new List<int>();
                foreach (int tile in breaks)
                {
                    int distance = RowDistance(board, StartingTile, RowRoute[0], tile);
                    rows[Math.Clamp(distance, 0, rowCount - 1)].Add(tile);
                }
                var schedule = new WaveBreakGroup[rowCount];
                double cue = StartsAt;
                for (int i = 0; i < rowCount; ++i)
                {
                    double drop = i == rowCount - 1 ? Deadline : cue + WavePacing.RowStepSeconds(i);
                    schedule[i] = Register(rows[i], cue, clueSeconds, drop);
                    cue = drop;
                }
                return schedule;
            }
            if (Level >= 6)
                return new[] { Register(breaks, StartsAt, clueSeconds, Deadline) };
            // Keep the player's pane for zero, even when the sweep starts on that side.
            bool sweep = Mode != WaveMode.LastTile && (Level >= 3 || Mode == WaveMode.Memory);
            int desired = sweep ? 2 : 1;
            int count = Math.Min(desired, breaks.Count);
            var ordered = new List<int>(breaks);
            ordered.Remove(StartingTile);
            int direction = (int)((uint)Seed % 5);
            if (count > 1 && Mode == WaveMode.Memory)
                WavePlanner.Shuffle(ordered, Seed);
            else if (count > 1)
                ordered.Sort((a, b) =>
                {
                    int order = SweepPosition(board, a, direction).CompareTo(SweepPosition(board, b, direction));
                    return order != 0 ? order : a.CompareTo(b);
                });
            ordered.Add(StartingTile);

            var groups = new WaveBreakGroup[count];
            int first = 0;
            for (int i = 0; i < count; ++i)
            {
                int size = ordered.Count / count + (i < ordered.Count % count ? 1 : 0);
                var tiles = ordered.GetRange(first, size);
                double delay = Math.Min(.12 * i, Duration - WavePacing.MinimumStep);
                double breakAfter = i < count - 1 && Level == 5 ? Duration * .75 : Duration;
                groups[i] = Register(tiles, StartsAt + delay, clueSeconds,
                    StartsAt + Math.Max(breakAfter, delay + WavePacing.MinimumStep));
                first += size;
            }
            return Array.AsReadOnly(groups);
        }

        private WaveBreakGroup Register(List<int> tiles, double cue, double clueSeconds, double drop)
        {
            var group = new WaveBreakGroup(tiles, cue, clueSeconds, drop);
            foreach (int tile in tiles) groupByTile[tile] = group;
            return group;
        }

        internal static int RowDistance(BoardState board, int start, int next, int tile)
        {
            int dx = next % board.Width - start % board.Width;
            int dy = next / board.Width - start / board.Width;
            return dx * (tile % board.Width - start % board.Width) + dy * (tile / board.Width - start / board.Width);
        }

        public bool IsJumpGap(int tile)
        {
            if (tile < 0) return false;
            for (int i = 0; i < JumpLandings.Count; ++i)
                if (StartingTile + (JumpLandings[i] - StartingTile) / 2 == tile) return true;
            return false;
        }

        private static long SweepPosition(BoardState board, int tile, int direction)
        {
            int x = tile % board.Width, y = tile / board.Width;
            switch (direction)
            {
                case 0: return x;
                case 1: return -x;
                case 2: return y;
                case 3: return -y;
                default: // Outermost panes first, then inward toward the board center.
                    return -(Math.Abs(2L * x + 1 - board.Width) + Math.Abs(2L * y + 1 - board.Height));
            }
        }

        public WaveBreakGroup BreakGroupFor(int tile) =>
            (uint)tile < (uint)groupByTile.Length ? groupByTile[tile] : null;
        public bool ShowsWarning(int tile, double now) => BreakGroupFor(tile)?.ShowsWarning(now) == true;
        public bool IsBreaking(int tile) => BreakGroupFor(tile) != null;
        public bool IsSafe(int tile)
        {
            if ((uint)tile >= (uint)Stationary.Length) return false;
            if (Stationary[tile]) return true;
            foreach (var pane in OfferedSliders) if (pane.Tile == tile) return true;
            return false;
        }

        // Give a newly displayed group its full interval even if rendering missed its scheduled frame.
        // The scene clock carries the delay forward, so the next row starts when the last row visibly drops.
        public double TimeForVisibleCue(double now, ref int nextGroup)
        {
            if (nextGroup >= BreakGroups.Count || now < BreakGroups[nextGroup].RevealStartsAt) return now;
            double cue = BreakGroups[nextGroup].RevealStartsAt;
            do { ++nextGroup; } while (nextGroup < BreakGroups.Count && BreakGroups[nextGroup].RevealStartsAt == cue);
            return cue;
        }

        public bool ShowsWarnings(double now)
        {
            for (int i = 0; i < BreakGroups.Count; ++i)
                if (BreakGroups[i].ShowsWarning(now)) return true;
            return false;
        }
    }

    public static class WavePlanner
    {
        internal static WavePlan Plan(BoardState board, int start, int level, int number,
            int advancedWavesSurvived, double now)
        {
            bool opening = level == 1 && number == 1;
            int seed = PatternSeed(board, start, level, number, opening);
            var random = new Generator(seed);

            var pacing = WavePacing.At(level);
            int desired = Math.Max(1, pacing.TargetSurvivors(board.IntactCount));
            int slot = level >= 9 ? (int)(((long)level - 9 + number - 1) % 6) :
                level >= 6 ? (int)(((long)level - 6 + number - 1) % 4) : -1;
            bool rowTurn = slot == 0;
            bool jumpTurn = level < 6 || (slot != 0 && slot != (level >= 9 ? 3 : 2));
            var landings = new List<int>(4);
            bool jumpsUnlocked = level >= 4 || (level >= 2 && number >= level);
            if (jumpTurn && jumpsUnlocked && board.ConsecutiveJumps < (level >= 9 ? 2 : 1) && board.Sliders.Count == 0)
            {
                for (int direction = 0; direction < 4; ++direction)
                {
                    int gap = board.Layout.Neighbor(start, direction);
                    if (!board.Layout.Contains(gap)) continue;
                    int landing = board.Layout.Neighbor(gap, direction);
                    if (board.IsIntact(landing)) landings.Add(landing);
                }
            }

            int guaranteed;
            int[] route = Array.Empty<int>();
            bool[] survivors;
            if (landings.Count > 0)
            {
                guaranteed = landings[random.Next(landings.Count)];
                survivors = JumpSurvivors(board, start, landings, desired, ref random);
            }
            else
            {
                if (rowTurn && board.Sliders.Count == 0)
                    route = ChooseRow(board, start, level >= 9 ? 3 : 2, ref random);
                survivors = OrdinarySurvivors(board, start, desired, route, opening, ref random, out guaranteed);
            }
            int remaining = 0;
            foreach (bool safe in survivors) if (safe) ++remaining;
            WaveMode mode = remaining == 1 ? WaveMode.LastTile : landings.Count != 0 ? WaveMode.Jump :
                level == 1 ? WaveMode.Classic : ChooseMode(board.Seed, advancedWavesSurvived, level);
            var sliders = level >= 3 && remaining > 1 ?
                ChooseSliders(board, start, survivors, route, landings.Count != 0, level >= 9 ? 3 : level >= 6 ? 2 : 1,
                    level >= 5, now, WavePacing.SwitchSeconds(level, number), ref random) : Array.Empty<SlidingPane>();
            var plan = new WavePlan(board, level, number, seed, start, guaranteed, survivors, sliders,
                mode, pacing, now, landings.AsReadOnly(), route);
            if (remaining != 1 || level < 6 || board.Sliders.Count != 0) return plan;
            var none = new bool[board.Count];
            var ride = ChooseSliders(board, start, none, route, false, 1, true, now,
                WavePacing.SwitchSeconds(level, number), ref random, plan.Deadline);
            if (ride.Length == 0 || random.Next(2) != 0) return plan;
            return new WavePlan(board, level, number, seed, start, ride[0].Tile, none, ride, mode, pacing, now,
                landings.AsReadOnly(), route, RideCell(board, start, ride[0], now, plan.Deadline));
        }

        // The pane is catchable while centered within a quarter pitch of a cell beside the start. Sampled every
        // 10 ms from the start until .3 s before zero, it must be catchable for .35 s in all. Returns the neighbour
        // with the most catchable time (lowest index on ties), or -1.
        internal static int RideCell(BoardState board, int start, SlidingPane pane, double startsAt, double deadline)
        {
            int best = -1, bestNear = 0, total = 0;
            foreach (int cell in new[] { start - board.Width, start - 1, start + 1, start + board.Width })
            {
                if (!board.Neighbors(start, cell)) continue;
                int near = 0;
                for (int i = 0; startsAt + i * .01 <= deadline - .3; ++i)
                {
                    UnityEngine.Vector2 d = pane.CenterAt(startsAt + i * .01) - board.Center(cell);
                    if (Math.Max(Math.Abs(d.x), Math.Abs(d.y)) <= .25f * board.Pitch) ++near;
                }
                total += near;
                if (near > bestNear) { best = cell; bestNear = near; }
            }
            return total >= 35 ? best : -1;
        }

        private static int[] ChooseRow(BoardState board, int start, int maxSteps, ref Generator random)
        {
            int best = 0, direction = -1, ties = 0;
            for (int d = 0; d < 4; ++d)
            {
                int tile = start, steps = 0;
                while (steps < maxSteps && board.IsIntact(tile = board.Layout.Neighbor(tile, d))) ++steps;
                if (steps == 0 || steps < best) continue;
                if (steps > best) { best = steps; ties = 0; }
                if (random.Next(++ties) == 0) direction = d;
            }
            if (direction < 0) return Array.Empty<int>();
            var route = new int[best];
            int current = start;
            for (int i = 0; i < best; ++i) route[i] = current = board.Layout.Neighbor(current, direction);
            return route;
        }

        private static bool[] OrdinarySurvivors(BoardState board, int start, int desired, int[] route,
            bool opening, ref Generator random, out int guaranteed)
        {
            var sizes = new int[board.Count];
            var queue = new int[board.IntactCount];
            int largest;
            if (route.Length != 0)
            {
                // All final survivors lie beyond the last row that falls.
                guaranteed = route[route.Length - 1];
                for (int i = 0; i < sizes.Length; ++i)
                    if (WavePlan.RowDistance(board, start, route[0], i) < route.Length) sizes[i] = -1;
                largest = MeasureComponent(board, start, guaranteed, queue, sizes);
            }
            else
            {
                var options = new List<int>(8);
                largest = 0;
                for (int tile = 0; tile < board.Count; ++tile)
                {
                    if (!board.IsIntact(tile) || !board.CanStepFrom(start, tile)) continue;
                    int size = sizes[tile];
                    if (size == 0) size = MeasureComponent(board, start, tile, queue, sizes);
                    if (size < largest) continue;
                    if (size > largest) { options.Clear(); largest = size; }
                    options.Add(tile);
                }
                if (options.Count == 0) throw new InvalidOperationException("No supported step from the current pane.");
                guaranteed = options[opening ? 0 : random.Next(options.Count)];
            }
            int target = Math.Min(largest, Math.Min(desired, Math.Max(1, board.IntactCount - 1)));
            return GrowSurvivors(board.Layout, guaranteed, target, sizes, opening, ref random);
        }

        private static bool[] JumpSurvivors(BoardState board, int start, List<int> landings, int target, ref Generator random)
        {
            // Join every available landing to its reachable peers. Opposite ends of a narrow
            // floor can be separate islands. Each keeps an onward step whenever one exists.
            var parent = new int[board.Count];
            Array.Fill(parent, -1);
            var kept = new bool[board.Count];
            var queue = new Queue<int>();
            int count = 0;
            foreach (int landing in landings)
            {
                if (parent[landing] < 0)
                {
                    parent[landing] = landing;
                    queue.Enqueue(landing);
                    while (queue.Count > 0)
                    {
                        int here = queue.Dequeue();
                        for (int d = 0; d < 4; ++d)
                        {
                            int next = board.Layout.Neighbor(here, d);
                            if (!board.IsIntact(next) || parent[next] >= 0 || TouchesStart(board, start, next)) continue;
                            parent[next] = here;
                            queue.Enqueue(next);
                        }
                    }
                }
                for (int tile = landing; !kept[tile]; tile = parent[tile])
                {
                    kept[tile] = true;
                    ++count;
                }
            }
            foreach (int landing in landings)
            {
                int exit = -1;
                for (int d = 0; d < 4; ++d)
                {
                    int next = board.Layout.Neighbor(landing, d);
                    if (next < 0 || parent[next] < 0) continue;
                    if (kept[next]) { exit = -1; break; }
                    exit = next;
                }
                if (exit >= 0) { kept[exit] = true; ++count; }
            }
            var frontier = new List<int>();
            for (int tile = 0; tile < kept.Length; ++tile) if (kept[tile]) AddNeighbors(tile);
            while (count < target && frontier.Count != 0)
            {
                int pick = random.Next(frontier.Count), tile = frontier[pick];
                frontier[pick] = frontier[frontier.Count - 1];
                frontier.RemoveAt(frontier.Count - 1);
                kept[tile] = true;
                ++count;
                AddNeighbors(tile);
            }
            return kept;

            void AddNeighbors(int tile)
            {
                for (int d = 0; d < 4; ++d)
                {
                    int next = board.Layout.Neighbor(tile, d);
                    if (next < 0 || parent[next] < 0 || kept[next]) continue;
                    parent[next] = -1;
                    frontier.Add(next);
                }
            }
        }

        internal static bool TouchesStart(BoardState board, int start, int tile) =>
            Math.Abs(start % board.Width - tile % board.Width) <= 1 &&
            Math.Abs(start / board.Width - tile / board.Width) <= 1;

        // Memory scatters sequential warning groups so a single spatial sweep cannot predict them.
        internal static void Shuffle(List<int> tiles, int seed)
        {
            var random = new Generator(seed);
            for (int i = tiles.Count - 1; i > 0; --i)
            {
                int other = random.Next(i + 1);
                (tiles[i], tiles[other]) = (tiles[other], tiles[i]);
            }
        }

        private static int MeasureComponent(BoardState board, int start, int first, int[] queue, int[] sizes)
        {
            int tail = 1;
            queue[0] = first;
            sizes[first] = -1;
            for (int head = 0; head < tail; ++head)
                for (int d = 0; d < 4; ++d)
                {
                    int next = board.Layout.Neighbor(queue[head], d);
                    if (next == start || !board.IsIntact(next) || sizes[next] != 0) continue;
                    sizes[next] = -1;
                    queue[tail++] = next;
                }
            for (int i = 0; i < tail; ++i) sizes[queue[i]] = tail;
            return tail;
        }

        private static bool[] GrowSurvivors(BoardLayout layout, int first, int target, int[] available,
            bool opening, ref Generator random)
        {
            var survivors = new bool[layout.CellCount];
            var frontier = new List<int>();
            int tile = first;
            available[first] = 0;
            for (int count = 1; ; ++count)
            {
                survivors[tile] = true;
                if (count == target) return survivors;
                for (int d = 0; d < 4; ++d)
                {
                    int next = layout.Neighbor(tile, d);
                    if (next < 0 || available[next] <= 0) continue;
                    available[next] = 0;
                    // Row-major selection preserves saved seeds across board representations.
                    frontier.Insert(~frontier.BinarySearch(next), next);
                }
                int pick = opening ? 0 : random.Next(frontier.Count);
                tile = frontier[pick];
                frontier.RemoveAt(pick);
            }
        }

        private static SlidingPane[] ChooseSliders(BoardState board, int start, bool[] survivors, int[] row,
            bool jump, int limit, bool patrols, double now, double duration, ref Generator random,
            double rideDeadline = double.NaN)
        {
            // A ride ends the level on landing, so it needs no side exits; it must be catchable beside the start.
            bool ride = !double.IsNaN(rideDeadline);
            var panes = new List<SlidingPane>(limit);
            var reserved = new bool[board.Count];
            // Last wave's panes keep shuttling (or holding the player) until they break this wave.
            // A pane's center never leaves its own path, so the path also covers the cell under it.
            foreach (var old in board.Sliders)
                foreach (int cell in old.Path) reserved[cell] = true;
            var trail = new int[5];
            var missing = new List<UnityEngine.Vector2>();
            int[] patrol = null, patrolCover = null;
            int patrolLength = 0, patrolTies = 0;
            while (panes.Count < limit)
            {
                int source = -1, end = -1, bestLength = 0, ties = 0;
                for (int a = 0; a < board.Count; ++a)
                {
                    if (!CanStart(a)) continue;
                    for (int d = 0; d < 4; ++d)
                    {
                        bool horizontal = d == 1 || d == 2;
                        if (!ride && !HasSideExit(board.Layout, a, horizontal, survivors)) continue;
                        int b = a, length = 0;
                        for (int next = board.Layout.Neighbor(b, d); board.Layout.Contains(next);
                            next = board.Layout.Neighbor(b, d))
                        {
                            if (board.IsIntact(next) || Unavailable(next) ||
                                (!ride && !HasSideExit(board.Layout, next, horizontal, survivors))) break;
                            b = next;
                            ++length;
                        }
                        if (length == 0 || length < bestLength ||
                            (ride && RideCell(board, start, new SlidingPane(board, a, b, now, duration), now, rideDeadline) < 0)) continue;
                        if (length > bestLength) { bestLength = length; ties = 0; }
                        if (random.Next(++ties) == 0) { source = a; end = b; }
                    }
                }
                // Patrol draws follow every lane draw so levels without patrols keep their seeded panes.
                // A patrol, when one exists, always replaces the lane.
                patrol = null;
                patrolLength = patrolTies = 0;
                if (patrols)
                    for (int a = 0; a < board.Count; ++a)
                    {
                        if (!CanStart(a)) continue;
                        trail[0] = a;
                        Walk(1, -1, false, Array.Empty<int>(), ref random);
                    }
                if (source < 0 && patrol == null) break;
                var pane = patrol != null
                    ? new SlidingPane(board, patrol, now, duration) { PromotedExits = patrolCover }
                    : new SlidingPane(board, source, end, now, duration);
                // Promoted tiles stay intact this wave so a pane caught anywhere on its patrol has an exit.
                foreach (int tile in pane.PromotedExits) survivors[tile] = true;
                panes.Add(pane);
                foreach (int cell in pane.Path) reserved[cell] = true;
            }
            return panes.ToArray();

            bool CanStart(int a) => a != start && board.IsIntact(a) && !survivors[a] && !Unavailable(a);

            bool Unavailable(int tile) => reserved[tile] || (jump && TouchesStart(board, start, tile)) ||
                (row.Length != 0 && WavePlan.RowDistance(board, start, row[0], tile) < row.Length);

            // Extends trail[0..count) over holes. A pane locked anywhere on the path must reach a survivor,
            // possibly one of at most two breaking tiles promoted to survivors (cover).
            void Walk(int count, int heading, bool turned, int[] cover, ref Generator draw)
            {
                if (turned && count >= patrolLength && // a turn needs three cells
                    (!ride || RideCell(board, start, new SlidingPane(board, trail[..count], now, duration), now, rideDeadline) >= 0))
                {
                    if (count > patrolLength) { patrolLength = count; patrolTies = 0; }
                    if (draw.Next(++patrolTies) == 0) { patrol = trail[..count]; patrolCover = cover; }
                }
                if (count == trail.Length) return;
                int tile = trail[count - 1];
                for (int d = 0; d < 4; ++d)
                {
                    int next = board.Layout.Neighbor(tile, d);
                    if (!board.Layout.Contains(next) || board.IsIntact(next) || Unavailable(next) ||
                        Array.IndexOf(trail, next, 0, count) >= 0) continue;
                    int mark = missing.Count;
                    if (!ride) AddMissingExits(board, tile, next, survivors, missing);
                    trail[count] = next;
                    int[] found = missing.Count == mark ? cover : Cover(count + 1);
                    if (found != null) Walk(count + 1, d, turned || (heading >= 0 && d != heading), found, ref draw);
                    missing.RemoveRange(mark, missing.Count - mark);
                }
            }

            // The smallest set of promotable tiles reaching every missing exit, lowest indices first; null if none.
            int[] Cover(int count)
            {
                var near = new SortedSet<int>();
                for (int i = 0; i < count; ++i)
                    for (int y = Math.Max(0, trail[i] / board.Width - 1); y <= Math.Min(board.Height - 1, trail[i] / board.Width + 1); ++y)
                    for (int x = Math.Max(0, trail[i] % board.Width - 1); x <= Math.Min(board.Width - 1, trail[i] % board.Width + 1); ++x)
                        if (Promotable(y * board.Width + x, count)) near.Add(y * board.Width + x);
                // Promoted tiles must join the survivors' group, directly or through the other promoted tile.
                foreach (int one in near)
                    if (Joins(one, -1) && missing.TrueForAll(point => Reaches(board, one, point))) return new[] { one };
                foreach (int one in near)
                foreach (int two in near)
                    if (two > one && (Joins(one, -1) || Joins(two, -1)) && Joins(one, two) && Joins(two, one) &&
                        missing.TrueForAll(point => Reaches(board, one, point) || Reaches(board, two, point)))
                        return new[] { one, two };
                return null;
            }

            bool Promotable(int tile, int count) => tile != start && board.IsIntact(tile) && !survivors[tile] &&
                !Unavailable(tile) && Array.IndexOf(trail, tile, 0, count) < 0;

            bool Joins(int tile, int other)
            {
                for (int d = 0; d < 4; ++d)
                {
                    int next = board.Layout.Neighbor(tile, d);
                    if (next >= 0 && (survivors[next] || next == other)) return true;
                }
                return false;
            }
        }

        // CanExitTo's geometry, kept inside its 1.2501 bound: both cell centers and 20 points between them
        // need a survivor. Midway samples are offset so abutting side exits (1.25 at the seam) still count.
        private static void AddMissingExits(BoardState board, int tile, int next, bool[] survivors,
            List<UnityEngine.Vector2> missing)
        {
            UnityEngine.Vector2 a = board.Center(tile), b = board.Center(next);
            for (int s = 0; s <= 21; ++s)
            {
                var point = s == 0 ? a : s == 21 ? b : UnityEngine.Vector2.Lerp(a, b, (s - .5f) / 20);
                if (!NearSurvivor(tile, point) && !NearSurvivor(next, point)) missing.Add(point);
            }

            // Any survivor within reach of the step is within one cell of one of its ends.
            bool NearSurvivor(int end, UnityEngine.Vector2 point)
            {
                int col = end % board.Width, row = end / board.Width;
                for (int y = Math.Max(0, row - 1); y <= Math.Min(board.Height - 1, row + 1); ++y)
                for (int x = Math.Max(0, col - 1); x <= Math.Min(board.Width - 1, col + 1); ++x)
                {
                    int cell = y * board.Width + x;
                    if (survivors[cell] && Reaches(board, cell, point)) return true;
                }
                return false;
            }
        }

        private static bool Reaches(BoardState board, int cell, UnityEngine.Vector2 point) =>
            (board.Center(cell) - point).sqrMagnitude <= board.Pitch * board.Pitch * 1.24f;

        private static bool HasSideExit(BoardLayout layout, int tile, bool horizontal, bool[] survivors)
        {
            int left = layout.Neighbor(tile, horizontal ? 0 : 1);
            int right = layout.Neighbor(tile, horizontal ? 3 : 2);
            return (left >= 0 && survivors[left]) || (right >= 0 && survivors[right]);
        }

        private static WaveMode ChooseMode(int seed, int advancedWavesSurvived, int level)
        {
            if (level >= 9) return advancedWavesSurvived % 3 == 0 ? WaveMode.Classic : WaveMode.Memory;
            if (level >= 5) return advancedWavesSurvived % 2 == 0 ? WaveMode.Classic : WaveMode.Memory;
            int group = advancedWavesSurvived / 3;
            int memorySlot = group == 0 ? 1 : (int)(Mix((uint)seed ^ Mix((uint)group)) % 3);
            return advancedWavesSurvived % 3 == memorySlot ? WaveMode.Memory : WaveMode.Classic;
        }

        private static int PatternSeed(BoardState board, int start, int level, int number, bool opening)
        {
            unchecked
            {
                // Retain the seeded patterns that ran on the original single-word boards.
                uint shape = (uint)(board.Width | (board.Height << 4) | (start << 8));
                if (board.Count > 64)
                    shape = Mix(Mix((uint)board.Width) ^ (uint)board.Height) ^ Mix((uint)start);
                else if (board.Width >= 16 || board.Height >= 16)
                    shape = 0x80000000u | (uint)(board.Width | (board.Height << 6) | (start << 12));
                uint value = Mix(shape);
                for (int first = 0; first < board.Count;)
                {
                    uint word = 0;
                    int length = Math.Min(32, board.Count - first);
                    for (int bit = 0; bit < length; ++bit)
                        if (board.IsIntact(first + bit)) word |= 1u << bit;
                    value = first < 64 ? value ^ Mix(word) : Mix(value ^ word ^ (uint)first);
                    first += length;
                }
                value ^= Mix((uint)level) ^ Mix((uint)number * 0x9e3779b9u);
                value ^= opening ? 0u : Mix((uint)board.Seed);
                for (int i = 0; i < board.Sliders.Count; ++i) value ^= Mix((uint)(board.Sliders[i].Tile + 1));
                return (int)Mix(value);
            }
        }

        // Explicit unsigned arithmetic gives the same shuffled board on Mono and IL2CPP.
        private static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }
        }

        internal struct Generator
        {
            private uint state;
            public Generator(int seed) { state = unchecked((uint)seed); }
            public int Next(int count)
            {
                unchecked { state += 0x9e3779b9u; }
                return (int)(Mix(state) % (uint)count);
            }
        }
    }
}
