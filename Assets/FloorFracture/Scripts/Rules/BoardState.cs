using System;
using UnityEngine;

namespace FloorFracture
{
    public enum WaveLossReason { None, GapGraceExpired, GapAtZero, CrackedPane, MemoryPane, MovingPane }

    public sealed class BoardState
    {
        public const int MinAxis = 2, MaxAxis = int.MaxValue / MinAxis;
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
        public int AppliedBreakGroupCount { get; private set; }
        public WaveLossReason LastLoss { get; private set; }
        internal int IntactCount { get; private set; }
        private bool[] stationary;
        private bool[] waveStartStationary;
        private int waveStartIntactCount;
        private SlidingPane waveStartSlider;
        private bool sliderWasLocked;

        private readonly PlayerTileTracker tracker = new PlayerTileTracker();
        private float[] columnEdges, rowEdges;
        private int advancedWavesSurvived;
        internal bool LastWaveWasJump { get; private set; }
        private bool levelEnded;

        public BoardState(int width, int height, int seed, float pitch = MinPitch) { Reset(width, height, seed, pitch); }
        public BoardState(BoardLayout layout, int seed, float pitch) { Reset(layout, seed, pitch); }

        // A row-major tile index and its rectangular cell count must both fit in an int.
        public static int MaxRowsFor(int columns) =>
            columns >= MinAxis && columns <= MaxAxis ? int.MaxValue / columns : 0;

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
            advancedWavesSurvived = 0;
            columnEdges = Edges(Width, pitch);
            rowEdges = Edges(Height, pitch);
            RebuildLevel();
        }

        public void RebuildLevel()
        {
            stationary = null;
            waveStartStationary = null;
            waveStartSlider = null;
            AppliedBreakGroupCount = 0;
            IntactCount = Layout.PaneCount;
            ActiveWave = null;
            Slider = null;
            LastWaveWasJump = false;
            levelEnded = false;
            BreaksApplied = false;
            LastLoss = WaveLossReason.None;
            tracker.Reset();
        }

        public bool IsIntact(int index) =>
            stationary == null ? Layout.Contains(index) : (uint)index < (uint)stationary.Length && stationary[index];

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

        private static float[] Edges(int length, float pitch)
        {
            // Store seam coordinates at the same single precision as tracked Vector2
            // poses. Some Mono builds retain extra precision in an inline comparison.
            var edges = new float[length + 1];
            for (int i = 0; i <= length; ++i) edges[i] = (i - length * 0.5f) * pitch;
            return edges;
        }

        private static int CellOnAxis(float value, float[] edges, int length)
        {
            if (!(value >= edges[0] && value < edges[length])) return -1;
            int low = 0, high = length;
            while (high - low > 1)
            {
                int mid = low + (high - low) / 2;
                if (value < edges[mid]) high = mid;
                else low = mid;
            }
            return low;
        }

        public bool CanStepFrom(int from, int to) =>
            Slider != null && Slider.Locked && from == Slider.Tile ? Slider.CanExitTo(this, to) : Neighbors(from, to);

        public bool Neighbors(int a, int b) => Layout.Contains(a) && Layout.Contains(b) &&
            Math.Abs(a % Width - b % Width) + Math.Abs(a / Width - b / Width) == 1;

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

            var plan = WavePlanner.Plan(this, startingTile, level, waveNumber, advancedWavesSurvived, now);
            waveStartStationary = stationary;
            waveStartIntactCount = IntactCount;
            waveStartSlider = Slider;
            sliderWasLocked = Slider != null && Slider.Locked;
            AppliedBreakGroupCount = 0;
            if (plan.NewSliderTile >= 0)
                Slider = new SlidingPane(this, plan.NewSliderTile, plan.OtherSliderHole, now, plan.Duration);
            ActiveWave = plan;
            BreaksApplied = false;
            tracker.ResetTiming();
            return plan;
        }

        public void Pause()
        {
            if (ActiveWave != null)
            {
                stationary = waveStartStationary;
                IntactCount = waveStartIntactCount;
                Slider = waveStartSlider;
                Slider?.RestoreLock(sliderWasLocked);
                AppliedBreakGroupCount = 0;
                BreaksApplied = false;
            }
            ActiveWave = null;
            tracker.Reset();
        }

        // Resolve timer events against interpolated poses even when several events fall between frames.
        public WaveResult Tick(Vector2 localXZ, bool tracked, double now)
        {
            WavePlan wave = ActiveWave;
            if (wave == null) return WaveResult.Pending;
            CheckTime(now);
            if (now < wave.StartsAt) throw new ArgumentOutOfRangeException(nameof(now));
            if (!tracked || IndexAt(localXZ) < 0) return Interrupt(localXZ, tracked, now);

            double until = Math.Min(now, wave.Deadline);
            while (AppliedBreakGroupCount < wave.BreakGroups.Count)
            {
                var group = wave.BreakGroups[AppliedBreakGroupCount];
                if (group.BreaksAt >= wave.Deadline || group.BreaksAt > until) break;

                WaveResult grace = CheckGapExpiry(wave, localXZ, now, group.BreaksAt);
                if (grace != WaveResult.Pending) return grace;
                Vector2 atBreak = tracker.PoseAtDeadline(localXZ, now, group.BreaksAt);
                if (IndexAt(atBreak) < 0) return Interrupt(atBreak, true, group.BreaksAt);
                ApplyEarlyGroup(group);
                tracker.Sample(this, atBreak, true, group.BreaksAt);
                tracker.UpdateGap(group.BreaksAt);
            }

            WaveResult beforeSample = CheckGapExpiry(wave, localXZ, now, until);
            if (beforeSample != WaveResult.Pending) return beforeSample;
            Vector2 point = tracker.PoseAtDeadline(localXZ, now, until);
            if (IndexAt(point) < 0) return Interrupt(point, true, until);
            TileSample sample = tracker.Sample(this, point, true, until);
            tracker.UpdateGap(until);
            if (until < wave.Deadline) return WaveResult.Pending;

            bool survived = sample.Kind == SupportKind.Glass && wave.IsSafe(sample.Index);
            stationary = wave.Stationary;
            IntactCount = wave.StationarySafe.Count;
            if (Slider != null && wave.IsBreaking(Slider.Tile)) Slider = null;
            AppliedBreakGroupCount = wave.BreakGroups.Count;
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
            LastWaveWasJump = wave.IsJump;
            ++WavesSurvived;
            if (wave.Mode == WaveMode.LastTile)
            {
                levelEnded = true;
                return WaveResult.LevelCleared;
            }
            if (wave.Level >= 2 && !wave.IsJump) ++advancedWavesSurvived;
            return WaveResult.Survived;
        }

        private WaveResult Interrupt(Vector2 point, bool tracked, double time)
        {
            Pause();
            tracker.Sample(this, point, tracked, time);
            return WaveResult.Paused;
        }

        private WaveResult CheckGapExpiry(WavePlan wave, Vector2 point, double now, double until)
        {
            double expiry = tracker.GapDeadline;
            if (!(expiry <= until && expiry < wave.Deadline)) return WaveResult.Pending;
            Vector2 atExpiry = tracker.PoseAtDeadline(point, now, expiry);
            if (IndexAt(atExpiry) < 0) return Interrupt(atExpiry, true, expiry);
            if (tracker.Sample(this, atExpiry, true, expiry).Kind == SupportKind.Gap)
            {
                ActiveWave = null;
                levelEnded = true;
                LastLoss = WaveLossReason.GapGraceExpired;
                return WaveResult.Lost;
            }
            tracker.UpdateGap(expiry);
            return WaveResult.Pending;
        }

        private void ApplyEarlyGroup(WaveBreakGroup group)
        {
            // The first early collapse gets a writable copy. Tracking loss can then restore
            // the exact pre-wave footprint, including old holes, with a reference swap.
            if (AppliedBreakGroupCount == 0)
            {
                var intact = new bool[Count];
                for (int tile = 0; tile < Count; ++tile) intact[tile] = IsIntact(tile);
                stationary = intact;
            }
            foreach (int tile in group.Tiles)
            {
                if (stationary[tile])
                {
                    stationary[tile] = false;
                    --IntactCount;
                }
                if (Slider != null && Slider.Tile == tile) Slider = null;
            }
            ++AppliedBreakGroupCount;
        }

        private static void CheckTime(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now));
        }
    }
}
