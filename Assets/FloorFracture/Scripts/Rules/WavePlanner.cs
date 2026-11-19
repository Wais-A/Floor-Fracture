using System;
using System.Collections.Generic;
using UnityEngine;

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
        internal int StationaryMask { get; }
        internal int NewSliderTile { get; }
        internal Vector2 SlideDirection { get; }

        private readonly int breakingMask;
        private readonly int safeMask;

        internal WavePlan(BoardState board, int level, int number, int seed, int start, int guaranteed,
            int stationary, int newSlider, Vector2 slideDirection, WaveMode mode, double now)
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
            SlideDirection = slideDirection;

            // An earlier, locked pane can break over its old hole. A new optional pane is
            // separate from the permanent survivors, so its underlying hole never fills in.
            breakingMask = board.StationaryMask & ~stationary;
            if (board.Slider != null) breakingMask |= 1 << board.Slider.Tile;
            safeMask = stationary;
            if (newSlider >= 0) safeMask |= 1 << newSlider;
            StationarySafe = CellList(stationary, board.Count);
            SafeTiles = newSlider < 0 ? StationarySafe : CellList(safeMask, board.Count);
            BreakingTiles = CellList(breakingMask, board.Count);
        }

        public bool IsBreaking(int tile) => Contains(breakingMask, tile);
        public bool IsSafe(int tile) => Contains(safeMask, tile);
        public bool ShowsWarnings(double now) =>
            now >= StartsAt && now <= Deadline && (Mode != WaveMode.Memory || now < RevealEndsAt);

        private static bool Contains(int mask, int index)
        {
            return index >= 0 && index < 16 && (mask & (1 << index)) != 0;
        }

        private static IReadOnlyList<int> CellList(int mask, int count)
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
            int eligible = board.StationaryMask & ~(1 << start);
            int adjacent = board.NeighborMask(start) & eligible;
            int visited = 0, options = 0, largest = 0;
            for (int tile = 0; tile < board.Count; ++tile)
            {
                int bit = 1 << tile;
                if ((adjacent & ~visited & bit) == 0) continue;
                int component = Flood(board, bit, eligible);
                visited |= component;
                int size = CountBits(component);
                if (size < largest) continue;
                if (size > largest) options = 0;
                largest = size;
                options |= component & adjacent;
            }
            if (options == 0) throw new InvalidOperationException("The starting pane has no stationary step.");
            int guaranteed = Pick(options, ref random, opening);
            int componentMask = Flood(board, 1 << guaranteed, eligible);
            int intact = CountBits(board.StationaryMask);
            int maximum = Math.Min(largest, Math.Max(1, intact - 1));
            int minimum = Math.Min(maximum, Math.Max(2, (intact + 1) / 2));
            int target = opening
                ? Math.Min(maximum, (2 * intact + 2) / 3)
                : minimum + random.Next(maximum - minimum + 1);

            int survivors = 1 << guaranteed;
            for (int size = 1; size < target; ++size)
            {
                int frontier = board.AdjacentMask(survivors) & componentMask & ~survivors;
                if (frontier == 0)
                {
                    // A new constraint that exhausts the candidate set still leaves a fair finale.
                    survivors = 1 << guaranteed;
                    break;
                }
                survivors |= 1 << Pick(frontier, ref random, opening);
            }

            WaveMode mode = survivors == (1 << guaranteed)
                ? WaveMode.LastTile : ChooseMode(board.Seed, regularWavesSurvived);
            int sliderTile = -1;
            Vector2 slideDirection = Vector2.zero;
            if (mode != WaveMode.LastTile && level >= 3 && board.Slider == null)
            {
                // Only use old holes, and only cells sharing an edge with a permanent survivor.
                int candidates = board.AdjacentMask(survivors) & board.FullMask & ~board.StationaryMask;
                if (candidates != 0)
                {
                    sliderTile = Pick(candidates, ref random, false);
                    slideDirection = random.Next(2) == 0 ? Vector2.right : Vector2.up;
                    if (random.Next(2) == 0) slideDirection = -slideDirection;
                }
            }
            return new WavePlan(board, level, number, seed, start, guaranteed, survivors,
                sliderTile, slideDirection, mode, now);
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
                uint value = Mix(shape) ^ Mix((uint)board.StationaryMask);
                value ^= Mix((uint)level) ^ Mix((uint)number * 0x9e3779b9u);
                value ^= opening ? 0u : Mix((uint)board.Seed);
                if (board.Slider != null) value ^= Mix((uint)(board.Slider.Tile + 1));
                return (int)Mix(value);
            }
        }

        private static int Flood(BoardState board, int seed, int allowed)
        {
            int group = seed, frontier = seed;
            while (frontier != 0)
            {
                frontier = board.AdjacentMask(frontier) & allowed & ~group;
                group |= frontier;
            }
            return group;
        }

        private static int CountBits(int mask)
        {
            int count = 0;
            while (mask != 0)
            {
                mask &= mask - 1;
                ++count;
            }
            return count;
        }

        private static int Pick(int mask, ref Generator random, bool first)
        {
            int skip = first ? 0 : random.Next(CountBits(mask));
            for (int tile = 0; tile < 16; ++tile)
                if ((mask & (1 << tile)) != 0 && skip-- == 0) return tile;
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
