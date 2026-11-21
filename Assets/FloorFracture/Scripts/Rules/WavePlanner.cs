using System;
using System.Collections.Generic;

namespace FloorFracture
{
    public enum WaveMode { Classic, Memory, LastTile }
    public enum WaveResult { Pending, Paused, Survived, LevelCleared, Lost }

    public sealed class WavePlan
    {
        public WaveMode Mode { get; }
        public int Level { get; }
        public int Number { get; }
        public int Seed { get; }
        public int StartingTile { get; }
        public int GuaranteedTile { get; }
        public double StartsAt { get; }
        public double Duration { get; }
        public double Deadline => StartsAt + Duration;
        public double RevealEndsAt { get; }
        public bool Fog { get; }
        public IReadOnlyList<int> BreakingTiles { get; }
        public IReadOnlyList<int> SafeTiles { get; }
        public IReadOnlyList<int> StationarySafe { get; }
        internal bool[] Stationary { get; }
        internal int NewSliderTile { get; }
        internal int OtherSliderHole { get; }

        private readonly bool[] breaking;

        internal WavePlan(BoardState board, int level, int number, int seed, int start, int guaranteed,
            bool[] stationary, int newSlider, int otherSliderHole, WaveMode mode, double now)
        {
            Level = level;
            Number = number;
            Seed = seed;
            StartingTile = start;
            GuaranteedTile = guaranteed;
            StartsAt = now;
            Duration = Math.Max(2.5, 4.0 - (level - 1) * .3);
            double reveal = level == 1 ? 1.5 : Math.Max(.85, 1.25 - (level - 2) * .15);
            RevealEndsAt = now + (mode == WaveMode.Memory ? Math.Max(.75, reveal - .15) : reveal);
            Fog = level >= 2 && (number - 1) % 3 == 0 && mode != WaveMode.LastTile;
            Mode = mode;
            Stationary = stationary;
            NewSliderTile = newSlider;
            OtherSliderHole = otherSliderHole;

            // A previous slider breaks over its old hole. The newly offered slider is
            // an additional safe choice and never fills in its underlying hole.
            breaking = new bool[board.Count];
            var survivors = new List<int>();
            var allSafe = newSlider < 0 ? null : new List<int>();
            var breaks = new List<int>();
            for (int tile = 0; tile < board.Count; ++tile)
            {
                if (stationary[tile]) survivors.Add(tile);
                if (IsSafe(tile)) allSafe?.Add(tile);
                else if (board.IsIntact(tile) || board.Slider?.Tile == tile)
                {
                    breaking[tile] = true;
                    breaks.Add(tile);
                }
            }
            StationarySafe = survivors.AsReadOnly();
            SafeTiles = allSafe == null ? StationarySafe : allSafe.AsReadOnly();
            BreakingTiles = breaks.AsReadOnly();
        }

        public bool IsBreaking(int tile) => (uint)tile < (uint)breaking.Length && breaking[tile];
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
            int largest = 0;
            for (int tile = 0; tile < board.Count; ++tile)
            {
                if (!board.IsIntact(tile) || !board.CanStepFrom(start, tile)) continue;
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
            int minimum = Math.Min(maximum, Math.Max(2, intact / 2 + intact % 2));
            int target = opening
                ? Math.Min(maximum, intact - intact / 3)
                : minimum + random.Next(maximum - minimum + 1);
            var survivors = GrowSurvivors(board.Layout, guaranteed, target, componentSizes, opening, ref random);

            WaveMode mode = target == 1 ? WaveMode.LastTile :
                level == 1 ? WaveMode.Classic : ChooseMode(board.Seed, advancedWavesSurvived);
            int sliderTile = -1, otherHole = -1;
            if (mode != WaveMode.LastTile && level >= 3 && board.Slider == null)
                ChooseSlider(board, survivors, ref random, out sliderTile, out otherHole);
            return new WavePlan(board, level, number, seed, start, guaranteed, survivors,
                sliderTile, otherHole, mode, now);
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
                    if (next < 0 || available[next] == 0) continue;
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

        // Each group of three regular waves from level two onward contains one Memory round.
        private static WaveMode ChooseMode(int seed, int advancedWavesSurvived)
        {
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
