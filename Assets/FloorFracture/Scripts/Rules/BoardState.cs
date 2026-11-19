using System;
using UnityEngine;

namespace FloorFracture
{
    public sealed class BoardState
    {
        public const float Pitch = 0.5f;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Count => Width * Height;
        public int Seed { get; private set; }
        public int WavesSurvived { get; private set; }
        public WavePlan ActiveWave { get; private set; }
        public SlidingPane Slider { get; private set; }
        public TileSample CurrentSample => tracker.Current;
        public bool BreaksApplied { get; private set; }
        internal int StationaryMask { get; private set; }
        internal int FullMask => (1 << Count) - 1;

        private readonly PlayerTileTracker tracker = new PlayerTileTracker();
        private readonly int[] neighborMasks = new int[16];
        private int regularWavesSurvived;
        private bool levelEnded;

        public BoardState(int width, int height, int seed) { Reset(width, height, seed); }

        public void Reset(int width, int height, int seed)
        {
            if (width < 2 || width > 4) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 2 || height > 4) throw new ArgumentOutOfRangeException(nameof(height));
            Width = width;
            Height = height;
            Seed = seed;
            WavesSurvived = 0;
            regularWavesSurvived = 0;
            for (int tile = 0; tile < Count; ++tile)
            {
                int neighbors = 0;
                if (tile % Width > 0) neighbors |= 1 << (tile - 1);
                if (tile % Width < Width - 1) neighbors |= 1 << (tile + 1);
                if (tile >= Width) neighbors |= 1 << (tile - Width);
                if (tile < Count - Width) neighbors |= 1 << (tile + Width);
                neighborMasks[tile] = neighbors;
            }
            RebuildLevel();
        }

        public void RebuildLevel()
        {
            StationaryMask = FullMask;
            ActiveWave = null;
            Slider = null;
            levelEnded = false;
            BreaksApplied = false;
            tracker.Reset();
        }

        public bool IsIntact(int index) =>
            index >= 0 && index < Count && (StationaryMask & (1 << index)) != 0;

        public Vector2 Center(int index)
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            return new Vector2((index % Width + 0.5f - Width * 0.5f) * Pitch,
                (index / Width + 0.5f - Height * 0.5f) * Pitch);
        }

        public int IndexAt(Vector2 localXZ)
        {
            int x = CellOnAxis(localXZ.x, Width), z = CellOnAxis(localXZ.y, Height);
            return x < 0 || z < 0 ? -1 : z * Width + x;
        }

        private static int CellOnAxis(float value, int length)
        {
            float extent = length * Pitch * 0.5f;
            if (!(value >= -extent && value < extent)) return -1;
            // Comparing the input directly to exact half-metre boundaries preserves even
            // the smallest representable displacement from a seam at zero.
            for (int i = 1; i < length; ++i)
                if (value < (i - length * 0.5f) * Pitch) return i - 1;
            return length - 1;
        }

        internal int NeighborMask(int tile) => neighborMasks[tile];

        internal int AdjacentMask(int group)
        {
            int adjacent = 0;
            for (int tile = 0; group != 0; ++tile, group >>= 1)
                if ((group & 1) != 0) adjacent |= neighborMasks[tile];
            return adjacent;
        }

        internal bool Neighbors(int a, int b) => (neighborMasks[a] & (1 << b)) != 0;

        public TileSample Sample(Vector2 localXZ, bool tracked, double now)
        {
            CheckTime(now);
            return tracker.Sample(this, localXZ, tracked, now);
        }

        public WavePlan BeginWave(int startingTile, int level, int waveNumber, double now)
        {
            CheckTime(now);
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            if (waveNumber < 1) throw new ArgumentOutOfRangeException(nameof(waveNumber));
            if (ActiveWave != null || levelEnded)
                throw new InvalidOperationException("A new wave requires a ready, unfinished board.");
            if (!IsIntact(startingTile) && !(Slider != null && Slider.Locked && Slider.Tile == startingTile))
                throw new ArgumentException("The start must be supported by glass.", nameof(startingTile));

            var plan = WavePlanner.Plan(this, startingTile, level, waveNumber, regularWavesSurvived, now);
            if (plan.NewSliderTile >= 0)
                Slider = new SlidingPane(plan.NewSliderTile, Center(plan.NewSliderTile), plan.SlideDirection, now);
            ActiveWave = plan;
            BreaksApplied = false;
            tracker.ResetTiming();
            return plan;
        }

        public void Pause()
        {
            ActiveWave = null;
            if (Slider != null && !Slider.Locked) Slider = null;
            tracker.ResetTiming();
        }

        // Consume each result once. GameFlow supplies an unscaled monotonic timestamp and handles
        // the resulting phase change. Scheduled breaks commit together only at the wave deadline.
        public WaveResult Tick(Vector2 localXZ, bool tracked, double now)
        {
            WavePlan wave = ActiveWave;
            if (wave == null) return WaveResult.Pending;
            CheckTime(now);
            if (now < wave.StartsAt) throw new ArgumentOutOfRangeException(nameof(now));
            if (!tracked || IndexAt(localXZ) < 0)
            {
                tracker.Sample(this, localXZ, tracked, now);
                Pause();
                return WaveResult.Paused;
            }

            bool atDeadline = now >= wave.Deadline;
            double sampleTime = atDeadline ? wave.Deadline : now;
            Vector2 point = atDeadline ? tracker.PoseAtDeadline(localXZ, now, wave.Deadline) : localXZ;
            TileSample sample = tracker.Sample(this, point, true, sampleTime);
            bool graceExpired = tracker.GraceExpired(sampleTime);
            if (graceExpired && (!atDeadline || tracker.GapDeadline < wave.Deadline))
            {
                ActiveWave = null;
                levelEnded = true;
                return WaveResult.Lost;
            }
            if (!atDeadline) return WaveResult.Pending;

            bool survived = !graceExpired && sample.Kind == SupportKind.Glass && wave.IsSafe(sample.Index);
            StationaryMask = wave.StationaryMask;
            if (Slider != null && (!Slider.Locked || wave.IsBreaking(Slider.Tile))) Slider = null;
            BreaksApplied = true;
            ActiveWave = null;
            if (!survived)
            {
                levelEnded = true;
                return WaveResult.Lost;
            }
            ++WavesSurvived;
            if (wave.Mode == WaveMode.LastTile)
            {
                levelEnded = true;
                return WaveResult.LevelCleared;
            }
            ++regularWavesSurvived;
            return WaveResult.Survived;
        }

        private static void CheckTime(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now));
        }
    }
}
