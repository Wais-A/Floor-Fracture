using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.U2D.Physics;
using UnityEngine;

namespace FloorFracture
{
    internal static class FloorPatchUnion
    {
        // Distinct steps and sloping patches keep their own plane. One millimetre permits
        // small transform differences between anchors measured on the same level surface.
        internal const float CoplanarRange = .001f;
        // Chained neighbours (0, .8, 1.6 mm) still form one surface, but a group never spans a real step.
        private const float GroupSpan = 2 * CoplanarRange;
        internal const float MillimetresPerMetre = 1000f;

        public static List<RoomFloor> Join(IReadOnlyList<RoomFloor> patches)
        {
            var ordered = patches.Where(BoardFitter.IsUsable).OrderBy(p => p.LowestY).ToList();
            var result = new List<RoomFloor>();
            for (int first = 0; first < ordered.Count;)
            {
                float low = ordered[first].LowestY, high = ordered[first].HighestY;
                int end = first + 1;
                while (end < ordered.Count && ordered[end].LowestY - high <= CoplanarRange &&
                       Mathf.Max(high, ordered[end].HighestY) - low <= GroupSpan)
                {
                    high = Mathf.Max(high, ordered[end].HighestY);
                    ++end;
                }
                if (end == first + 1) result.Add(ordered[first]);
                else
                {
                    var group = ordered.GetRange(first, end - first);
                    var obstacles = new List<Vector2[]>();
                    foreach (var patch in group)
                        foreach (var obstacle in patch.Obstacles)
                            if (!obstacles.Exists(existing => existing.SequenceEqual(obstacle))) obstacles.Add(obstacle);
                    // If the native composer rejects a contour, only individual measured patches are safe.
                    var regions = Union(group, obstacles, low, high);
                    if (regions != null) result.AddRange(regions);
                    else foreach (var patch in group)
                        result.Add(new RoomFloor(patch.Boundary, obstacles, patch.LowestY, patch.HighestY));
                }
                first = end;
            }
            return result;
        }

        private static List<RoomFloor> Union(List<RoomFloor> patches, List<Vector2[]> obstacles, float low, float high)
        {
            using var composer = PhysicsComposer.Create(Allocator.Temp);
            var origin = patches[0].Boundary[0];
            foreach (var patch in patches)
            {
                // Native physics welds very short edges. Compose in millimetres so a narrow
                // measured gap survives, then convert the resulting contours back to world XZ.
                AddLayer(composer, patch.Boundary, origin, PhysicsComposer.Operation.OR, 0);
            }
            using var paths = composer.CreateChainGeometry(out var vertices, Allocator.Temp);
            using (vertices)
            {
                if (composer.rejectedGeometryCount != 0 || paths.Length == 0) return null;
                var regions = new List<RoomFloor>();
                var holes = new List<Vector2[]>();
                foreach (var path in paths)
                {
                    var outline = Array.ConvertAll(path.vertices.ToArray(), p => origin + p / MillimetresPerMetre);
                    if (!FloorGeometry.SimpleFloor(outline)) return null;
                    if (SignedArea(outline) > 0)
                        regions.Add(new RoomFloor(outline, new List<Vector2[]>(obstacles), low, high));
                    else holes.Add(outline);
                }
                // Each clockwise contour belongs to its smallest enclosing outer loop.
                // This also preserves an island that sits inside a hole in another region.
                foreach (var hole in holes)
                {
                    var parent = regions.Where(region => FloorGeometry.Contains(region.Boundary, hole[0]))
                        .OrderBy(region => SignedArea(region.Boundary)).FirstOrDefault();
                    if (parent == null) return null;
                    parent.Obstacles.Add(hole);
                }
                return regions;
            }
        }

        internal static void AddLayer(PhysicsComposer composer, Vector2[] outline, Vector2 origin,
            PhysicsComposer.Operation operation, int order)
        {
            var points = Array.ConvertAll(outline, p => (p - origin) * MillimetresPerMetre);
            composer.AddLayer(points, PhysicsTransform.identity, operation, order, reverseWinding: SignedArea(points) < 0);
        }

        internal static double SignedArea(Vector2[] points)
        {
            double area = 0;
            var origin = points[0];
            for (int i = 1; i + 1 < points.Length; ++i)
            {
                var a = points[i] - origin;
                var b = points[i + 1] - origin;
                area += (double)a.x * b.y - (double)a.y * b.x;
            }
            return area * .5;
        }
    }
}
