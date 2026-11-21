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
        public int JumpGap { get; }
        public bool IsJump => JumpGap >= 0;
        public double StartsAt { get; }
        public double Duration { get; }
        public double Deadline => StartsAt + Duration;
        public double RevealEndsAt { get; }
        public bool Fog { get; }
        public double RecoverySeconds { get; }
        public IReadOnlyList<WaveBreakGroup> BreakGroups { get; }
        public IReadOnlyList<int> BreakingTiles { get; }
        public IReadOnlyList<int> SafeTiles { get; }
        public IReadOnlyList<int> StationarySafe { get; }
        internal bool[] Stationary { get; }
        internal int NewSliderTile { get; }
        internal int OtherSliderHole { get; }

        private readonly WaveBreakGroup[] groupByTile;

        internal WavePlan(BoardState board, int level, int number, int seed, int start, int guaranteed,
            bool[] stationary, int newSlider, int otherSliderHole, WaveMode mode, WavePacing pacing, double now,
            int jumpGap)
        {
            Level = level;
            Number = number;
            Seed = seed;
            StartingTile = start;
            GuaranteedTile = guaranteed;
            JumpGap = jumpGap;
            StartsAt = now;
            double switchSeconds = WavePacing.SwitchSeconds(level, number);
            Duration = IsJump ? Math.Max(switchSeconds, 1.85) : switchSeconds;
            RecoverySeconds = WavePacing.RecoveryAt(level);
            Fog = !IsJump && mode != WaveMode.LastTile && level >= 2 &&
                (level >= 9 ? number % 3 != 0 : (number - 1) % (level >= 5 ? 2 : 3) == 0);
            Mode = mode;
            Stationary = stationary;
            NewSliderTile = newSlider;
            OtherSliderHole = otherSliderHole;

            // A previous slider breaks over its old hole. The newly offered slider is
            // an additional safe choice and never fills in its underlying hole.
            groupByTile = new WaveBreakGroup[board.Count];
            var survivors = new List<int>();
            var allSafe = newSlider < 0 ? null : new List<int>();
            var breaks = new List<int>();
            for (int tile = 0; tile < board.Count; ++tile)
            {
                if (stationary[tile]) survivors.Add(tile);
                if (IsSafe(tile)) allSafe?.Add(tile);
                else if (board.IsIntact(tile) || board.Slider?.Tile == tile)
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
                var takeoff = new List<int>(8);
                var atZero = new List<int>();
                foreach (int tile in breaks)
                {
                    if (tile != StartingTile && WavePlanner.TouchesStart(board, StartingTile, tile))
                        takeoff.Add(tile);
                    else atZero.Add(tile);
                }
                var open = new WaveBreakGroup(takeoff, StartsAt, 0, StartsAt);
                var finish = new WaveBreakGroup(atZero, StartsAt, clueSeconds, Deadline);
                foreach (int tile in takeoff) groupByTile[tile] = open;
                foreach (int tile in atZero) groupByTile[tile] = finish;
                return Array.AsReadOnly(new[] { open, finish });
            }
            // Keep the player's pane for zero, even when the sweep starts on that side.
            bool sweep = Mode != WaveMode.LastTile && (Level >= 3 || Mode == WaveMode.Memory);
            int desired = !sweep ? 1 : Level < 7 ? 2 : 3;
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
                double revealStart = StartsAt + Math.Min(.12, Duration * .12) * i;
                double breakTime = StartsAt + Duration * WavePacing.BreakFraction(Level, i, count);
                var group = new WaveBreakGroup(tiles, revealStart, clueSeconds, breakTime);
                groups[i] = group;
                foreach (int tile in tiles) groupByTile[tile] = group;
                first += size;
            }
            return Array.AsReadOnly(groups);
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
        public bool IsSafe(int tile) =>
            (uint)tile < (uint)Stationary.Length && (Stationary[tile] || tile == NewSliderTile);
        public bool ShowsWarnings(double now) => now >= StartsAt && now < RevealEndsAt;
    }

    public static class WavePlanner
    {
        internal static WavePlan Plan(BoardState board, int start, int level, int number,
            int advancedWavesSurvived, double now)
        {
            bool opening = level == 1 && number == 1;
            int seed = PatternSeed(board, start, level, number, opening);
            var random = new Generator(seed);

            // Removing the player's pane can split the board. Only consider exits into
            // a largest reachable component, so the first step supports a viable continuation.
            var componentSizes = new int[board.Count];
            var queue = new int[board.IntactCount];
            var options = new List<int>(8);
            var landings = new List<int>(4);
            // Introduce fog and sliding glass before jumps on their teaching levels.
            // From level four, check even the opening wave, while the most lanes are intact.
            bool jumpsUnlocked = level >= 4 || (level >= 2 && number >= level);
            if (jumpsUnlocked && !board.LastWaveWasJump && board.Slider == null)
            {
                for (int direction = 0; direction < 4; direction++)
                {
                    int gap = board.Layout.Neighbor(start, direction);
                    if (!board.Layout.Contains(gap)) continue;
                    int landing = board.Layout.Neighbor(gap, direction);
                    if (board.IsIntact(landing)) landings.Add(landing);
                }
            }
            bool jump = landings.Count > 0;
            if (jump)
            {
                // Exclude the whole ring, including corners, from the surviving component.
                // A missing scan cell can never be the gap: all three cells were fitted.
                for (int tile = 0; tile < board.Count; tile++)
                    if (TouchesStart(board, start, tile)) componentSizes[tile] = -1;
            }
            int largest = 0;
            for (int tile = 0; tile < board.Count; ++tile)
            {
                if (!board.IsIntact(tile) || !(jump ? landings.Contains(tile) : board.CanStepFrom(start, tile))) continue;
                int size = componentSizes[tile];
                if (size == 0) size = MeasureComponent(board, start, tile, queue, componentSizes);
                if (size < largest) continue;
                if (size > largest) options.Clear();
                largest = size;
                options.Add(tile);
            }
            if (options.Count == 0) throw new InvalidOperationException("The starting pane has no stationary step.");
            int guaranteed = options[opening ? 0 : random.Next(options.Count)];
            int intact = board.IntactCount;
            int maximum = Math.Min(largest, Math.Max(1, intact - 1));
            var pacing = WavePacing.At(level);
            int target = Math.Min(maximum, Math.Max(1, pacing.TargetSurvivors(intact)));
            var survivors = GrowSurvivors(board.Layout, guaranteed, target, componentSizes, opening, ref random);

            WaveMode mode = target == 1 ? WaveMode.LastTile : jump ? WaveMode.Jump :
                level == 1 ? WaveMode.Classic : ChooseMode(board.Seed, advancedWavesSurvived, level);
            int sliderTile = -1, otherHole = -1;
            if (!jump && mode != WaveMode.LastTile && level >= 3 && board.Slider == null)
                ChooseSlider(board, survivors, ref random, out sliderTile, out otherHole);
            return new WavePlan(board, level, number, seed, start, guaranteed, survivors,
                sliderTile, otherHole, mode, pacing, now, jump ? start + (guaranteed - start) / 2 : -1);
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

        private static void ChooseSlider(BoardState board, bool[] survivors, ref Generator random,
            out int sliderTile, out int otherHole)
        {
            sliderTile = otherHole = -1;
            int lanes = 0;
            for (int a = 0; a < board.Count; ++a)
            {
                if (!board.Layout.Contains(a) || board.IsIntact(a)) continue;
                // Enumerate each pair of old holes once, in row-major order.
                for (int direction = 2; direction <= 3; ++direction)
                {
                    int b = board.Layout.Neighbor(a, direction);
                    if (!board.Layout.Contains(b) || board.IsIntact(b)) continue;
                    bool horizontal = direction == 2;
                    if (!HasSideExit(board.Layout, a, horizontal, survivors) ||
                        !HasSideExit(board.Layout, b, horizontal, survivors)) continue;
                    if (random.Next(++lanes) != 0) continue;
                    bool reverse = random.Next(2) != 0;
                    sliderTile = reverse ? b : a;
                    otherHole = reverse ? a : b;
                }
            }
        }

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
                if (board.Slider != null) value ^= Mix((uint)(board.Slider.Tile + 1));
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

        private struct Generator
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
