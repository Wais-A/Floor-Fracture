using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FloorFracture
{
    public readonly struct BoardPlacement
    {
        public const float Clearance = 0.4f;
        public readonly int Columns;
        public readonly int Rows;
        public readonly Vector2 Center;
        public readonly float FloorHeight;
        public readonly float AngleDegrees;
        public readonly float Pitch;

        public Pose Pose => new Pose(new Vector3(Center.x, FloorHeight, Center.y),
            Quaternion.Euler(0f, -AngleDegrees, 0f));

        public BoardPlacement(Vector2 center, float floorHeight, float angleDegrees, int columns, int rows,
            float pitch = BoardState.MinPitch)
        {
            Center = center;
            FloorHeight = floorHeight;
            AngleDegrees = angleDegrees;
            Columns = columns;
            Rows = rows;
            Pitch = pitch;
        }

        public Vector2 ToWorldXZ(float x, float z)
        {
            float radians = AngleDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return Center + new Vector2(c * x - s * z, s * x + c * z);
        }

        public Vector2[] ClearanceOutline()
        {
            float x = Columns * Pitch / 2f + Clearance;
            float z = Rows * Pitch / 2f + Clearance;
            return new[] { ToWorldXZ(-x, -z), ToWorldXZ(x, -z), ToWorldXZ(x, z), ToWorldXZ(-x, z) };
        }
    }

    public static class BoardFitter
    {
        private const float PitchPrecision = 0.0005f;
        private const float ScoreTolerance = 0.00001f;

        private readonly struct Candidate
        {
            public readonly Frame Frame;
            public readonly int Columns, Rows;
            public readonly float MaximumPitch, MaximumScore;
            public Candidate(Frame frame, int columns, int rows, float maximumPitch)
            {
                Frame = frame;
                Columns = columns;
                Rows = rows;
                MaximumPitch = maximumPitch;
                MaximumScore = Score(columns * rows, maximumPitch);
            }
        }

        // Weight covered glass area by the playable area of one pane. At equal physical
        // coverage this favors a wider step, while a long narrow room can still add columns.
        private static float Score(int cells, float pitch)
        {
            float paneArea = pitch * pitch;
            return cells * paneArea * paneArea;
        }

        public static bool TryFind(IReadOnlyList<RoomFloor> floors, Vector2 preferredCenter, out BoardPlacement placement,
            CancellationToken cancellation = default, IReadOnlyList<Vector2> suggestedCenters = null)
        {
            placement = default;
            if (floors == null || !FloorGeometry.Finite(preferredCenter)) return false;
            var suggestions = new List<Vector2>();
            if (suggestedCenters != null)
                foreach (var suggestion in suggestedCenters)
                    if (FloorGeometry.Finite(suggestion)) suggestions.Add(suggestion);
            suggestions.Sort((a, b) => (a - preferredCenter).sqrMagnitude.CompareTo((b - preferredCenter).sqrMagnitude));
            var candidates = new List<Candidate>();
            int frameOrder = 0;
            foreach (var floor in floors)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!IsUsable(floor)) continue;
                var angles = new List<float> { 0f };
                AddEdgeDirections(floor.Boundary, angles);
                foreach (var obstacle in floor.Obstacles) AddEdgeDirections(obstacle, angles);
                for (int angle = 5; angle < 90; angle += 5) AddAngle(angle, angles);
                foreach (float angle in angles)
                {
                    var frame = new Frame(floor, angle);
                    frame.PrepareSearch(preferredCenter, suggestions, frameOrder++);
                    // Every legal footprint contains this one. In a cluttered scan a
                    // single failed search removes this orientation for all 49 shapes.
                    if (frame.PitchLimit(BoardState.MinAxis, BoardState.MinAxis) < BoardState.MinPitch - FloorGeometry.Epsilon ||
                        !frame.TryFindCenter(HalfSize(BoardState.MinAxis, BoardState.MinAxis, BoardState.MinPitch),
                            cancellation, out _)) continue;
                    for (int columns = BoardState.MinAxis; columns <= BoardState.MaxAxis; ++columns)
                    for (int rows = BoardState.MinAxis; rows <= BoardState.MaxAxis; ++rows)
                    {
                        float upper = frame.PitchLimit(columns, rows);
                        if (upper >= BoardState.MinPitch - FloorGeometry.Epsilon)
                            candidates.Add(new Candidate(frame, columns, rows, Mathf.Max(BoardState.MinPitch, upper)));
                    }
                }
            }
            candidates.Sort((a, b) =>
            {
                int score = b.MaximumScore.CompareTo(a.MaximumScore);
                return score != 0 ? score : a.Frame.Order.CompareTo(b.Frame.Order);
            });
            float minimumArea = LegacyCoverage(candidates, cancellation);
            float bestScore = 0f, bestDistance = float.PositiveInfinity;
            foreach (var candidate in candidates)
            {
                cancellation.ThrowIfCancellationRequested();
                if (candidate.MaximumScore < bestScore - ScoreTolerance) break;
                if (candidate.MaximumScore <= bestScore && bestDistance == 0f) break;
                int columns = candidate.Columns, rows = candidate.Rows;
                Frame frame = candidate.Frame;
                float pitch = candidate.MaximumPitch;
                float coveragePitch = Mathf.Sqrt(minimumArea / (columns * rows));
                if (columns * rows * pitch * pitch < minimumArea) continue;
                if (!frame.TryFindCenter(HalfSize(columns, rows, pitch), cancellation, out var center))
                {
                    // Smaller rectangles nest inside larger ones. Only search pitches that
                    // can improve the current winner and retain an explicitly certified fit.
                    float cutoff = Mathf.Sqrt(Mathf.Sqrt(Mathf.Max(0f, bestScore - ScoreTolerance) / (columns * rows)));
                    float low = Mathf.Max(coveragePitch, Mathf.Max(BoardState.MinPitch, cutoff)), high = pitch;
                    if (low >= high || !frame.TryFindCenter(HalfSize(columns, rows, low), cancellation, out center))
                        continue;
                    while (high - low > PitchPrecision)
                    {
                        float mid = (low + high) * 0.5f;
                        if (frame.TryFindCenter(HalfSize(columns, rows, mid), cancellation, out var trial))
                        {
                            low = mid;
                            center = trial;
                        }
                        else high = mid;
                    }
                    pitch = low;
                }
                if (columns * rows * pitch * pitch < minimumArea) continue;
                Vector2 world = frame.ToWorld(center);
                float score = Score(columns * rows, pitch), distance = (world - preferredCenter).sqrMagnitude;
                if (score < bestScore - ScoreTolerance ||
                    (Mathf.Abs(score - bestScore) <= ScoreTolerance && distance >= bestDistance)) continue;
                bestScore = score;
                bestDistance = distance;
                placement = new BoardPlacement(world, frame.Height, frame.Angle, columns, rows, pitch);
            }
            return bestScore > 0f;
        }

        private static float LegacyCoverage(List<Candidate> candidates, CancellationToken cancellation)
        {
            // Preserve the largest playable area that the previous fixed 50 cm, 2–4
            // pane-per-axis layout could actually fit on these same floor and obstacle polygons.
            int mostCells = 0;
            foreach (var candidate in candidates)
            {
                int cells = candidate.Columns * candidate.Rows;
                if (candidate.Columns > 4 || candidate.Rows > 4 || cells <= mostCells) continue;
                cancellation.ThrowIfCancellationRequested();
                var half = HalfSize(candidate.Columns, candidate.Rows, BoardState.MinPitch);
                if (!candidate.Frame.TryFindCenter(half, cancellation, out _)) continue;
                mostCells = cells;
                if (cells == 16) break;
            }
            return mostCells * BoardState.MinPitch * BoardState.MinPitch;
        }

        public static bool Fits(RoomFloor floor, BoardPlacement placement)
        {
            if (!IsUsable(floor) || !FloorGeometry.Finite(placement.Center) ||
                !FloorGeometry.Finite(placement.AngleDegrees) || !FloorGeometry.Finite(placement.FloorHeight) ||
                Mathf.Abs(floor.Height - placement.FloorHeight) > 0.025f ||
                placement.Columns < BoardState.MinAxis || placement.Columns > BoardState.MaxAxis ||
                placement.Rows < BoardState.MinAxis || placement.Rows > BoardState.MaxAxis ||
                !(placement.Pitch >= BoardState.MinPitch && placement.Pitch <= BoardState.MaxPitch)) return false;
            var frame = new Frame(floor, placement.AngleDegrees);
            return frame.Admits(frame.ToLocal(placement.Center), HalfSize(placement.Columns, placement.Rows, placement.Pitch));
        }

        internal static bool IsUsable(RoomFloor floor)
        {
            if (floor == null || !FloorGeometry.Finite(floor.Height) || !FloorGeometry.SimpleFloor(floor.Boundary) ||
                floor.Obstacles == null) return false;
            foreach (var outline in floor.Obstacles)
            {
                if (outline == null || outline.Length == 0) return false;
                foreach (var p in outline) if (!FloorGeometry.Finite(p)) return false;
            }
            return true;
        }

        private static Vector2 HalfSize(int columns, int rows, float pitch) => new Vector2(
            columns * pitch / 2f + BoardPlacement.Clearance, rows * pitch / 2f + BoardPlacement.Clearance);

        private static void AddEdgeDirections(Vector2[] outline, List<float> angles)
        {
            for (int i = 0; i < outline.Length; i++)
            {
                var edge = outline[(i + 1) % outline.Length] - outline[i];
                if (edge.sqrMagnitude < 0.0001f) continue;
                float angle = Mathf.Atan2(edge.y, edge.x) * Mathf.Rad2Deg;
                AddAngle(Mathf.Repeat(angle, 90f), angles);
            }
        }

        private static void AddAngle(float angle, List<float> angles)
        {
            foreach (float existing in angles)
            {
                float difference = Mathf.Abs(existing - angle);
                if (Mathf.Min(difference, 90f - difference) < 0.00005f) return;
            }
            angles.Add(angle);
        }

        private sealed class Shape
        {
            public readonly Vector2[] Points;
            public readonly Vector2 Min, Max;
            public Shape(Vector2[] points)
            {
                Points = points;
                FloorGeometry.Bounds(points, out Min, out Max);
            }

            public bool EdgesEnter(Vector2 min, Vector2 max)
            {
                if (Max.x < min.x || Max.y < min.y || Min.x > max.x || Min.y > max.y) return false;
                for (int i = 0, j = Points.Length - 1; i < Points.Length; j = i++)
                    if (FloorGeometry.EntersInterior(Points[j], Points[i], min, max)) return true;
                return false;
            }
        }

        private readonly struct Segment
        {
            public readonly Vector2 A, B, Min, Max;
            public Segment(Vector2 a, Vector2 b)
            {
                A = a; B = b;
                Min = Vector2.Min(a, b); Max = Vector2.Max(a, b);
            }
        }

        private sealed class Frame
        {
            public readonly float Height, Angle;
            public int Order { get; private set; }
            private readonly Vector2 right, forward;
            private readonly Shape floor;
            private readonly List<Shape> obstacles = new List<Shape>();
            private readonly List<Vector2> failedHalfSizes = new List<Vector2>();
            private readonly List<Vector2> suggestions = new List<Vector2>();
            private readonly List<Vector3> spanLimits = new List<Vector3>();
            private Vector2 preferred;

            public Frame(RoomFloor source, float angle)
            {
                Angle = angle;
                Height = source.Height;
                right = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                forward = new Vector2(-right.y, right.x);
                floor = Project(source.Boundary);
                foreach (var outline in source.Obstacles) obstacles.Add(Project(outline));
            }

            public Vector2 ToLocal(Vector2 point) => new Vector2(Vector2.Dot(point, right), Vector2.Dot(point, forward));
            public Vector2 ToWorld(Vector2 point) => right * point.x + forward * point.y;

            public void PrepareSearch(Vector2 preferredCenter, List<Vector2> suggestedCenters, int order)
            {
                preferred = ToLocal(preferredCenter);
                Order = order;
                foreach (var suggestion in suggestedCenters) suggestions.Add(ToLocal(suggestion));
                // The board's projection onto every floor-edge normal must fit between
                // the floor's extreme points. In particular, a diagonal frame cannot
                // inflate the apparent width of a long, narrow room.
                var span = floor.Max - floor.Min;
                spanLimits.Add(new Vector3(1f, 0f, span.x));
                spanLimits.Add(new Vector3(0f, 1f, span.y));
                for (int i = 0; i < floor.Points.Length; ++i)
                {
                    var edge = floor.Points[(i + 1) % floor.Points.Length] - floor.Points[i];
                    var normal = new Vector2(-edge.y, edge.x) / edge.magnitude;
                    float min = float.PositiveInfinity, max = float.NegativeInfinity;
                    foreach (var p in floor.Points)
                    {
                        float projection = Vector2.Dot(p, normal);
                        min = Mathf.Min(min, projection);
                        max = Mathf.Max(max, projection);
                    }
                    spanLimits.Add(new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), max - min));
                }
            }

            public float PitchLimit(int columns, int rows)
            {
                float limit = BoardState.MaxPitch;
                foreach (var span in spanLimits)
                {
                    float clearance = 2f * BoardPlacement.Clearance * (span.x + span.y);
                    limit = Mathf.Min(limit, (span.z - clearance) / (columns * span.x + rows * span.y));
                }
                return limit;
            }

            private Shape Project(Vector2[] points)
            {
                var transformed = new Vector2[points.Length];
                for (int i = 0; i < points.Length; i++) transformed[i] = ToLocal(points[i]);
                return new Shape(transformed);
            }

            public bool Admits(Vector2 center, Vector2 half)
            {
                var min = center - half;
                var max = center + half;
                const float e = FloorGeometry.Epsilon;
                if (min.x < floor.Min.x - e || min.y < floor.Min.y - e ||
                    max.x > floor.Max.x + e || max.y > floor.Max.y + e) return false;
                if (!FloorGeometry.Contains(floor.Points, center) ||
                    !FloorGeometry.Contains(floor.Points, min) || !FloorGeometry.Contains(floor.Points, max) ||
                    !FloorGeometry.Contains(floor.Points, new Vector2(min.x, max.y)) ||
                    !FloorGeometry.Contains(floor.Points, new Vector2(max.x, min.y)) ||
                    floor.EdgesEnter(min, max)) return false;
                foreach (var shape in obstacles)
                {
                    if (shape.EdgesEnter(min, max)) return false;
                    // A large furniture footprint can contain the whole padded board.
                    if (shape.Points.Length >= 3 && center.x >= shape.Min.x && center.x <= shape.Max.x &&
                        center.y >= shape.Min.y && center.y <= shape.Max.y &&
                        FloorGeometry.Contains(shape.Points, center)) return false;
                }
                return true;
            }

            public bool TryFindCenter(Vector2 half, CancellationToken cancellation, out Vector2 center)
            {
                foreach (var failed in failedHalfSizes)
                    if (half.x >= failed.x && half.y >= failed.y) { center = default; return false; }
                if (Search(half, cancellation, out center)) return true;
                for (int i = failedHalfSizes.Count - 1; i >= 0; --i)
                    if (failedHalfSizes[i].x >= half.x && failedHalfSizes[i].y >= half.y) failedHalfSizes.RemoveAt(i);
                failedHalfSizes.Add(half);
                return false;
            }

            private bool Search(Vector2 half, CancellationToken cancellation, out Vector2 center)
            {
                center = default;
                var lower = floor.Min + half;
                var upper = floor.Max - half;
                const float e = FloorGeometry.Epsilon;
                if (lower.x > upper.x + e || lower.y > upper.y + e) return false;
                var target = new Vector2(Mathf.Clamp(preferred.x, lower.x, upper.x), Mathf.Clamp(preferred.y, lower.y, upper.y));
                if (Admits(target, half)) { center = target; return true; }
                foreach (var suggestion in suggestions)
                    if (Admits(suggestion, half)) { center = suggestion; return true; }
                var middle = (lower + upper) * 0.5f;
                if (Admits(middle, half)) { center = middle; return true; }

                // A feasible region for the board center ends wherever a board corner
                // contacts a scan edge or a scan vertex contacts a board edge. Test those
                // segments, and their intersections, so a tight fit between samples survives.
                var contacts = new List<Segment>();
                AddContacts(floor);
                foreach (var shape in obstacles) AddContacts(shape);
                var candidates = new List<Vector2>(contacts.Count * 3);
                foreach (var contact in contacts)
                {
                    candidates.Add(FloorGeometry.Closest(contact.A, contact.B, target));
                    candidates.Add(contact.A);
                    candidates.Add(contact.B);
                }
                candidates.Sort((a, b) => (a - target).sqrMagnitude.CompareTo((b - target).sqrMagnitude));
                foreach (var candidate in candidates)
                {
                    if (!Admits(candidate, half)) continue;
                    center = candidate;
                    return true;
                }
                contacts.Sort((a, b) => a.Min.x.CompareTo(b.Min.x));
                for (int i = 0; i < contacts.Count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var a = contacts[i];
                    for (int j = i + 1; j < contacts.Count; j++)
                    {
                        var b = contacts[j];
                        if (b.Min.x > a.Max.x + e) break;
                        if (b.Max.y < a.Min.y - e || b.Min.y > a.Max.y + e) continue;
                        if (!FloorGeometry.Intersection(a.A, a.B, b.A, b.B, out var candidate) ||
                            !Admits(candidate, half)) continue;
                        center = candidate;
                        return true;
                    }
                }
                return false;

                void AddContacts(Shape shape)
                {
                    var corner = new Vector2(half.x, -half.y);
                    for (int i = 0; i < shape.Points.Length; i++)
                    {
                        var a = shape.Points[i];
                        var b = shape.Points[(i + 1) % shape.Points.Length];
                        Add(a - half, a + corner);
                        Add(a + corner, a + half);
                        Add(a + half, a - corner);
                        Add(a - corner, a - half);
                        var edge = b - a;
                        var offset = new Vector2(Math.Sign(-edge.y) * half.x, Math.Sign(edge.x) * half.y);
                        Add(a + offset, b + offset);
                        Add(a - offset, b - offset);
                    }
                }

                void Add(Vector2 a, Vector2 b)
                {
                    if (Mathf.Max(a.x, b.x) < lower.x - e || Mathf.Min(a.x, b.x) > upper.x + e ||
                        Mathf.Max(a.y, b.y) < lower.y - e || Mathf.Min(a.y, b.y) > upper.y + e) return;
                    contacts.Add(new Segment(a, b));
                }
            }
        }
    }
}
