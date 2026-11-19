using System;
using UnityEngine;

namespace FloorFracture
{
    public enum SupportKind { Untracked, Outside, Gap, Glass }

    public readonly struct TileSample
    {
        public SupportKind Kind { get; }
        public int Index { get; }
        public bool IsSlider { get; }

        internal TileSample(SupportKind kind, int index = -1, bool isSlider = false)
        {
            Kind = kind;
            Index = index;
            IsSlider = isSlider;
        }
    }

    public sealed class SlidingPane
    {
        // The moving pane and its full travel fit inside one already-open 0.5 m cell.
        public const float Travel = 0.09f;
        public float HalfSize => 0.15f;
        public int Tile { get; }
        public Vector2 Direction { get; }
        public double MotionStartsAt { get; }
        public bool Locked { get; private set; }

        private readonly Vector2 holeCenter;
        private Vector2 lockedCenter;
        private const double OneWaySeconds = 1.20;

        internal SlidingPane(int tile, Vector2 holeCenter, Vector2 direction, double start)
        {
            Tile = tile;
            this.holeCenter = holeCenter;
            Direction = direction;
            MotionStartsAt = start + 0.65;
        }

        public Vector2 CenterAt(double now)
        {
            if (Locked) return lockedCenter;
            double phase = (Math.Max(0, now - MotionStartsAt) / OneWaySeconds) % 2.0;
            double fraction = phase <= 1 ? phase : 2 - phase;
            return holeCenter + Direction * (float)((2 * fraction - 1) * Travel);
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
        public const float BorderTolerance = 0.008f;
        public const double GapGrace = 0.3;
        public TileSample Current { get; private set; } = new TileSample(SupportKind.Untracked);

        private double gapStartedAt = double.NaN;
        internal double GapDeadline => gapStartedAt + GapGrace;
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

        internal void Reset()
        {
            Current = new TileSample(SupportKind.Untracked);
            ResetTiming();
        }

        internal void ResetTiming()
        {
            gapStartedAt = double.NaN;
            previousPoseTime = double.NaN;
        }

        // Between two tracked frames, estimate the pose at zero instead of giving an extra frame to move.
        internal Vector2 PoseAtDeadline(Vector2 point, double now, double deadline)
        {
            if (now <= deadline || double.IsNaN(previousPoseTime) || previousPoseTime >= deadline)
                return point;
            float fraction = (float)((deadline - previousPoseTime) / (now - previousPoseTime));
            return previousPose + (point - previousPose) * fraction;
        }

        internal TileSample Sample(BoardState board, Vector2 point, bool tracked, double now)
        {
            TileSample sample = Classify(board, point, tracked, now);
            Current = sample;
            if (sample.Kind == SupportKind.Glass || sample.Kind == SupportKind.Gap)
            {
                previousPoseTime = now;
                previousPose = point;
            }
            else
            {
                previousPoseTime = double.NaN;
            }
            return sample;
        }

        internal bool GraceExpired(double now)
        {
            // An arrival exactly at the end of grace is late. Compare deadlines in their time domain.
            if (!double.IsNaN(gapStartedAt) && now >= gapStartedAt + GapGrace) return true;
            if (Current.Kind == SupportKind.Gap)
            {
                if (double.IsNaN(gapStartedAt)) gapStartedAt = now;
            }
            else gapStartedAt = double.NaN;
            return false;
        }

        private TileSample Classify(BoardState board, Vector2 point, bool tracked, double now)
        {
            if (!tracked || !Finite(point)) return new TileSample(SupportKind.Untracked);
            int index = board.IndexAt(point);
            if (index < 0) return new TileSample(SupportKind.Outside);
            if (!board.IsIntact(index))
            {
                if (board.Slider != null && board.Slider.Tile == index && board.Slider.TryLand(point, now))
                    return new TileSample(SupportKind.Glass, index, true);
                return new TileSample(SupportKind.Gap, index);
            }

            int previous = Current.Index;
            WavePlan wave = board.ActiveWave;
            // At readiness and the deadline, use the exact pane. Otherwise an unchanged head
            // on the far side of a seam could start the next wave on a different safe cell.
            if (wave != null && now < wave.Deadline && Current.Kind == SupportKind.Glass &&
                !Current.IsSlider && previous != index &&
                board.IsIntact(previous) && board.Neighbors(previous, index) &&
                wave.IsBreaking(previous) == wave.IsBreaking(index))
            {
                // A single sample drives the marker and the verdict. A dangerous edge, hole, or
                // perimeter always uses the exact footprint; two equivalent panes can share a seam.
                Vector2 oldCenter = board.Center(previous);
                Vector2 newCenter = board.Center(index);
                bool alongX = previous % board.Width != index % board.Width;
                float seam = alongX ? (oldCenter.x + newCenter.x) * 0.5f : (oldCenter.y + newCenter.y) * 0.5f;
                if (Math.Abs((alongX ? point.x : point.y) - seam) <= BorderTolerance)
                    index = previous;
            }
            return new TileSample(SupportKind.Glass, index);
        }

        internal static bool Finite(Vector2 point)
        {
            return !(float.IsNaN(point.x) || float.IsInfinity(point.x) ||
                     float.IsNaN(point.y) || float.IsInfinity(point.y));
        }
    }
}
