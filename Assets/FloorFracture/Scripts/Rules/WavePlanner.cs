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
        internal ulong StationaryMask { get; }
        internal int NewSliderTile { get; }
        internal int OtherSliderHole { get; }

        private readonly ulong breakingMask;
        private readonly ulong safeMask;

        internal WavePlan(BoardState board, int level, int number, int seed, int start, int guaranteed,
            ulong stationary, int newSlider, int otherSliderHole, WaveMode mode, double now)
        {
            Level = level;
            Number = number;
            Seed = seed;
            StartingTile = start;
            GuaranteedTile = guaranteed;
            StartsAt = now;
            Duration = Math.Max(2.5, 5.0 - (level - 1) * 0.4);
            RevealEndsAt = now + Math.Min(1.65, Duration * 0.4);
            Fog = level >= 2 && (number & 1) == 1 && mode != WaveMode.LastTile;
            Mode = mode;
            StationaryMask = stationary;
            NewSliderTile = newSlider;
            OtherSliderHole = otherSliderHole;

            // An earlier, locked pane can break over its old hole. A new optional pane is
            // separate from the permanent survivors, so its underlying hole never fills in.
            breakingMask = board.StationaryMask & ~stationary;
            if (board.Slider != null) breakingMask |= 1UL << board.Slider.Tile;
            safeMask = stationary;
            if (newSlider >= 0) safeMask |= 1UL << newSlider;
            StationarySafe = CellList(stationary, board.Count);
            SafeTiles = newSlider < 0 ? StationarySafe : CellList(safeMask, board.Count);
            BreakingTiles = CellList(breakingMask, board.Count);
        }

        public bool IsBreaking(int tile) => Contains(breakingMask, tile);
        public bool IsSafe(int tile) => Contains(safeMask, tile);
        public bool ShowsWarnings(double now) =>
            now >= StartsAt && now <= Deadline && (Mode != WaveMode.Memory || now < RevealEndsAt);

        private static bool Contains(ulong mask, int index)
        {
            return index >= 0 && index < BoardState.MaxPanes && (mask & (1UL << index)) != 0;
        }

        private static IReadOnlyList<int> CellList(ulong mask, int count)
        {
            var cells = new List<int>();
            for (int i = 0; i < count; ++i)
                if (Contains(mask, i)) cells.Add(i);
            return cells.AsReadOnly();
        }
    }

    // The largest connected component adjacent to the start gives the player the longest
    // viable continuation. Grow survivors outward from the reserved first step, so each
    // intermediate choice is connected even if removing the starting tile splits the board.
    public static class WavePlanner
    {
        internal static WavePlan Plan(BoardState board, int start, int level, int number,
            int regularWavesSurvived, double now)
        {
            bool opening = level == 1 && number == 1;
            int seed = PatternSeed(board, start, level, number, opening);
            var random = new Generator(seed);
            ulong eligible = board.StationaryMask & ~(1UL << start);
            ulong adjacent = board.StepMask(start) & eligible;
            ulong visited = 0, options = 0;
            int largest = 0;
            for (int tile = 0; tile < board.Count; ++tile)
            {
                ulong bit = 1UL << tile;
                if ((adjacent & ~visited & bit) == 0) continue;
                ulong component = Flood(board, bit, eligible);
                visited |= component;
                int size = CountBits(component);
                if (size < largest) continue;
                if (size > largest) options = 0;
                largest = size;
                options |= component & adjacent;
            }
            if (options == 0) throw new InvalidOperationException("The starting pane has no stationary step.");
            int guaranteed = Pick(options, ref random, opening);
            ulong componentMask = Flood(board, 1UL << guaranteed, eligible);
            int intact = CountBits(board.StationaryMask);
            int maximum = Math.Min(largest, Math.Max(1, intact - 1));
            int minimum = Math.Min(maximum, Math.Max(2, (intact + 1) / 2));
            int target = opening
                ? Math.Min(maximum, (2 * intact + 2) / 3)
                : minimum + random.Next(maximum - minimum + 1);

            ulong survivors = 1UL << guaranteed;
            for (int size = 1; size < target; ++size)
            {
                ulong frontier = board.AdjacentMask(survivors) & componentMask & ~survivors;
                if (frontier == 0)
                {
                    // A new constraint that exhausts the candidate set still leaves a fair finale.
                    survivors = 1UL << guaranteed;
                    break;
                }
                survivors |= 1UL << Pick(frontier, ref random, opening);
            }

            WaveMode mode = survivors == (1UL << guaranteed)
                ? WaveMode.LastTile : ChooseMode(board.Seed, regularWavesSurvived);
            int sliderTile = -1;
            int otherHole = -1;
            if (mode != WaveMode.LastTile && level >= 3 && board.Slider == null)
            {
                // A full pane travels between two old holes. At each end, a permanent
                // side step must remain so a player can lock the pane anywhere along it.
                ulong holes = board.AdjacentMask(survivors) & ~board.StationaryMask;
                int lanes = 0;
                for (int a = 0; a < board.Count; ++a)
                {
                    if ((holes & (1UL<<a)) == 0) continue;
                    ulong adjacentHoles = board.NeighborMask(a) & holes;
                    for (int b = a+1; b < board.Count; ++b)
                    {
                        if ((adjacentHoles & (1UL<<b)) == 0) continue;
                        bool horizontal = a / board.Width == b / board.Width;
                        if ((board.SideNeighbors(a,horizontal) & survivors) == 0 ||
                            (board.SideNeighbors(b,horizontal) & survivors) == 0) continue;
                        if (random.Next(++lanes) != 0) continue;
                        bool reverse = random.Next(2) != 0;
                        sliderTile = reverse ? b : a;
                        otherHole = reverse ? a : b;
                    }
                }
            }
            return new WavePlan(board, level, number, seed, start, guaranteed, survivors,
                sliderTile, otherHole, mode, now);
        }

        private static WaveMode ChooseMode(int seed, int regularWavesSurvived)
        {
            if (regularWavesSurvived == 0) return WaveMode.Classic;
            if (regularWavesSurvived == 1) return WaveMode.Memory;
            int slot = regularWavesSurvived - 2;
            uint bag = Mix(unchecked((uint)seed) ^ Mix((uint)(slot / 2 + 1)));
            bool memory = ((bag ^ (uint)slot) & 1) != 0;
            return memory ? WaveMode.Memory : WaveMode.Classic;
        }

        private static int PatternSeed(BoardState board, int start, int level, int number, bool opening)
        {
            unchecked
            {
                uint shape = (uint)(board.Width | (board.Height << 4) | (start << 8));
                // Preserve the existing seeds where each dimension fits in four bits.
                // Tag larger dimensions and give each its own six-bit field.
                if (board.Width >= 16 || board.Height >= 16)
                    shape = 0x80000000u | (uint)(board.Width | (board.Height << 6) | (start << 12));
                uint value = Mix(shape) ^ Mix((uint)board.StationaryMask) ^ Mix((uint)(board.StationaryMask >> 32));
                value ^= Mix((uint)level) ^ Mix((uint)number * 0x9e3779b9u);
                value ^= opening ? 0u : Mix((uint)board.Seed);
                if (board.Slider != null) value ^= Mix((uint)(board.Slider.Tile + 1));
                return (int)Mix(value);
            }
        }

        private static ulong Flood(BoardState board, ulong seed, ulong allowed)
        {
            ulong group = seed, frontier = seed;
            while (frontier != 0)
            {
                frontier = board.AdjacentMask(frontier) & allowed & ~group;
                group |= frontier;
            }
            return group;
        }

        private static int CountBits(ulong mask)
        {
            int count = 0;
            while (mask != 0)
            {
                mask &= mask - 1;
                ++count;
            }
            return count;
        }

        private static int Pick(ulong mask, ref Generator random, bool first)
        {
            int skip = first ? 0 : random.Next(CountBits(mask));
            for (int tile = 0; mask != 0; ++tile, mask >>= 1)
                if ((mask & 1) != 0 && skip-- == 0) return tile;
            throw new InvalidOperationException("Cannot select a tile from an empty pattern.");
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
