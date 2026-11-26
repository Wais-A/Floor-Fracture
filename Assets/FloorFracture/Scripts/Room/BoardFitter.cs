using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FloorFracture
{
    public readonly struct BoardPlacement
    {
        public readonly BoardLayout Layout;
        public int Columns => Layout.Columns;
        public int Rows => Layout.Rows;
        public readonly Vector2 Center;
        public readonly float FloorHeight;
        public readonly float AngleDegrees;
        public readonly float Pitch;

        public Pose Pose => new Pose(new Vector3(Center.x, FloorHeight, Center.y),
            Quaternion.Euler(0f, -AngleDegrees, 0f));

        public BoardPlacement(Vector2 center, float floorHeight, float angleDegrees, int columns, int rows,
            float pitch = BoardState.MinPitch)
            : this(center, floorHeight, angleDegrees, BoardLayout.Rectangle(columns, rows), pitch) { }

        internal BoardPlacement(Vector2 center, float floorHeight, float angleDegrees, BoardLayout layout, float pitch)
        {
            Center = center;
            FloorHeight = floorHeight;
            AngleDegrees = angleDegrees;
            Layout = layout;
            Pitch = pitch;
        }

        public Vector2 ToWorldXZ(float x, float z)
        {
            float radians = AngleDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return Center + new Vector2(c * x - s * z, s * x + c * z);
        }

        public Vector2[] FootprintOutline()
        {
            float x = Columns * Pitch / 2f;
            float z = Rows * Pitch / 2f;
            return new[] { ToWorldXZ(-x, -z), ToWorldXZ(x, -z), ToWorldXZ(x, z), ToWorldXZ(-x, z) };
        }

        internal Vector2[] TileOutline(int column, int row, float margin)
        {
            float x = (column - Columns * .5f) * Pitch;
            float z = (row - Rows * .5f) * Pitch;
            return new[]
            {
                ToWorldXZ(x - margin, z - margin),
                ToWorldXZ(x + Pitch + margin, z - margin),
                ToWorldXZ(x + Pitch + margin, z + Pitch + margin),
                ToWorldXZ(x - margin, z + Pitch + margin)
            };
        }
    }

    public static class BoardFitter
    {
        // Keep the rectangle contact search bounded. The connected fitter can examine
        // a whole surveyed floor and uses these rectangles as additional placement candidates.
        private const int RectangleSearchLimit = 64;
        private const float PitchPrecision = 0.0005f;

        private readonly struct Coverage : IComparable<Coverage>
        {
            // Cover as many physical square meters as possible. At equal area prefer
            // the wider short side, then larger panes for an identical footprint.
            // Round areas to cm² and lengths to mm so equivalent rotated fits tie.
            private readonly int glassAreaCm2, shortSideMm, paneWidthMm;

            public Coverage(int columns, int rows, float pitch)
            {
                glassAreaCm2 = Mathf.RoundToInt(columns * rows * pitch * pitch * 10000f);
                shortSideMm = Mathf.RoundToInt(Math.Min(columns, rows) * pitch * 1000f);
                paneWidthMm = Mathf.RoundToInt(pitch * 1000f);
            }

            public int CompareTo(Coverage other)
            {
                int area = glassAreaCm2.CompareTo(other.glassAreaCm2);
                if (area != 0) return area;
                int width = shortSideMm.CompareTo(other.shortSideMm);
                return width != 0 ? width : paneWidthMm.CompareTo(other.paneWidthMm);
            }

            public float MinimumCompetitivePitch(int columns, int rows)
            {
                // Include all pitches that could tie this rounded area.
                return Mathf.Sqrt(Math.Max(0, glassAreaCm2 - 1) / (10000f * columns * rows));
            }
        }

        private readonly struct Candidate
        {
            public readonly Frame Frame;
            public readonly int Columns, Rows, Order;
            public readonly float MaximumPitch;
            public readonly Coverage MaximumCoverage;
            public Candidate(Frame frame, int columns, int rows, float maximumPitch, int order)
            {
                Frame = frame;
                Columns = columns;
                Rows = rows;
                Order = order;
                MaximumPitch = maximumPitch;
                MaximumCoverage = new Coverage(columns, rows, maximumPitch);
            }
        }

        private static int CompareCenters(Vector2 a, Vector2 b, Vector2 target)
        {
            int distance = (a - target).sqrMagnitude.CompareTo((b - target).sqrMagnitude);
            if (distance != 0) return distance;
            int x = a.x.CompareTo(b.x);
            return x != 0 ? x : a.y.CompareTo(b.y);
        }

        public static bool TryFind(IReadOnlyList<RoomFloor> floors, Vector2 preferredCenter, out BoardPlacement placement,
            CancellationToken cancellation = default, IReadOnlyList<Vector2> suggestedCenters = null)
        {
            return TryFindOnSurfaces(floors != null && floors.Count > 1 ? FloorPatchUnion.Join(floors) : floors,
                preferredCenter, out placement, cancellation, suggestedCenters);
        }

        // The room reader joins native contours on the main thread before dispatching this
        // managed rectangle search. Revalidation reads fresh contours through the same reader.
        internal static bool TryFindOnSurfaces(IReadOnlyList<RoomFloor> floors, Vector2 preferredCenter, out BoardPlacement placement,
            CancellationToken cancellation = default, IReadOnlyList<Vector2> suggestedCenters = null,
            float margin = 0, bool alignedSurveyRectangles = false)
        {
            placement = default;
            if (floors == null || !FloorGeometry.Finite(preferredCenter)) return false;
            var suggestions = new List<Vector2>();
            if (suggestedCenters != null)
                foreach (var suggestion in suggestedCenters)
                    if (FloorGeometry.Finite(suggestion)) suggestions.Add(suggestion);
            suggestions.Sort((a, b) => CompareCenters(a, b, preferredCenter));
            var candidates = new List<Candidate>();
            foreach (var floor in floors)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!IsUsable(floor)) continue;
                var angles = new List<float>();
                if (!alignedSurveyRectangles) angles.Add(0f);
                AddEdgeDirections(floor.Boundary, angles);
                if (!alignedSurveyRectangles)
                {
                    foreach (var obstacle in floor.Obstacles) AddEdgeDirections(obstacle, angles);
                    for (int angle = 1; angle < 90; ++angle) AddAngle(angle, angles);
                }
                foreach (float angle in angles)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var frame = new Frame(floor, angle, margin);
                    frame.PrepareSearch(preferredCenter, suggestions);
                    // Every legal footprint contains this one. In a cluttered scan a
                    // single failed search removes this orientation for every shape.
                    if (frame.PitchLimit(BoardState.MinAxis, BoardState.MinAxis) < BoardState.MinPitch - FloorGeometry.Epsilon ||
                        !frame.TryFindCenter(HalfSize(BoardState.MinAxis, BoardState.MinAxis, BoardState.MinPitch),
                            cancellation, out _)) continue;
                    for (int columns = BoardState.MinAxis; columns <= frame.MaximumCounts.x; ++columns)
                    {
                        int maxRows = Math.Min(frame.MaximumCounts.y, RectangleSearchLimit / columns);
                        for (int rows = BoardState.MinAxis; rows <= maxRows; ++rows)
                        {
                            float upper = frame.PitchLimit(columns, rows);
                            if (upper >= BoardState.MinPitch - FloorGeometry.Epsilon)
                                candidates.Add(new Candidate(frame, columns, rows, Mathf.Max(BoardState.MinPitch, upper),
                                    candidates.Count));
                        }
                    }
                }
            }
            candidates.Sort((a, b) =>
            {
                int coverage = b.MaximumCoverage.CompareTo(a.MaximumCoverage);
                return coverage != 0 ? coverage : a.Order.CompareTo(b.Order);
            });
            Coverage bestCoverage = default;
            int bestDistanceMm = int.MaxValue;
            int bestOrder = int.MaxValue;
            foreach (var candidate in candidates)
            {
                cancellation.ThrowIfCancellationRequested();
                int upperComparison = candidate.MaximumCoverage.CompareTo(bestCoverage);
                if (upperComparison < 0 ||
                    (upperComparison == 0 && bestDistanceMm == 0 && candidate.Order >= bestOrder)) break;
                int columns = candidate.Columns, rows = candidate.Rows;
                Frame frame = candidate.Frame;
                float pitch = candidate.MaximumPitch;
                if (!frame.TryFindCenter(HalfSize(columns, rows, pitch), cancellation, out var center))
                {
                    // Smaller rectangles nest inside larger ones. Only search pitches that
                    // can improve the current winner and retain an explicitly certified fit.
                    float low = Mathf.Max(BoardState.MinPitch, bestCoverage.MinimumCompetitivePitch(columns, rows));
                    float high = pitch;
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
                Vector2 world = frame.ToWorld(center);
                var coverage = new Coverage(columns, rows, pitch);
                int comparison = coverage.CompareTo(bestCoverage);
                // Projecting the same Meta hint through different frames can differ
                // by a few float bits. Compare physical distance at millimeter precision.
                int distanceMm = Mathf.RoundToInt((world - preferredCenter).magnitude * 1000f);
                if (comparison < 0 || (comparison == 0 &&
                    (distanceMm > bestDistanceMm || (distanceMm == bestDistanceMm && candidate.Order >= bestOrder)))) continue;
                bestCoverage = coverage;
                bestDistanceMm = distanceMm;
                bestOrder = candidate.Order;
                placement = new BoardPlacement(world, frame.Height, frame.Angle, columns, rows, pitch);
            }
            return bestOrder != int.MaxValue;
        }

        public static bool Fits(RoomFloor floor, BoardPlacement placement, float margin = 0)
        {
            if (!IsUsable(floor) || !FloorGeometry.Finite(placement.Center) ||
                !FloorGeometry.Finite(placement.AngleDegrees) || !FloorGeometry.Finite(placement.FloorHeight) ||
                Mathf.Abs(floor.Height - placement.FloorHeight) > 0.025f ||
                !placement.Layout.IsPlayable ||
                !(placement.Pitch >= BoardState.CompactMinPitch && placement.Pitch <= BoardState.MaxPitch)) return false;
            var frame = new Frame(floor, placement.AngleDegrees);
            var center = frame.ToLocal(placement.Center);
            if (placement.Layout.IsRectangle)
                return frame.Admits(center, HalfSize(placement.Columns, placement.Rows, placement.Pitch) + Vector2.one * margin);
            for (int row = 0; row < placement.Rows; row++)
                for (int col = 0; col < placement.Columns; col++)
                {
                    if (!placement.Layout.HasPane(col, row)) continue;
                    var offset = new Vector2(col + .5f - placement.Columns * .5f, row + .5f - placement.Rows * .5f);
                    if (!frame.Admits(center + offset * placement.Pitch, Vector2.one * (placement.Pitch * .5f + margin))) return false;
                }
            return true;
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
            columns * pitch / 2f, rows * pitch / 2f);

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
                return Max.x >= min.x && Max.y >= min.y && Min.x <= max.x && Min.y <= max.y &&
                    FloorGeometry.EdgesEnter(Points, min, max);
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

        // A clear rectangle must contain a clear rectangular set of lattice points.
        // This cheap upper bound eliminates impossible sizes. All accepted placements
        // still go through the exact floor and obstacle tests.
        private sealed class ClearanceGrid
        {
            private readonly float spacing;
            private readonly float roundingMargin;
            private readonly int[] maxColumnsForHeight;

            public ClearanceGrid(Shape floor, List<Shape> obstacles)
            {
                var span = floor.Max - floor.Min;
                spacing = Mathf.Max(.12f, Mathf.Max(span.x, span.y) / 64f);
                float coordinateScale = Mathf.Max(Mathf.Max(Mathf.Abs(floor.Min.x), Mathf.Abs(floor.Max.x)),
                    Mathf.Max(Mathf.Abs(floor.Min.y), Mathf.Abs(floor.Max.y)));
                roundingMargin = .001f + coordinateScale * .000002f;
                int columns = Mathf.CeilToInt(span.x / spacing) + 1;
                int rows = Mathf.CeilToInt(span.y / spacing) + 1;
                maxColumnsForHeight = new int[rows + 1];
                var heights = new int[columns];
                var stack = new int[columns];
                for (int y = 0; y < rows; y++)
                {
                    for (int x = 0; x < columns; x++)
                    {
                        var point = floor.Min + new Vector2(x * spacing, y * spacing);
                        heights[x] = IsClear(point) ? heights[x] + 1 : 0;
                    }
                    // For every run of clear rows, record the widest set of
                    // neighboring columns reaching at least that height.
                    int count = 0;
                    for (int x = 0; x <= columns; x++)
                    {
                        int height = x < columns ? heights[x] : 0;
                        while (count > 0 && heights[stack[count - 1]] > height)
                        {
                            int runHeight = heights[stack[--count]];
                            int left = count == 0 ? 0 : stack[count - 1] + 1;
                            maxColumnsForHeight[runHeight] = Math.Max(maxColumnsForHeight[runHeight], x - left);
                        }
                        if (x < columns) stack[count++] = x;
                    }
                }
                for (int h = rows - 1; h >= 1; h--)
                    maxColumnsForHeight[h] = Math.Max(maxColumnsForHeight[h], maxColumnsForHeight[h + 1]);

                bool IsClear(Vector2 point)
                {
                    if (!FloorGeometry.Contains(floor.Points, point)) return false;
                    foreach (var shape in obstacles)
                    {
                        if (shape.Points.Length < 3 || point.x < shape.Min.x || point.x > shape.Max.x ||
                            point.y < shape.Min.y || point.y > shape.Max.y) continue;
                        if (FloorGeometry.Contains(shape.Points, point)) return false;
                    }
                    return true;
                }
            }

            public bool MayFit(Vector2 half)
            {
                // Round down and leave a margin on the rectangle edges so an exact
                // fit, a thin aisle or floating-point edge contact cannot be discarded.
                int columns = Mathf.FloorToInt((2f * half.x - roundingMargin) / spacing);
                int rows = Mathf.FloorToInt((2f * half.y - roundingMargin) / spacing);
                return columns <= 0 || rows <= 0 || (rows < maxColumnsForHeight.Length && maxColumnsForHeight[rows] >= columns);
            }
        }

        private sealed class Frame
        {
            public readonly float Height, Angle;
            public readonly Vector2Int MaximumCounts;
            private readonly Vector2 right, forward;
            private readonly Shape floor;
            private readonly List<Shape> obstacles = new List<Shape>();
            private readonly List<Vector2> failedHalfSizes = new List<Vector2>();
            private readonly List<Vector2> suggestions = new List<Vector2>();
            private readonly List<Vector3> spanLimits = new List<Vector3>();
            private Vector2 preferred;
            private ClearanceGrid clearanceGrid;

            private readonly float footMargin;

            public Frame(RoomFloor source, float angle, float margin = 0)
            {
                footMargin = margin;
                Angle = angle;
                Height = source.Height;
                right = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                forward = new Vector2(-right.y, right.x);
                floor = Project(source.Boundary);
                var span = floor.Max - floor.Min;
                MaximumCounts = new Vector2Int(AxisCount(span.x), AxisCount(span.y));
                foreach (var outline in source.Obstacles) obstacles.Add(Project(outline));
            }

            private static int AxisCount(float meters) => Math.Min(RectangleSearchLimit / BoardState.MinAxis,
                Mathf.FloorToInt((meters + FloorGeometry.Epsilon) / BoardState.MinPitch));

            public Vector2 ToLocal(Vector2 point) => new Vector2(Vector2.Dot(point, right), Vector2.Dot(point, forward));
            public Vector2 ToWorld(Vector2 point) => right * point.x + forward * point.y;

            public void PrepareSearch(Vector2 preferredCenter, List<Vector2> suggestedCenters)
            {
                preferred = ToLocal(preferredCenter);
                foreach (var suggestion in suggestedCenters) suggestions.Add(ToLocal(suggestion));
                // The board's projection onto every floor-edge normal must fit between
                // the floor's extreme points. In particular, a diagonal frame cannot
                // inflate the apparent width of a long, narrow room.
                var span = floor.Max - floor.Min;
                if ((floor.Points.Length > 6 || obstacles.Count >= 3) && FloorGeometry.Finite(span))
                    clearanceGrid = new ClearanceGrid(floor, obstacles);
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
                    limit = Mathf.Min(limit, (span.z - 2f * footMargin * (span.x + span.y)) /
                        (columns * span.x + rows * span.y));
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
                    // A large furniture footprint can contain the whole board.
                    if (shape.Points.Length >= 3 && center.x >= shape.Min.x && center.x <= shape.Max.x &&
                        center.y >= shape.Min.y && center.y <= shape.Max.y &&
                        FloorGeometry.Contains(shape.Points, center)) return false;
                }
                return true;
            }

            public bool TryFindCenter(Vector2 half, CancellationToken cancellation, out Vector2 center)
            {
                half += Vector2.one * footMargin;
                if (clearanceGrid != null && !clearanceGrid.MayFit(half))
                {
                    center = default;
                    return false;
                }
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
                candidates.Sort((a, b) => CompareCenters(a, b, target));
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
