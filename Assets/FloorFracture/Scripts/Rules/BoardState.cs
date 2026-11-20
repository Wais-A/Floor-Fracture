using System;
using UnityEngine;

namespace FloorFracture
{
    public enum WaveLossReason { None, GapGraceExpired, GapAtZero, CrackedPane, MemoryPane, MovingPane }

    public sealed class BoardState
    {
        public const int MinAxis = 2, MaxPanes = 64, MaxAxis = MaxPanes / MinAxis;
        public const float CompactMinPitch = 0.40f, MinPitch = 0.50f, MaxPitch = 0.67f;
        public const float PaneHalfPitch = .484f;
        public float Pitch { get; private set; }
        public BoardLayout Layout { get; private set; }
        public int Width => Layout.Columns;
        public int Height => Layout.Rows;
        public int Count => Width * Height;
        public int Seed { get; private set; }
        public int WavesSurvived { get; private set; }
        public WavePlan ActiveWave { get; private set; }
        public SlidingPane Slider { get; private set; }
        public TileSample CurrentSample => tracker.Current;
        public bool BreaksApplied { get; private set; }
        public WaveLossReason LastLoss { get; private set; }
        internal ulong StationaryMask { get; private set; }
        internal ulong FullMask => Layout.Mask;

        private readonly PlayerTileTracker tracker = new PlayerTileTracker();
        private readonly float[] columnEdges = new float[MaxPanes + 1], rowEdges = new float[MaxPanes + 1];
        private int regularWavesSurvived;
        private bool levelEnded;

        public BoardState(int width, int height, int seed, float pitch = MinPitch) { Reset(width, height, seed, pitch); }
        public BoardState(BoardLayout layout, int seed, float pitch) { Reset(layout, seed, pitch); }

        // Each pane occupies one bit. A long, narrow rectangle can use all 64 bits.
        public static int MaxRowsFor(int columns) =>
            columns >= MinAxis && columns <= MaxAxis ? MaxPanes / columns : 0;

        public void Reset(int width, int height, int seed, float pitch = MinPitch)
        {
            int maxRows = MaxRowsFor(width);
            if (maxRows == 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < MinAxis || height > maxRows) throw new ArgumentOutOfRangeException(nameof(height));
            if (!(pitch >= MinPitch && pitch <= MaxPitch)) throw new ArgumentOutOfRangeException(nameof(pitch));
            Reset(BoardLayout.Rectangle(width, height), seed, pitch);
        }

        private void Reset(BoardLayout layout, int seed, float pitch)
        {
            if (!layout.IsPlayable) throw new ArgumentException("The board needs at least two edge-connected panes.", nameof(layout));
            if (!(pitch >= CompactMinPitch && pitch <= MaxPitch)) throw new ArgumentOutOfRangeException(nameof(pitch));
            Layout = layout;
            Pitch = pitch;
            Seed = seed;
            WavesSurvived = 0;
            regularWavesSurvived = 0;
            SetEdges(columnEdges, Width, pitch);
            SetEdges(rowEdges, Height, pitch);
            RebuildLevel();
        }

        public void RebuildLevel()
        {
            StationaryMask = FullMask;
            ActiveWave = null;
            Slider = null;
            levelEnded = false;
            BreaksApplied = false;
            LastLoss = WaveLossReason.None;
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
            return Layout.HasPane(x, z) ? z * Width + x : -1;
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

        internal ulong SideNeighbors(int tile, bool horizontalLane)
        {
            ulong bit = 1UL << tile;
            ulong vertical = (bit >> Width) | (bit << Width);
            return NeighborMask(tile) & (horizontalLane ? vertical : ~vertical);
        }

        public bool CanStepFrom(int from, int to) =>
            (uint)to < (uint)Count && (uint)from < (uint)Count && (StepMask(from) & (1UL << to)) != 0;

        internal ulong StepMask(int from) =>
            Slider != null && Slider.Locked && from == Slider.Tile ? Slider.ExitMask(this) : NeighborMask(from);

        internal ulong AdjacentMask(ulong group) => Layout.Adjacent(group);

        public bool Neighbors(int a, int b) => (uint)a < (uint)Count && (uint)b < (uint)Count && (NeighborMask(a) & (1UL << b)) != 0;

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
                Slider = new SlidingPane(this, plan.NewSliderTile, plan.OtherSliderHole, now);
            ActiveWave = plan;
            BreaksApplied = false;
            tracker.ResetTiming();
            return plan;
        }

        public void Pause()
        {
            // The optional pane belongs to the wave that introduced it until that wave resolves.
            if (ActiveWave != null && ActiveWave.NewSliderTile >= 0) Slider = null;
            ActiveWave = null;
            tracker.Reset();
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
                Pause();
                tracker.Sample(this, localXZ, tracked, now);
                return WaveResult.Paused;
            }

            bool atDeadline = now >= wave.Deadline;
            double sampleTime = atDeadline ? wave.Deadline : now;
            double gapDeadline = tracker.GapDeadline;
            if (gapDeadline <= sampleTime && gapDeadline < wave.Deadline)
            {
                Vector2 atExpiry = tracker.PoseAtDeadline(localXZ, now, gapDeadline);
                if (tracker.Sample(this, atExpiry, true, gapDeadline).Kind == SupportKind.Gap)
                {
                    ActiveWave = null;
                    levelEnded = true;
                    LastLoss = WaveLossReason.GapGraceExpired;
                    return WaveResult.Lost;
                }
                tracker.UpdateGap(gapDeadline);
            }
            Vector2 point = atDeadline ? tracker.PoseAtDeadline(localXZ, now, wave.Deadline) : localXZ;
            TileSample sample = tracker.Sample(this, point, true, sampleTime);
            tracker.UpdateGap(sampleTime);
            if (!atDeadline) return WaveResult.Pending;

            bool survived = sample.Kind == SupportKind.Glass && wave.IsSafe(sample.Index);
            StationaryMask = wave.StationaryMask;
            if (Slider != null && wave.IsBreaking(Slider.Tile)) Slider = null;
            BreaksApplied = true;
            ActiveWave = null;
            if (!survived)
            {
                levelEnded = true;
                LastLoss = sample.Kind == SupportKind.Gap ? WaveLossReason.GapAtZero :
                    sample.IsSlider ? WaveLossReason.MovingPane :
                    wave.Mode == WaveMode.Memory ? WaveLossReason.MemoryPane : WaveLossReason.CrackedPane;
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
