using System;
using UnityEngine;

namespace FloorFracture
{
    public sealed class BoardState
    {
        public const int MinAxis = 2, MaxAxis = 8;
        public const float MinPitch = 0.50f, MaxPitch = 0.67f;
        public float Pitch { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Count => Width * Height;
        public int Seed { get; private set; }
        public int WavesSurvived { get; private set; }
        public WavePlan ActiveWave { get; private set; }
        public SlidingPane Slider { get; private set; }
        public TileSample CurrentSample => tracker.Current;
        public bool BreaksApplied { get; private set; }
        internal ulong StationaryMask { get; private set; }
        internal ulong FullMask => ulong.MaxValue >> (64 - Count);

        private readonly PlayerTileTracker tracker = new PlayerTileTracker();
        private readonly float[] columnEdges = new float[MaxAxis + 1], rowEdges = new float[MaxAxis + 1];
        private ulong leftColumn, rightColumn;
        private int regularWavesSurvived;
        private bool levelEnded;

        public BoardState(int width, int height, int seed, float pitch = MinPitch) { Reset(width, height, seed, pitch); }

        public void Reset(int width, int height, int seed, float pitch = MinPitch)
        {
            if (width < MinAxis || width > MaxAxis) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < MinAxis || height > MaxAxis) throw new ArgumentOutOfRangeException(nameof(height));
            if (!(pitch >= MinPitch && pitch <= MaxPitch)) throw new ArgumentOutOfRangeException(nameof(pitch));
            Width = width;
            Height = height;
            Pitch = pitch;
            Seed = seed;
            WavesSurvived = 0;
            regularWavesSurvived = 0;
            SetEdges(columnEdges, width, pitch);
            SetEdges(rowEdges, height, pitch);
            leftColumn = 0;
            for (int row = 0; row < height; ++row) leftColumn |= 1UL << (row * width);
            rightColumn = leftColumn << (width - 1);
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
            index >= 0 && index < Count && (StationaryMask & (1UL << index)) != 0;

        public Vector2 Center(int index)
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            return new Vector2((index % Width + 0.5f - Width * 0.5f) * Pitch,
                (index / Width + 0.5f - Height * 0.5f) * Pitch);
        }

        public int IndexAt(Vector2 localXZ)
        {
            int x = CellOnAxis(localXZ.x, columnEdges, Width), z = CellOnAxis(localXZ.y, rowEdges, Height);
            return x < 0 || z < 0 ? -1 : z * Width + x;
        }

        private static void SetEdges(float[] edges, int length, float pitch)
        {
            // Store seam coordinates at the same single precision as tracked Vector2
            // poses. Some Mono builds retain extra precision in an inline comparison.
            for (int i = 0; i <= length; ++i) edges[i] = (i - length * 0.5f) * pitch;
        }

        private static int CellOnAxis(float value, float[] edges, int length)
        {
            if (!(value >= edges[0] && value < edges[length])) return -1;
            for (int i = 1; i < length; ++i)
                if (value < edges[i]) return i - 1;
            return length - 1;
        }

        internal ulong NeighborMask(int tile) => AdjacentMask(1UL << tile);

        internal ulong AdjacentMask(ulong group)
        {
            ulong horizontal = ((group & ~leftColumn) >> 1) | ((group & ~rightColumn) << 1);
            return (horizontal | (group >> Width) | (group << Width)) & FullMask;
        }

        internal bool Neighbors(int a, int b) => (NeighborMask(a) & (1UL << b)) != 0;

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
                Slider = new SlidingPane(plan.NewSliderTile, Center(plan.NewSliderTile), plan.SlideDirection, Pitch, now);
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
