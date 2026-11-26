using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("FloorFracture.Rules.EditorTests")]

namespace FloorFracture
{
    public enum SupportKind { Untracked, Outside, Gap, Glass }

    public readonly struct TileSample
    {
        public SupportKind Kind { get; }
        public int Index { get; }
        public bool IsSlider { get; }
        public Vector2 Stance { get; }

        internal TileSample(SupportKind kind, int index = -1, bool isSlider = false, Vector2 stance = default)
        {
            Kind = kind;
            Index = index;
            IsSlider = isSlider;
            Stance = stance;
        }
    }

    public sealed class SlidingPane
    {
        public float HalfSize { get; }
        public int Tile { get; }
        public int OtherHole { get; }
        public Vector2 Direction { get; }
        public int TravelCells { get; }
        // Edge-adjacent cells from Tile to OtherHole; a patrol turns corners.
        public IReadOnlyList<int> Path { get; }
        // Tiles the planner kept intact this wave so every point on the patrol has an exit.
        internal int[] PromotedExits { get; set; } = Array.Empty<int>();
        public double MotionStartsAt { get; }
        public bool Locked { get; private set; }

        private readonly Vector2[] centers;
        private Vector2 lockedCenter;
        public double OneWaySeconds { get; }

        internal SlidingPane(BoardState board, int tile, int otherHole, double start, double duration)
            : this(board, Lane(board, tile, otherHole), start, duration) { }

        internal SlidingPane(BoardState board, IReadOnlyList<int> path, double start, double duration)
        {
            Path = path.ToArray();
            Tile = path[0];
            OtherHole = path[path.Count - 1];
            TravelCells = path.Count - 1;
            centers = path.Select(board.Center).ToArray();
            Direction = (centers[1] - centers[0]).normalized;
            HalfSize = board.Pitch * BoardState.PaneHalfPitch;
            double pace = Math.Min(1, duration / 2);
            MotionStartsAt = start + .65 * pace;
            OneWaySeconds = TravelCells * 1.20 * pace;
        }

        private static int[] Lane(BoardState board, int tile, int otherHole)
        {
            int cells = Math.Abs(otherHole % board.Width - tile % board.Width) +
                Math.Abs(otherHole / board.Width - tile / board.Width);
            int stride = (otherHole - tile) / cells;
            return Enumerable.Range(0, cells + 1).Select(i => tile + i * stride).ToArray();
        }

        public Vector2 CenterAt(double now)
        {
            if (Locked) return lockedCenter;
            double phase = (Math.Max(0, now - MotionStartsAt) / OneWaySeconds) % 2.0;
            // Every segment is one pitch long, so distance along the path is measured in cells.
            double along = (phase <= 1 ? phase : 2 - phase) * TravelCells;
            int cell = Math.Min((int)along, TravelCells - 1);
            return Vector2.LerpUnclamped(centers[cell], centers[cell + 1], (float)(along - cell));
        }

        internal void RestoreLock(bool wasLocked) => Locked = wasLocked;

        internal bool CanExitTo(BoardState board, int tile)
        {
            // At mid-travel an exit can be half a pitch along the lane and one pitch across.
            if (!board.IsIntact(tile)) return false;
            Vector2 delta = board.Center(tile) - lockedCenter;
            return delta.sqrMagnitude <= board.Pitch * board.Pitch * 1.2501f;
        }

        internal bool TryLand(Vector2 point, double now)
        {
            Vector2 center = CenterAt(now);
            if (point.x < center.x - HalfSize || point.x >= center.x + HalfSize ||
                point.y < center.y - HalfSize || point.y >= center.y + HalfSize) return false;

            lockedCenter = center;
            Locked = true;
            return true;
        }
    }

    public sealed class PlayerTileTracker
    {
        // In metres. A supported stance survives small head shifts across an internal seam.
        public const float BorderTolerance = 0.035f;
        public const float SupportReach = 0.09f;
        public const double GapGrace = 0.3;
        internal const double JumpGapGrace = .45;
        public TileSample Current { get; private set; } = new TileSample(SupportKind.Untracked);

        private double gapStartedAt = double.NaN;
        private bool crossingJumpGap;
        internal double GapDeadline => gapStartedAt + (crossingJumpGap ? JumpGapGrace : GapGrace);
        private double previousPoseTime = double.NaN;
        private Vector2 previousPose;

        // Project vertically through the headset onto the floor even if the anchor has a small tilt.
        public static Vector2 Project(Transform board, Vector3 worldHead)
        {
            return Project(board.worldToLocalMatrix, worldHead);
        }

        public static Vector2 Project(Matrix4x4 worldToBoard, Vector3 worldHead)
        {
            Vector3 local = worldToBoard.MultiplyPoint3x4(worldHead);
            Vector3 up = worldToBoard.MultiplyVector(Vector3.up);
            if (Math.Abs(up.y) < 0.00001f) return new Vector2(float.NaN, float.NaN);
            local -= up * (local.y / up.y);
            return new Vector2(local.x, local.z);
        }

        // The center eye rotates around the neck when looking down. Project that pivot,
        // rather than the eye or gaze ray, to estimate a planted stance from the tracked rig.
        public static Vector2 ProjectStance(Matrix4x4 worldToBoard, Vector3 eye, Quaternion rotation, float eyeDepth)
        {
            return Project(worldToBoard, eye + rotation * new Vector3(0f, -.075f, -eyeDepth));
        }

        internal void Reset()
        {
            Current = new TileSample(SupportKind.Untracked);
            ResetTiming();
        }

        internal void ResetTiming()
        {
            gapStartedAt = double.NaN;
            crossingJumpGap = false;
            previousPoseTime = double.NaN;
        }

        // Between two tracked frames, estimate the pose at zero instead of giving an extra frame to move.
        internal Vector2 PoseAtDeadline(Vector2 point, double now, double deadline)
        {
            if (now <= deadline || double.IsNaN(previousPoseTime)) return point;
            if (previousPoseTime >= deadline) return previousPose;
            float fraction = (float)((deadline - previousPoseTime) / (now - previousPoseTime));
            return previousPose + (point - previousPose) * fraction;
        }

        internal TileSample Sample(BoardState board, Vector2 point, bool tracked, double now)
        {
            TileSample sample = Classify(board, point, tracked, now);
            var wave = board.ActiveWave;
            if (sample.Kind != SupportKind.Gap || wave == null || !wave.IsJumpGap(sample.Index))
                crossingJumpGap = false;
            else if (Current.Kind != SupportKind.Gap || Current.Index != sample.Index)
                crossingJumpGap = Current.Kind == SupportKind.Glass && Current.Index == wave.StartingTile;
            Current = new TileSample(sample.Kind, sample.Index, sample.IsSlider, point);
            if (sample.Kind == SupportKind.Glass || sample.Kind == SupportKind.Gap)
            {
                previousPoseTime = now;
                previousPose = point;
            }
            else
            {
                previousPoseTime = double.NaN;
            }
            return Current;
        }

        internal void UpdateGap(double now)
        {
            if (Current.Kind == SupportKind.Gap)
            {
                if (double.IsNaN(gapStartedAt)) gapStartedAt = now;
            }
            else gapStartedAt = double.NaN;
        }

        private TileSample Classify(BoardState board, Vector2 point, bool tracked, double now)
        {
            if (!tracked || !Finite(point)) return new TileSample(SupportKind.Untracked);
            int index = board.IndexAt(point);
            if (index < 0) return new TileSample(SupportKind.Outside);
            bool intact = board.IsIntact(index);
            if (!intact)
            {
                for (int i = 0; i < board.Sliders.Count; ++i)
                {
                    var pane = board.Sliders[i];
                    if (pane.TryLand(point, now)) return new TileSample(SupportKind.Glass, pane.Tile, true);
                }
            }

            var slider = Current.IsSlider ? board.SliderFor(Current.Index) : null;
            float half = board.Pitch * BoardState.PaneHalfPitch;
            float reach = intact ? BorderTolerance : SupportReach;
            if (Current.Kind == SupportKind.Glass &&
                (Current.IsSlider ? slider != null && slider.Locked && slider.Tile == Current.Index : board.IsIntact(Current.Index)))
            {
                Vector2 old = Current.IsSlider ? slider.CenterAt(now) : board.Center(Current.Index);
                if (DistanceToPaneSquared(point, old, half) <= reach * reach) return Current;
            }
            if (intact) return new TileSample(SupportKind.Glass, index);

            // Only the immediate neighbours can meet this circular footprint, even on
            // the narrowest board. A full gap center always remains unsupported.
            int nearest = -1, col = index % board.Width, row = index / board.Width;
            float best = SupportReach * SupportReach;
            for (int y = Math.Max(0,row-1); y <= Math.Min(board.Height-1,row+1); ++y)
            for (int x = Math.Max(0,col-1); x <= Math.Min(board.Width-1,col+1); ++x)
            {
                int tile = y * board.Width + x;
                if (!board.IsIntact(tile)) continue;
                float distance = DistanceToPaneSquared(point,board.Center(tile),half);
                if (distance >= best) continue;
                best = distance;
                nearest = tile;
            }
            return nearest < 0 ? new TileSample(SupportKind.Gap, index) : new TileSample(SupportKind.Glass, nearest);
        }

        private static float DistanceToPaneSquared(Vector2 point, Vector2 center, float half)
        {
            float x = Math.Max(0f,Math.Abs(point.x-center.x)-half);
            float y = Math.Max(0f,Math.Abs(point.y-center.y)-half);
            return x*x+y*y;
        }

        internal static bool Finite(Vector2 point)
        {
            return !(float.IsNaN(point.x) || float.IsInfinity(point.x) ||
                     float.IsNaN(point.y) || float.IsInfinity(point.y));
        }
    }
}
