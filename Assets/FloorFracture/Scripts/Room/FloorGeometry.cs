using System;
using System.Collections.Generic;
using UnityEngine;

namespace FloorFracture
{
    // All points are metres in world XZ. An obstruction may also be a line (a wall)
    // or a single point (a vertical support). The floor itself must be a simple polygon.
    public sealed class RoomFloor
    {
        public readonly Vector2[] Boundary;
        public readonly List<Vector2[]> Obstacles;
        public readonly float Height;

        public RoomFloor(Vector2[] boundary, List<Vector2[]> obstacles, float height = 0f)
        {
            Boundary = boundary;
            Obstacles = obstacles;
            Height = height;
        }
    }

    internal static class FloorGeometry
    {
        // Allow exact edge contact within 0.01 mm of floating-point roundoff.
        internal const float Epsilon = 0.00001f;

        internal static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Finite(Vector2 p) => Finite(p.x) && Finite(p.y);

        internal static void Bounds(Vector2[] vertices, out Vector2 min, out Vector2 max)
        {
            min = max = vertices[0];
            foreach (var point in vertices)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }

        internal static Vector2 Closest(Vector2 a, Vector2 b, Vector2 point)
        {
            var direction = b - a;
            var length = direction.sqrMagnitude;
            if (length < Epsilon * Epsilon) return a;
            return a + Mathf.Clamp01(Vector2.Dot(point - a, direction) / length) * direction;
        }

        internal static bool Intersection(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 point)
        {
            point = default;
            var ab = b - a;
            var cd = d - c;
            float denominator = Cross(ab, cd);
            if (Mathf.Abs(denominator) < 1e-10f) return false;
            float t = Cross(c - a, cd) / denominator;
            float u = Cross(c - a, ab) / denominator;
            if (t < -Epsilon || t > 1f + Epsilon || u < -Epsilon || u > 1f + Epsilon) return false;
            point = a + t * ab;
            return true;
        }

        internal static bool Contains(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var a = polygon[j];
                var b = polygon[i];
                if ((point - Closest(a, b, point)).sqrMagnitude <= Epsilon * Epsilon) return true;
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < a.x + (b.x - a.x) * (point.y - a.y) / (b.y - a.y)) inside = !inside;
            }
            return inside;
        }

        internal static bool SimpleFloor(Vector2[] boundary)
        {
            if (boundary == null || boundary.Length < 3) return false;
            double twiceArea = 0;
            for (int i = 0; i < boundary.Length; i++)
            {
                var a = boundary[i];
                var b = boundary[(i + 1) % boundary.Length];
                if (!Finite(a) || (b - a).sqrMagnitude < Epsilon * Epsilon) return false;
                twiceArea += (double)a.x * b.y - (double)b.x * a.y;
                for (int j = i + 2; j < boundary.Length; j++)
                {
                    if (i == 0 && j == boundary.Length - 1) continue;
                    var c = boundary[j];
                    var d = boundary[(j + 1) % boundary.Length];
                    if (Intersection(a, b, c, d, out _) ||
                        (a - Closest(c, d, a)).sqrMagnitude <= Epsilon * Epsilon ||
                        (c - Closest(a, b, c)).sqrMagnitude <= Epsilon * Epsilon) return false;
                }
            }
            return Math.Abs(twiceArea) > Epsilon;
        }

        // Clip to the rectangle interior. Testing only board corners misses an inward
        // notch, a thin wall across the middle, or a chair wholly inside the board.
        internal static bool EntersInterior(Vector2 a, Vector2 b, Vector2 min, Vector2 max)
        {
            float near = 0f, far = 1f;
            var delta = b - a;
            return Clip(a.x, delta.x, min.x + Epsilon, max.x - Epsilon, ref near, ref far) &&
                   Clip(a.y, delta.y, min.y + Epsilon, max.y - Epsilon, ref near, ref far);
        }

        private static bool Clip(float origin, float direction, float min, float max, ref float near, ref float far)
        {
            if (Mathf.Abs(direction) < 1e-10f) return origin >= min && origin <= max;
            float a = (min - origin) / direction, b = (max - origin) / direction;
            near = Mathf.Max(near, Mathf.Min(a, b));
            far = Mathf.Min(far, Mathf.Max(a, b));
            return near <= far;
        }

        // Projecting a rotated 3D volume can produce a hexagon. A convex hull of all
        // eight corners preserves that footprint, including tilted furniture.
        internal static Vector2[] ConvexHull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var unique = new List<Vector2>(points.Count);
            foreach (var p in points)
                if (unique.Count == 0 || (p - unique[unique.Count - 1]).sqrMagnitude > Epsilon * Epsilon)
                    unique.Add(p);
            if (unique.Count <= 2) return unique.ToArray();

            var hull = new List<Vector2>(unique.Count * 2);
            foreach (var p in unique) Append(p, 0);
            int lowerEnd = hull.Count - 1;
            for (int i = unique.Count - 2; i >= 0; i--) Append(unique[i], lowerEnd);
            hull.RemoveAt(hull.Count - 1);
            return hull.ToArray();

            void Append(Vector2 p, int start)
            {
                while (hull.Count >= start + 2 &&
                       Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
        }
    }
}
