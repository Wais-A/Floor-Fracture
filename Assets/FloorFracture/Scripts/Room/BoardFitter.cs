using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FloorFracture
{
    public readonly struct BoardPlacement
    {
        public const float TileSize = 0.5f;
        public const float Clearance = 0.4f;
        public readonly int Columns;
        public readonly int Rows;
        public readonly Vector2 Center;
        public readonly float FloorHeight;
        public readonly float AngleDegrees;

        public Pose Pose => new Pose(new Vector3(Center.x, FloorHeight, Center.y),
            Quaternion.Euler(0f, -AngleDegrees, 0f));

        public BoardPlacement(Vector2 center, float floorHeight, float angleDegrees, int columns, int rows)
        {
            Center = center;
            FloorHeight = floorHeight;
            AngleDegrees = angleDegrees;
            Columns = columns;
            Rows = rows;
        }

        public Vector2 ToWorldXZ(float x, float z)
        {
            float radians = AngleDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return Center + new Vector2(c * x - s * z, s * x + c * z);
        }

        public Vector2[] ClearanceOutline()
        {
            float x = Columns * TileSize / 2f + Clearance;
            float z = Rows * TileSize / 2f + Clearance;
            return new[] { ToWorldXZ(-x, -z), ToWorldXZ(x, -z), ToWorldXZ(x, z), ToWorldXZ(-x, z) };
        }
    }

    public static class BoardFitter
    {
        private static readonly Vector2Int[] Sizes =
        {
            new Vector2Int(4, 4), new Vector2Int(4, 3), new Vector2Int(3, 4), new Vector2Int(3, 3),
            new Vector2Int(4, 2), new Vector2Int(2, 4), new Vector2Int(3, 2), new Vector2Int(2, 3),
            new Vector2Int(2, 2)
        };

        public static bool TryFind(IReadOnlyList<RoomFloor> floors, Vector2 preferredCenter, out BoardPlacement placement,
            CancellationToken cancellation = default)
        {
            placement = default;
            if (floors == null || !FloorGeometry.Finite(preferredCenter)) return false;
            var frames = new List<Frame>();
            foreach (var floor in floors)
            {
                if (!IsUsable(floor)) continue;
                var angles = new List<float> { 0f };
                AddEdgeDirections(floor.Boundary, angles);
                foreach (var obstacle in floor.Obstacles) AddEdgeDirections(obstacle, angles);
                for (int angle = 5; angle < 90; angle += 5) AddAngle(angle, angles);
                foreach (float angle in angles) frames.Add(new Frame(floor, angle));
            }

            foreach (var size in Sizes)
            {
                Vector2 half = HalfSize(size.x, size.y);
                foreach (var frame in frames)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!frame.TryFindCenter(half, frame.ToLocal(preferredCenter), cancellation, out var center)) continue;
                    placement = new BoardPlacement(frame.ToWorld(center), frame.Height, frame.Angle, size.x, size.y);
                    return true;
                }
            }
            return false;
        }

        public static bool Fits(RoomFloor floor, BoardPlacement placement)
        {
            if (!IsUsable(floor) || !FloorGeometry.Finite(placement.Center) ||
                !FloorGeometry.Finite(placement.AngleDegrees) || !FloorGeometry.Finite(placement.FloorHeight) ||
                Mathf.Abs(floor.Height - placement.FloorHeight) > 0.025f ||
                placement.Columns < 2 || placement.Columns > 4 || placement.Rows < 2 || placement.Rows > 4) return false;
            var frame = new Frame(floor, placement.AngleDegrees);
            return frame.Admits(frame.ToLocal(placement.Center), HalfSize(placement.Columns, placement.Rows));
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

        private static Vector2 HalfSize(int columns, int rows) => new Vector2(
            columns * BoardPlacement.TileSize / 2f + BoardPlacement.Clearance,
            rows * BoardPlacement.TileSize / 2f + BoardPlacement.Clearance);

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
            private readonly Vector2 right, forward;
            private readonly Shape floor;
            private readonly List<Shape> obstacles = new List<Shape>();

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

            public bool TryFindCenter(Vector2 half, Vector2 preferred, CancellationToken cancellation, out Vector2 center)
            {
                center = default;
                var lower = floor.Min + half;
                var upper = floor.Max - half;
                const float e = FloorGeometry.Epsilon;
                if (lower.x > upper.x + e || lower.y > upper.y + e) return false;
                var target = new Vector2(Mathf.Clamp(preferred.x, lower.x, upper.x), Mathf.Clamp(preferred.y, lower.y, upper.y));
                if (Admits(target, half)) { center = target; return true; }
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
