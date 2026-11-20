using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FloorFracture
{
    internal enum TileClearance
    {
        Clear,
        OutsideFloor,
        Furniture,
        MeshObstacle,
        Unobserved,
        LiveObstacle,
        LiveUnknown,
        Declined
    }

    // MRUK supplies the floor polygon. The mesh and native depth add measured
    // exclusions inside it. Survey cells are independent of gameplay pane size.
    internal sealed partial class FloorSurvey
    {
        internal const float FootMargin = .08f;
        internal const float RaisedHeight = .10f;
        internal const float DropDepth = .12f;
        private const float CellSize = .08f;
        private const int MaxCellsPerAxis = 128;
        private readonly float? refinementHeading;
        private readonly FloorMeshScan liveSurface;
        private readonly IReadOnlyList<Vector2[]> liveObstacles;
        internal readonly RoomFloor Floor;
        internal readonly int Width, Height;
        internal readonly TileClearance[] Cells;
        internal readonly Vector2 Step;
        internal readonly float Angle;
        private readonly Vector2 origin, right, forward, min, max;
        private readonly struct Triangle
        {
            public readonly Vector2 A, B, C;
            public Triangle(Vector2 a, Vector2 b, Vector2 c)
            {
                A = a;
                B = b;
                C = c;
            }
        }

        internal FloorSurvey(RoomFloor floor, FloorMeshScan scan, float headroom, IReadOnlyList<Vector2[]> declined,
            CancellationToken cancel, IReadOnlyList<Vector2[]> liveObstacles = null, FloorMeshScan liveSurface = null)
            : this(floor, scan, headroom, declined, cancel, liveObstacles, liveSurface, null) { }

        private FloorSurvey(RoomFloor floor, FloorMeshScan scan, float headroom, IReadOnlyList<Vector2[]> declined,
            CancellationToken cancel, IReadOnlyList<Vector2[]> liveObstacles, FloorMeshScan liveSurface, float? refinedHeading)
        {
            Floor = floor;
            this.liveSurface = liveSurface;
            refinementHeading = refinedHeading;
            this.liveObstacles = liveObstacles;
            origin = floor.Boundary[0];
            Vector2 longest = Vector2.right * FloorGeometry.Epsilon;
            for (int i = 0; i < floor.Boundary.Length; i++)
            {
                var edge = floor.Boundary[(i + 1) % floor.Boundary.Length] - floor.Boundary[i];
                if (edge.sqrMagnitude > longest.sqrMagnitude)
                    longest = edge;
            }

            float radians = refinedHeading.GetValueOrDefault() * Mathf.Deg2Rad;
            right = refinedHeading.HasValue ? new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) : longest.normalized;
            forward = new Vector2(-right.y, right.x);
            Angle = Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg;
            var outline = Array.ConvertAll(floor.Boundary, Local);
            FloorGeometry.Bounds(outline, out min, out max);
            float targetStep = refinedHeading.HasValue ? CellSize * .5f : CellSize;
            int limit = refinedHeading.HasValue ? 192 : MaxCellsPerAxis;
            Width = Mathf.Clamp(Mathf.CeilToInt((max.x - min.x) / targetStep), 1, limit);
            Height = Mathf.Clamp(Mathf.CeilToInt((max.y - min.y) / targetStep), 1, limit);
            Step = new Vector2((max.x - min.x) / Width, (max.y - min.y) / Height);
            Cells = new TileClearance[Width * Height];
            for (int z = 0; z < Height; z++)
                for (int x = 0; x < Width; x++)
                {
                    var cellMin = Corner(x, z);
                    if (!FloorGeometry.Contains(outline, cellMin + Step * .5f) || EdgesEnter(outline, cellMin, Corner(x + 1, z + 1)))
                        Cells[z * Width + x] = TileClearance.OutsideFloor;
                }

            foreach (var obstacle in floor.Obstacles)
                MarkWorld(obstacle, TileClearance.Furniture);
            if (declined != null)
                foreach (var footprint in declined)
                    MarkWorld(footprint, TileClearance.Declined);
            if (liveObstacles != null)
                foreach (var footprint in liveObstacles)
                    MarkWorld(footprint, TileClearance.LiveObstacle);
            if (scan == null)
            {
                for (int i = 0; i < Cells.Length; i++)
                    if (Cells[i] == TileClearance.Clear) Cells[i] = TileClearance.Unobserved;
                return;
            }

            var polygon = new Vector3[8];
            var scratch = new Vector3[8];
            float high = Mathf.Max(floor.MeshFloorHeight, floor.Height) + RaisedHeight;
            float low = Mathf.Min(floor.MeshFloorHeight, floor.Height) - DropDepth;
            float headHigh = floor.Height + headroom;
            var raised = scan.FindRaisedFaces(high, headHigh, cancel);
            var below = new List<Triangle>[Cells.Length];
            var level = new List<Triangle>[Cells.Length];
            for (int t = 0; t < scan.Indices.Length; t += 3)
            {
                if ((t & 255) == 0) cancel.ThrowIfCancellationRequested();
                var a = scan.Vertices[scan.Indices[t]];
                var b = scan.Vertices[scan.Indices[t + 1]];
                var c = scan.Vertices[scan.Indices[t + 2]];
                if (raised[t / 3])
                {
                    int n = SliceAtHeights(a, b, c, high, headHigh, polygon, scratch);
                    if (n > 0)
                    {
                        var footprint = new Vector2[n];
                        for (int j = 0; j < n; j++) footprint[j] = Project(polygon[j]);
                        MarkLocal(footprint, TileClearance.MeshObstacle);
                    }
                }
                var normal = Vector3.Cross(b - a, c - a);
                if (Mathf.Abs(normal.y) < 1e-8f || normal.y * normal.y < .96f * normal.sqrMagnitude) continue;
                // A hole in the tessellation is not a measured step down. Require an
                // actual lower surface and ignore one seen underneath intact floor.
                if (Mathf.Min(a.y, Mathf.Min(b.y, c.y)) < low)
                    AddBand(low - 1f, low, below);
                if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) >= low)
                    AddBand(low, high, level);

                void AddBand(float y0, float y1, List<Triangle>[] cells)
                {
                    int n = SliceAtHeights(a, b, c, y0, y1, polygon, scratch);
                    for (int j = 2; j < n; j++)
                        AddSupport(Project(polygon[0]), Project(polygon[j - 1]), Project(polygon[j]), cells);
                }
            }
            if (liveSurface != null)
            {
                var v = liveSurface.Vertices;
                var t = liveSurface.Indices;
                for (int i = 0; i < t.Length; i += 3)
                    AddSupport(Project(v[t[i]]), Project(v[t[i + 1]]), Project(v[t[i + 2]]), level);
            }
            for (int i = 0; i < Cells.Length; i++)
            {
                if (Cells[i] != TileClearance.Clear || below[i] == null) continue;
                var corner = Corner(i % Width, i / Width);
                var far = Corner(i % Width + 1, i / Width + 1);
                if (Covered(corner, far, below[i]) && !Covered(corner, far, level[i]))
                    Cells[i] = TileClearance.MeshObstacle;
            }
        }

        internal FloorSurvey Refresh(RoomFloor floor, FloorMeshScan scan, float headroom,
            IReadOnlyList<Vector2[]> excluded, CancellationToken cancel) =>
            new FloorSurvey(floor, scan, headroom, excluded, cancel, liveObstacles, liveSurface, refinementHeading);

        private Vector2 Project(Vector3 point) => Local(new Vector2(point.x, point.z));

        private void AddSupport(Vector2 a, Vector2 b, Vector2 c, List<Triangle>[] support)
        {
            float area = FloorGeometry.Cross(b - a, c - a);
            if (area == 0) return;
            var triangle = area > 0 ? new Triangle(a, b, c) : new Triangle(a, c, b);
            Range(Vector2.Min(a, Vector2.Min(b, c)), Vector2.Max(a, Vector2.Max(b, c)), out var low, out var high);
            for (int z = low.y; z <= high.y; z++)
                for (int x = low.x; x <= high.x; x++)
                {
                    int index = z * Width + x;
                    if (Cells[index] == TileClearance.Clear)
                        (support[index] ??= new List<Triangle>()).Add(triangle);
                }
        }

        private static int SliceAtHeights(Vector3 a, Vector3 b, Vector3 c, float low, float high,
            Vector3[] polygon, Vector3[] scratch)
        {
            polygon[0] = a;
            polygon[1] = b;
            polygon[2] = c;
            if (a.y >= low && b.y >= low && c.y >= low && a.y <= high && b.y <= high && c.y <= high)
                return 3;
            int count = ClipHeight(polygon, 3, scratch, low, keepAbove: true);
            return ClipHeight(scratch, count, polygon, high, keepAbove: false);
        }

        private static int ClipHeight(Vector3[] input, int count, Vector3[] output, float height, bool keepAbove)
        {
            if (count == 0) return 0;
            int written = 0;
            var previous = input[count - 1];
            for (int i = 0; i < count; i++)
            {
                var current = input[i];
                if ((previous.y < height && current.y > height) || (previous.y > height && current.y < height))
                {
                    // Both triangles sharing an edge must produce the same crossing in either winding.
                    var lower = previous.y < current.y ? previous : current;
                    var higher = previous.y < current.y ? current : previous;
                    double t = ((double)height - lower.y) / ((double)higher.y - lower.y);
                    output[written++] = new Vector3((float)(lower.x + t * ((double)higher.x - lower.x)), height,
                        (float)(lower.z + t * ((double)higher.z - lower.z)));
                }
                if (keepAbove ? current.y >= height : current.y <= height)
                    output[written++] = current;
                previous = current;
            }
            return written;
        }

        private Vector2 Local(Vector2 p) => new Vector2(Vector2.Dot(p - origin, right), Vector2.Dot(p - origin, forward));
        private Vector2 World(Vector2 p) => origin + right * p.x + forward * p.y;
        // Use the same endpoints for neighboring cells and the exact measured outer edge.
        private Vector2 Corner(int x, int z) => new Vector2(x == Width ? max.x : min.x + x * Step.x,
            z == Height ? max.y : min.y + z * Step.y);
        internal Vector2[] Rect(int x, int z, int columns, int rows)
        {
            return new[]
            {
                World(Corner(x, z)),
                World(Corner(x + columns, z)),
                World(Corner(x + columns, z + rows)),
                World(Corner(x, z + rows))
            };
        }

        private void Range(Vector2 a, Vector2 b, out Vector2Int lo, out Vector2Int hi)
        {
            lo = new Vector2Int(Mathf.Max(0, Mathf.FloorToInt((a.x - min.x) / Step.x) - 1), Mathf.Max(0, Mathf.FloorToInt((a.y - min.y) / Step.y) - 1));
            hi = new Vector2Int(Mathf.Min(Width - 1, Mathf.FloorToInt((b.x - min.x) / Step.x) + 1), Mathf.Min(Height - 1, Mathf.FloorToInt((b.y - min.y) / Step.y) + 1));
        }

        private static bool EdgesEnter(Vector2[] polygon, Vector2 lo, Vector2 hi)
        {
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if (FloorGeometry.EntersInterior(polygon[j], polygon[i], lo, hi))
                    return true;
            return false;
        }

        private static bool Overlaps(Vector2[] polygon, Vector2 lo, Vector2 hi) => EdgesEnter(polygon, lo, hi) || (polygon.Length >= 3 && FloorGeometry.Contains(polygon, (lo + hi) * .5f));
        internal void MarkWorld(Vector2[] polygon, TileClearance reason) => MarkLocal(Array.ConvertAll(polygon, Local), reason);
        private void MarkLocal(Vector2[] polygon, TileClearance reason)
        {
            FloorGeometry.Bounds(polygon, out var a, out var b);
            Range(a, b, out var lo, out var hi);
            for (int z = lo.y; z <= hi.y; z++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    int i = z * Width + x;
                    var corner = Corner(x, z);
                    // Include very thin supports exactly on cell boundaries on both sides.
                    var edgeTolerance = Vector2.one * (FloorGeometry.Epsilon * 3);
                    if (Cells[i] == TileClearance.Clear && Overlaps(polygon, corner - edgeTolerance, corner + Step + edgeTolerance))
                        Cells[i] = reason;
                }
        }

        internal TileClearance Check(Vector2[] footprint)
        {
            var shape = Array.ConvertAll(footprint, Local);
            FloorGeometry.Bounds(shape, out var a, out var b);
            var max = Corner(Width, Height);
            const float e = FloorGeometry.Epsilon;
            if (a.x < min.x - e || a.y < min.y - e || b.x > max.x + e || b.y > max.y + e)
                return TileClearance.OutsideFloor;
            Range(a, b, out var lo, out var hi);
            for (int z = lo.y; z <= hi.y; z++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    var reason = Cells[z * Width + x];
                    if (reason == TileClearance.Clear)
                        continue;
                    var corner = Corner(x, z);
                    if (Overlaps(shape, corner, corner + Step))
                        return reason;
                }

            return TileClearance.Clear;
        }

        internal bool ChecksEveryTile(BoardPlacement fit)
        {
            if (!fit.Layout.IsPlayable || Mathf.Abs(Floor.Height - fit.FloorHeight) > .025f)
                return false;
            for (int z = 0; z < fit.Rows; z++)
                for (int x = 0; x < fit.Columns; x++)
                    if (fit.Layout.HasPane(x, z) && Check(fit.TileOutline(x, z, FootMargin)) != TileClearance.Clear)
                        return false;
            return true;
        }

        // Histogram of consecutive clear rows. Only maximal rectangles large enough to play
        // enter the existing fitter; a detailed triangle mesh never enters its contact search.
        internal float AddClearRectangles(List<RoomFloor> output)
        {
            float minimum = BoardState.MinAxis * BoardState.MinPitch + 2 * FootMargin;
            float largestSquare = 0;
            var heights = new int[Width];
            var stack = new int[Width];
            var nextBlocked = new int[Width + 1];
            for (int z = 0; z < Height; z++)
            {
                for (int x = 0; x < Width; x++)
                {
                    heights[x] = Cells[z * Width + x] == TileClearance.Clear ? heights[x] + 1 : 0;
                    nextBlocked[x + 1] = nextBlocked[x] + (z + 1 < Height && Cells[(z + 1) * Width + x] == TileClearance.Clear ? 0 : 1);
                }

                int count = 0;
                for (int x = 0; x <= Width; x++)
                {
                    int h = x < Width ? heights[x] : 0;
                    while (count > 0 && heights[stack[count - 1]] > h)
                    {
                        int rows = heights[stack[--count]];
                        if (count > 0 && heights[stack[count - 1]] == rows)
                            continue;
                        int left = count == 0 ? 0 : stack[count - 1] + 1;
                        float shortSide = Mathf.Min((x - left) * Step.x, rows * Step.y);
                        largestSquare = Mathf.Max(largestSquare, shortSide);
                        if (output == null || shortSide + FloorGeometry.Epsilon < minimum || nextBlocked[x] == nextBlocked[left])
                            continue;
                        output.Add(new RoomFloor(Rect(left, z - rows + 1, x - left, rows), new List<Vector2[]>(), Floor.Height));
                    }

                    if (x < Width)
                        stack[count++] = x;
                }
            }
            return largestSquare;
        }

        // Subtract each observed triangle from the pieces still missing. Duplicate triangles cannot
        // fill a hole by double-counting area. Excessive fragmentation is an uncertain measurement.
        private static bool Covered(Vector2 lo, Vector2 hi, List<Triangle> triangles)
        {
            if (triangles == null)
                return false;
            var remaining = new List<List<Point>>
            {
                new List<Point>
                {
                    new Point(lo),
                    new Point(hi.x, lo.y),
                    new Point(hi),
                    new Point(lo.x, hi.y)
                }
            };
            foreach (var triangle in triangles)
            {
                var a = new Point(triangle.A);
                var b = new Point(triangle.B);
                var c = new Point(triangle.C);
                var next = new List<List<Point>>();
                foreach (var piece in remaining)
                {
                    var inside = piece;
                    Split(ref inside, a, b, next);
                    Split(ref inside, b, c, next);
                    Split(ref inside, c, a, next);
                    if (next.Count > 64)
                        return false;
                }

                if (next.Count == 0)
                    return true;
                remaining = next;
            }

            return false;
        }

        // Double precision keeps adjacent triangles watertight when repeatedly clipping a cell.
        private readonly struct Point
        {
            internal readonly double X, Y;
            internal Point(Vector2 p) : this(p.x, p.y)
            {
            }

            internal Point(double x, double y)
            {
                X = x;
                Y = y;
            }
        }

        private static double Side(Point a, Point b, Point p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        private static void Split(ref List<Point> polygon, Point a, Point b, List<List<Point>> remaining)
        {
            if (polygon == null)
                return;
            int outsideCount = 0;
            foreach (var p in polygon)
                if (Side(a, b, p) < 0)
                    outsideCount++;
            if (outsideCount == 0)
                return;
            if (outsideCount == polygon.Count)
            {
                remaining.Add(polygon);
                polygon = null;
                return;
            }

            var inside = new List<Point>(polygon.Count + 1);
            var outside = new List<Point>(polygon.Count + 1);
            var last = polygon[polygon.Count - 1];
            double d = Side(a, b, last);
            foreach (var p in polygon)
            {
                double next = Side(a, b, p);
                if ((d < 0) != (next < 0))
                {
                    double t = d / (d - next);
                    var intersection = new Point(last.X + (p.X - last.X) * t, last.Y + (p.Y - last.Y) * t);
                    inside.Add(intersection);
                    outside.Add(intersection);
                }

                if (next >= 0)
                    inside.Add(p);
                else
                    outside.Add(p);
                last = p;
                d = next;
            }

            if (HasArea(outside))
                remaining.Add(outside);
            polygon = HasArea(inside) ? inside : null;
        }

        private static bool HasArea(List<Point> polygon)
        {
            if (polygon.Count < 3)
                return false;
            double twiceArea = 0;
            for (int i = 1; i + 1 < polygon.Count; i++)
                twiceArea += Side(polygon[0], polygon[i], polygon[i + 1]);
            return Math.Abs(twiceArea) > 1e-15;
        }
    }
}
